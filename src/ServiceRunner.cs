using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace LumPanel
{
    public enum ServiceState { Stopped, Starting, Running, Stopping, Crashed, External }

    public enum LineLevel { Info, Warn, Error, Input, Panel }

    public sealed class LogLine
    {
        public long Seq;
        public DateTime Time;
        public string Text;
        public LineLevel Level;
    }

    /// <summary>Runs one program hidden (no console window), keeps its output, takes console input.</summary>
    public sealed class ServiceRunner
    {
        static readonly Regex Ansi = new Regex(@"\x1B\[[0-9;?]*[ -/]*[@-~]|\x1B\][^\x07]*\x07", RegexOptions.Compiled);
        static readonly Regex Joined = new Regex(@"\b([A-Za-z0-9_]{2,16}) (joined the game|oyuna katıldı)", RegexOptions.Compiled);
        static readonly Regex Left = new Regex(@"\b([A-Za-z0-9_]{2,16}) (left the game|oyundan ayrıldı)", RegexOptions.Compiled);
        static readonly Regex ListLine = new Regex(@"There are (\d+) of a max(?: of)? (\d+) players online:?\s*(.*)$", RegexOptions.Compiled);
        static readonly Regex Ready = new Regex(@"Done \(\d|Listening on|listening on|Giriş yapıldı|serving initial configuration|Web panel:", RegexOptions.Compiled);

        public ServiceConfig Config { get; private set; }
        public ServiceState State { get; private set; } = ServiceState.Stopped;
        public DateTime? StartedAt { get; private set; }
        public int RootPid => process != null && !process.HasExited ? process.Id : externalPid;
        public double CpuPercent { get; set; }
        /// <summary>Last 60 CPU samples (2 s apart), oldest first.</summary>
        public float[] CpuHistory { get; private set; } = new float[60];
        public void PushCpu(double v)
        {
            var next = new float[CpuHistory.Length];
            Array.Copy(CpuHistory, 1, next, 0, next.Length - 1);
            next[next.Length - 1] = (float)Math.Max(0, Math.Min(100, v));
            CpuHistory = next;
        }
        public long MemoryBytes { get; set; }
        public int ProcessCount { get; set; }
        public int MaxPlayers { get; private set; }
        public int RestartsInWindow => crashTimes.Count(t => DateTime.Now - t < TimeSpan.FromMinutes(10));
        public string LastExit { get; private set; } = "";

        public event Action<ServiceRunner, string> Event;   // panel events (started, crashed...)
        public event Action<ServiceRunner> StateChanged;
        /// <summary>An ERROR / exception line from the program itself (not panel messages).</summary>
        public event Action<ServiceRunner, LogLine> ErrorLine;

        readonly object gate = new object();
        readonly List<LogLine> lines = new List<LogLine>();
        readonly SortedSet<string> players = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        readonly List<DateTime> crashTimes = new List<DateTime>();
        long seq;
        Process process;
        int externalPid;
        bool stopRequested;
        bool restartAfterStop;
        Timer readyTimer;
        Timer killTimer;
        int maxLines;

        public ServiceRunner(ServiceConfig config, int maxLines)
        {
            Config = config;
            this.maxLines = Math.Max(500, maxLines);
        }

        public void UpdateConfig(ServiceConfig config, int maxLines)
        {
            Config = config;
            this.maxLines = Math.Max(500, maxLines);
        }

        public override string ToString() => Config.Name;

        public bool IsAlive => State == ServiceState.Starting || State == ServiceState.Running || State == ServiceState.Stopping;
        public bool IsOwned => process != null && !SafeExited(process);

        public string[] Players { get { lock (gate) return players.ToArray(); } }

        public List<LogLine> LinesAfter(long afterSeq)
        {
            lock (gate)
            {
                if (lines.Count == 0 || lines[lines.Count - 1].Seq <= afterSeq) return new List<LogLine>();
                int i = lines.Count - 1;
                while (i > 0 && lines[i - 1].Seq > afterSeq) i--;
                return lines.GetRange(i, lines.Count - i);
            }
        }

        public void ClearLog() { lock (gate) lines.Clear(); }

        public void AddPanelLine(string text) => Append(text, LineLevel.Panel);

        void Append(string raw, LineLevel? forced = null)
        {
            if (raw == null) return;
            string text = Ansi.Replace(raw, "").TrimEnd('\r');
            LineLevel level = forced ?? Classify(text);
            LogLine entry;
            lock (gate)
            {
                entry = new LogLine { Seq = ++seq, Time = DateTime.Now, Text = text, Level = level };
                lines.Add(entry);
                if (lines.Count > maxLines) lines.RemoveRange(0, lines.Count - maxLines);
            }
            if (forced == null) Inspect(text);
            if (level == LineLevel.Error && forced != LineLevel.Panel && forced != LineLevel.Input) ErrorLine?.Invoke(this, entry);
        }

        static LineLevel Classify(string t)
        {
            if (t.IndexOf("ERROR", StringComparison.Ordinal) >= 0 || t.IndexOf("SEVERE", StringComparison.Ordinal) >= 0 ||
                t.IndexOf("Exception", StringComparison.Ordinal) >= 0 || t.StartsWith("Traceback") || t.Contains(" HATA ")) return LineLevel.Error;
            if (t.IndexOf("WARN", StringComparison.Ordinal) >= 0 || t.Contains("UYARI")) return LineLevel.Warn;
            return LineLevel.Info;
        }

        void Inspect(string text)
        {
            if (State == ServiceState.Starting && Ready.IsMatch(text)) MarkRunning();
            if (Config.Kind != ServiceKinds.Minecraft && Config.Kind != ServiceKinds.Proxy) return;
            Match m;
            if ((m = Joined.Match(text)).Success) lock (gate) players.Add(m.Groups[1].Value);
            else if ((m = Left.Match(text)).Success) lock (gate) players.Remove(m.Groups[1].Value);
            else if ((m = ListLine.Match(text)).Success)
            {
                MaxPlayers = int.Parse(m.Groups[2].Value);
                var names = m.Groups[3].Value.Split(new[] { ',', ' ' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(n => Regex.Replace(n, @"^\[[^\]]*\]|[^A-Za-z0-9_]", "")).Where(n => n.Length >= 2);
                lock (gate) { players.Clear(); foreach (var n in names) players.Add(n); }
            }
        }

        void SetState(ServiceState s)
        {
            if (State == s) return;
            State = s;
            StateChanged?.Invoke(this);
        }

        void MarkRunning()
        {
            if (State != ServiceState.Starting) return;
            SetState(ServiceState.Running);
            Event?.Invoke(this, "çalışıyor");
        }

        public string Start()
        {
            if (IsOwned) return "Zaten çalışıyor.";
            if (State == ServiceState.External) return "Bu program panel dışında çalışıyor. Önce onu kapat (Durdur).";
            string program = Environment.ExpandEnvironmentVariables(Config.Program ?? "").Trim().Trim('"');
            string dir = Environment.ExpandEnvironmentVariables(Config.WorkingDir ?? "").Trim().Trim('"');
            if (program.Length == 0) return "Program ayarlanmamış (Ayarlar sayfası).";
            if (dir.Length > 0 && !Directory.Exists(dir)) return "Klasör bulunamadı: " + dir;

            var psi = new ProcessStartInfo
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                WorkingDirectory = dir.Length > 0 ? dir : AppConfig.Folder,
            };
            Encoding enc;
            try { enc = Encoding.GetEncoding(string.IsNullOrWhiteSpace(Config.Encoding) ? "utf-8" : Config.Encoding); }
            catch { enc = new UTF8Encoding(false); }
            psi.StandardOutputEncoding = enc;
            psi.StandardErrorEncoding = enc;

            string ext = Path.GetExtension(program).ToLowerInvariant();
            if (ext == ".bat" || ext == ".cmd")
            {
                psi.FileName = Environment.GetEnvironmentVariable("ComSpec") ?? "cmd.exe";
                psi.Arguments = $"/d /c \"\"{program}\" {Config.Arguments}\"";
            }
            else
            {
                psi.FileName = program;
                psi.Arguments = Config.Arguments ?? "";
            }
            if (enc.WebName == "utf-8")
            {
                psi.EnvironmentVariables["PYTHONUTF8"] = "1";
                psi.EnvironmentVariables["PYTHONIOENCODING"] = "utf-8";
                psi.EnvironmentVariables["PYTHONUNBUFFERED"] = "1";
                if (Config.Kind == ServiceKinds.Minecraft || Config.Kind == ServiceKinds.Proxy)
                    psi.EnvironmentVariables["JAVA_TOOL_OPTIONS"] = "-Dstdout.encoding=UTF-8 -Dstderr.encoding=UTF-8 -Dfile.encoding=UTF-8";
            }

            var p = new Process { StartInfo = psi, EnableRaisingEvents = true };
            p.OutputDataReceived += (s, e) => Append(e.Data);
            p.ErrorDataReceived += (s, e) => { if (e.Data != null) Append(e.Data, Classify(e.Data) == LineLevel.Info ? LineLevel.Warn : (LineLevel?)null); };
            p.Exited += (s, e) => OnExited((Process)s);
            try
            {
                p.Start();
            }
            catch (Exception ex)
            {
                AddPanelLine("Başlatılamadı: " + ex.Message);
                return "Başlatılamadı: " + ex.Message;
            }
            process = p;
            stopRequested = false;
            StartedAt = DateTime.Now;
            lock (gate) players.Clear();
            p.BeginOutputReadLine();
            p.BeginErrorReadLine();
            ApplyPriority(p);
            AddPanelLine($"» Başlatıldı (PID {p.Id}): {psi.FileName} {psi.Arguments}");
            SetState(ServiceState.Starting);
            Event?.Invoke(this, "başlatıldı");
            // Programs without a recognisable "ready" line count as running after 20 s.
            readyTimer?.Dispose();
            readyTimer = new Timer(_ => { if (!SafeExited(p)) MarkRunning(); }, null, 20000, Timeout.Infinite);
            return null;
        }

        void ApplyPriority(Process p)
        {
            try
            {
                if (Enum.TryParse(Config.Priority, out ProcessPriorityClass prio) && prio != ProcessPriorityClass.Normal)
                    p.PriorityClass = prio;
            }
            catch { }
        }

        public string Send(string command)
        {
            command = (command ?? "").Trim();
            if (command.Length == 0) return null;
            if (!IsOwned) return "Program panelden başlatılmadığı için komut gönderilemez.";
            try
            {
                byte[] bytes = new UTF8Encoding(false).GetBytes(command + "\n");
                if (!string.Equals(Config.Encoding, "utf-8", StringComparison.OrdinalIgnoreCase))
                {
                    try { bytes = Encoding.GetEncoding(Config.Encoding).GetBytes(command + "\n"); } catch { }
                }
                // a shutdown typed by hand is a wanted stop, not a crash to auto-restart
                string verb = command.Split(' ')[0].ToLowerInvariant();
                if (State != ServiceState.Stopping && (command.Equals(Config.EffectiveStopCommand, StringComparison.OrdinalIgnoreCase) ||
                    ((Config.Kind == ServiceKinds.Minecraft || Config.Kind == ServiceKinds.Proxy) && (verb == "stop" || verb == "end" || verb == "shutdown"))))
                {
                    stopRequested = true;
                    restartAfterStop = false;
                    SetState(ServiceState.Stopping);
                }
                var stream = process.StandardInput.BaseStream;
                stream.Write(bytes, 0, bytes.Length);
                stream.Flush();
                Append("> " + command, LineLevel.Input);
                return null;
            }
            catch (Exception ex)
            {
                return "Komut gönderilemedi: " + ex.Message;
            }
        }

        public void Stop(bool restart = false)
        {
            if (State == ServiceState.External)
            {
                if (externalPid > 0) KillTree(externalPid);
                AddPanelLine($"» Panel dışındaki süreç (PID {externalPid}) kapatıldı.");
                externalPid = 0;
                SetState(ServiceState.Stopped);
                if (restart) Start();
                return;
            }
            if (!IsOwned) { if (restart) Start(); return; }
            stopRequested = true;
            restartAfterStop = restart;
            SetState(ServiceState.Stopping);
            string stopCmd = Config.EffectiveStopCommand;
            if (stopCmd.Length > 0 && Send(stopCmd) == null)
            {
                AddPanelLine($"» Kapatılıyor ('{stopCmd}' gönderildi, en fazla {Config.StopTimeoutSec} sn beklenecek)...");
                var p = process;
                killTimer?.Dispose();
                killTimer = new Timer(_ =>
                {
                    if (!SafeExited(p)) { AddPanelLine("» Zamanında kapanmadı, zorla sonlandırılıyor."); KillTree(p.Id); }
                }, null, Math.Max(5, Config.StopTimeoutSec) * 1000, Timeout.Infinite);
            }
            else
            {
                AddPanelLine("» Kapatılıyor...");
                KillTree(process.Id);
            }
        }

        public void Kill()
        {
            if (IsOwned) { stopRequested = true; restartAfterStop = false; KillTree(process.Id); }
            else if (State == ServiceState.External && externalPid > 0) Stop();
        }

        void OnExited(Process p)
        {
            if (p != process) return;
            int code = 0;
            try { code = p.ExitCode; } catch { }
            readyTimer?.Dispose();
            killTimer?.Dispose();
            LastExit = $"{DateTime.Now:HH:mm:ss} çıkış kodu {code}";
            bool wanted = stopRequested;
            process = null;
            StartedAt = null;
            lock (gate) players.Clear();
            CpuPercent = 0; MemoryBytes = 0; ProcessCount = 0;
            if (wanted)
            {
                AddPanelLine($"» Durdu ({LastExit}).");
                SetState(ServiceState.Stopped);
                Event?.Invoke(this, "durduruldu");
                if (restartAfterStop) { restartAfterStop = false; Start(); }
                return;
            }
            crashTimes.Add(DateTime.Now);
            crashTimes.RemoveAll(t => DateTime.Now - t > TimeSpan.FromMinutes(30));
            AddPanelLine($"» HATA: Beklenmedik şekilde kapandı ({LastExit}).");
            SetState(ServiceState.Crashed);
            Event?.Invoke(this, $"ÇÖKTÜ ({LastExit})");
            if (Config.AutoRestart)
            {
                int recent = RestartsInWindow;
                if (recent > 5)
                {
                    AddPanelLine("» 10 dakikada 5'ten fazla çöktü; otomatik yeniden başlatma durduruldu.");
                    Event?.Invoke(this, "çok sık çöktü, otomatik yeniden başlatma durdu");
                    return;
                }
                int delay = Math.Min(60, 5 * recent);
                AddPanelLine($"» {delay} sn sonra otomatik yeniden başlatılacak...");
                new Timer(_ => { if (State == ServiceState.Crashed) Start(); }, null, delay * 1000, Timeout.Infinite);
            }
        }

        /// <summary>Called by the monitor: a matching process exists that the panel did not start.</summary>
        public void SetExternal(int pid)
        {
            if (IsOwned) return;
            if (pid > 0 && State != ServiceState.External)
            {
                externalPid = pid;
                SetState(ServiceState.External);
                AddPanelLine($"» Panel dışında çalışan bir kopya bulundu (PID {pid}). Çıktısı burada görünmez; Durdur ile kapatıp panelden başlatabilirsin.");
            }
            else if (pid <= 0 && State == ServiceState.External)
            {
                externalPid = 0;
                SetState(ServiceState.Stopped);
            }
            else if (pid > 0) externalPid = pid;
        }

        public static bool SafeExited(Process p)
        {
            try { return p == null || p.HasExited; } catch { return true; }
        }

        public static void KillTree(int pid)
        {
            try
            {
                using var k = Process.Start(new ProcessStartInfo("taskkill", $"/PID {pid} /T /F")
                { CreateNoWindow = true, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true });
                k?.WaitForExit(10000);
            }
            catch { }
        }
    }

    /// <summary>Parent/child map of all processes (toolhelp snapshot; no WMI, cheap).</summary>
    public static class ProcessTree
    {
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct PROCESSENTRY32
        {
            public uint dwSize, cntUsage, th32ProcessID;
            public IntPtr th32DefaultHeapID;
            public uint th32ModuleID, cntThreads, th32ParentProcessID;
            public int pcPriClassBase;
            public uint dwFlags;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string szExeFile;
        }

        [DllImport("kernel32.dll", SetLastError = true)] static extern IntPtr CreateToolhelp32Snapshot(uint flags, uint pid);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern bool Process32FirstW(IntPtr snap, ref PROCESSENTRY32 e);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern bool Process32NextW(IntPtr snap, ref PROCESSENTRY32 e);
        [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr h);

        public static Dictionary<int, List<int>> Children()
        {
            var map = new Dictionary<int, List<int>>();
            IntPtr snap = CreateToolhelp32Snapshot(0x2, 0);
            if (snap == IntPtr.Zero || snap == new IntPtr(-1)) return map;
            try
            {
                var e = new PROCESSENTRY32 { dwSize = (uint)Marshal.SizeOf(typeof(PROCESSENTRY32)) };
                if (!Process32FirstW(snap, ref e)) return map;
                do
                {
                    int parent = (int)e.th32ParentProcessID, id = (int)e.th32ProcessID;
                    if (parent == id) continue;
                    if (!map.TryGetValue(parent, out var list)) map[parent] = list = new List<int>();
                    list.Add(id);
                } while (Process32NextW(snap, ref e));
            }
            finally { CloseHandle(snap); }
            return map;
        }

        public static List<int> Tree(int root, Dictionary<int, List<int>> map)
        {
            var result = new List<int>();
            if (root <= 0) return result;
            var stack = new Stack<int>();
            stack.Push(root);
            while (stack.Count > 0 && result.Count < 64)
            {
                int id = stack.Pop();
                result.Add(id);
                if (map.TryGetValue(id, out var kids)) foreach (var k in kids) if (!result.Contains(k)) stack.Push(k);
            }
            return result;
        }
    }
}
