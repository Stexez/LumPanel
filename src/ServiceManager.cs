using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management;
using System.Net;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;

namespace LumPanel
{
    public sealed class PanelEvent
    {
        public DateTime Time;
        public string Service;
        public string Message;
        public bool Important;
    }

    public sealed class SystemStats
    {
        public double CpuPercent;
        public double MemUsedGb, MemTotalGb;
        public double DiskFreeGb, DiskTotalGb;
        public string DiskName = "C:";
    }

    public sealed class ServiceManager : IDisposable
    {
        public AppConfig Config { get; private set; }
        public List<ServiceRunner> Runners { get; } = new List<ServiceRunner>();
        public SystemStats Sys { get; } = new SystemStats();
        public event Action<PanelEvent> EventAdded;

        readonly object eventGate = new object();
        readonly List<PanelEvent> events = new List<PanelEvent>();
        readonly Dictionary<int, TimeSpan> lastCpu = new Dictionary<int, TimeSpan>();
        readonly HashSet<string> scheduleDone = new HashSet<string>();
        readonly Dictionary<string, DateTime> intervalLast = new Dictionary<string, DateTime>();
        DateTime lastSample = DateTime.Now;
        DateTime lastExternalScan = DateTime.MinValue;
        Timer monitor;
        Timer scheduler;
        PerformanceCounter cpuCounter;
        bool startingAll;

        public ServiceManager(AppConfig config)
        {
            Config = config;
            foreach (var s in config.Services) AddRunner(s);
            try { cpuCounter = new PerformanceCounter("Processor", "% Processor Time", "_Total"); cpuCounter.NextValue(); } catch { }
            monitor = new Timer(_ => SafeRun(Monitor), null, 1000, 2000);
            scheduler = new Timer(_ => SafeRun(ScheduleTick), null, 5000, 5000);
        }

        static void SafeRun(Action a) { try { a(); } catch (Exception ex) { Trace.WriteLine(ex); } }

        ServiceRunner AddRunner(ServiceConfig s)
        {
            var r = new ServiceRunner(s, Config.ConsoleMaxLines);
            r.Event += (runner, msg) => AddEvent(runner.Config.Name, msg,
                msg.StartsWith("ÇÖKTÜ") || msg.StartsWith("çok sık"));
            Runners.Add(r);
            return r;
        }

        public ServiceRunner Find(string id) => Runners.FirstOrDefault(r => r.Config.Id == id);
        public string NameOf(string id) => string.IsNullOrEmpty(id) ? "Tüm Minecraft sunucuları" : Find(id)?.Config.Name ?? "(silinmiş servis)";

        /// <summary>Applies an edited config; keeps running processes of services that still exist.</summary>
        public void ApplyConfig(AppConfig cfg)
        {
            Config = cfg;
            var keep = new List<ServiceRunner>();
            foreach (var s in cfg.Services)
            {
                var existing = Runners.FirstOrDefault(r => r.Config.Id == s.Id);
                if (existing != null) { existing.UpdateConfig(s, cfg.ConsoleMaxLines); keep.Add(existing); }
                else keep.Add(AddRunner(s));
            }
            foreach (var gone in Runners.Except(keep).ToList())
                if (gone.IsOwned) gone.Stop();
            Runners.Clear();
            Runners.AddRange(keep);
            Runners.Sort((a, b) => cfg.Services.IndexOf(a.Config).CompareTo(cfg.Services.IndexOf(b.Config)));
            cfg.Save();
            AddEvent("Panel", "Ayarlar kaydedildi.");
        }

        // ------------------------------------------------------------------ events
        public void AddEvent(string service, string message, bool important = false)
        {
            var e = new PanelEvent { Time = DateTime.Now, Service = service, Message = message, Important = important };
            lock (eventGate)
            {
                events.Add(e);
                if (events.Count > 3000) events.RemoveRange(0, events.Count - 3000);
            }
            try
            {
                File.AppendAllText(Path.Combine(AppConfig.Folder, "lumpanel-olaylar.log"),
                    $"{e.Time:yyyy-MM-dd HH:mm:ss}\t{service}\t{message}\r\n", Encoding.UTF8);
            }
            catch { }
            EventAdded?.Invoke(e);
            if (important) NotifyDiscord($"**{service}**: {message}");
        }

        public List<PanelEvent> Events { get { lock (eventGate) return events.ToList(); } }
        public void ClearEvents() { lock (eventGate) events.Clear(); }

        public void NotifyDiscord(string text)
        {
            string url = Config.DiscordWebhook?.Trim();
            if (string.IsNullOrEmpty(url) || !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) return;
            ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
                    using var wc = new WebClient();
                    wc.Headers[HttpRequestHeader.ContentType] = "application/json";
                    wc.Encoding = Encoding.UTF8;
                    string body = new JavaScriptSerializer().Serialize(new { username = "LumPanel", content = "🖥️ " + text });
                    wc.UploadString(url, body);
                }
                catch (Exception ex) { Trace.WriteLine("Discord webhook: " + ex.Message); }
            });
        }

        // ------------------------------------------------------------------ bulk actions
        public void StartAll(bool onlyAutoStart)
        {
            if (startingAll) return;
            startingAll = true;
            var list = Runners.Where(r => !onlyAutoStart || r.Config.AutoStart).ToList();
            AddEvent("Panel", onlyAutoStart ? "Otomatik başlatılan servisler sırayla açılıyor." : "Bütün servisler sırayla açılıyor.");
            ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    foreach (var r in list)
                    {
                        if (r.IsAlive || r.State == ServiceState.External) continue;
                        string err = r.Start();
                        if (err != null) AddEvent(r.Config.Name, "başlatılamadı: " + err, true);
                        Thread.Sleep(Math.Max(0, r.Config.StartDelaySec) * 1000);
                    }
                }
                finally { startingAll = false; }
            });
        }

        /// <summary>Stops in reverse order (proxy and bot before game servers) and waits until all exit.</summary>
        public void StopAll(bool wait = false)
        {
            AddEvent("Panel", "Bütün servisler kapatılıyor.");
            var list = Runners.AsEnumerable().Reverse().Where(r => r.IsOwned).ToList();
            foreach (var r in list) r.Stop();
            if (!wait) return;
            var until = DateTime.Now.AddSeconds(Math.Max(15, list.Select(r => r.Config.StopTimeoutSec).DefaultIfEmpty(0).Max() + 10));
            while (DateTime.Now < until && list.Any(r => r.IsOwned)) Thread.Sleep(300);
            foreach (var r in list.Where(r => r.IsOwned)) r.Kill();
        }

        // ------------------------------------------------------------------ monitor
        void Monitor()
        {
            var now = DateTime.Now;
            double seconds = Math.Max(0.5, (now - lastSample).TotalSeconds);
            lastSample = now;
            var map = ProcessTree.Children();
            var seen = new HashSet<int>();
            foreach (var r in Runners)
            {
                int root = r.RootPid;
                if (root <= 0) { r.CpuPercent = 0; r.MemoryBytes = 0; r.ProcessCount = 0; r.PushCpu(0); continue; }
                double cpu = 0; long mem = 0; int count = 0;
                foreach (int pid in ProcessTree.Tree(root, map))
                {
                    try
                    {
                        using var p = Process.GetProcessById(pid);
                        var total = p.TotalProcessorTime;
                        if (lastCpu.TryGetValue(pid, out var before)) cpu += (total - before).TotalSeconds;
                        lastCpu[pid] = total;
                        mem += p.WorkingSet64;
                        count++;
                        seen.Add(pid);
                        if (r.IsOwned && r.Config.Priority != "Normal" &&
                            Enum.TryParse(r.Config.Priority, out ProcessPriorityClass prio) && p.PriorityClass != prio &&
                            !string.Equals(p.ProcessName, "conhost", StringComparison.OrdinalIgnoreCase))
                            p.PriorityClass = prio;
                    }
                    catch { }
                }
                r.CpuPercent = cpu / seconds / Environment.ProcessorCount * 100;
                r.PushCpu(r.CpuPercent);
                r.MemoryBytes = mem;
                r.ProcessCount = count;
            }
            foreach (var stale in lastCpu.Keys.Where(k => !seen.Contains(k)).ToList()) lastCpu.Remove(stale);

            if (now - lastExternalScan > TimeSpan.FromSeconds(8)) { lastExternalScan = now; ScanExternal(); }
            SampleSystem();
        }

        void ScanExternal()
        {
            var watch = Runners.Where(r => !r.IsOwned && !string.IsNullOrWhiteSpace(r.Config.MatchText)).ToList();
            if (watch.Count == 0) return;
            int self = Process.GetCurrentProcess().Id;
            var found = new Dictionary<ServiceRunner, int>();
            using (var searcher = new ManagementObjectSearcher("SELECT ProcessId, CommandLine, Name FROM Win32_Process"))
            using (var results = searcher.Get())
            {
                foreach (ManagementObject mo in results)
                {
                    using (mo)
                    {
                        string cmd = mo["CommandLine"] as string;
                        string name = (mo["Name"] as string ?? "").ToLowerInvariant();
                        int pid = Convert.ToInt32(mo["ProcessId"]);
                        if (cmd == null || pid == self || name == "cmd.exe" || name == "conhost.exe" || name == "lumpanel.exe") continue;
                        foreach (var r in watch)
                            if (!found.ContainsKey(r) && cmd.IndexOf(r.Config.MatchText, StringComparison.OrdinalIgnoreCase) >= 0)
                                found[r] = pid;
                    }
                }
            }
            foreach (var r in watch) r.SetExternal(found.TryGetValue(r, out int pid) ? pid : 0);
        }

        [StructLayout(LayoutKind.Sequential)]
        struct MEMORYSTATUSEX
        {
            public uint dwLength, dwMemoryLoad;
            public ulong ullTotalPhys, ullAvailPhys, ullTotalPageFile, ullAvailPageFile, ullTotalVirtual, ullAvailVirtual, ullAvailExtendedVirtual;
        }
        [DllImport("kernel32.dll")] static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX m);

        void SampleSystem()
        {
            try { if (cpuCounter != null) Sys.CpuPercent = cpuCounter.NextValue(); } catch { }
            var m = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf(typeof(MEMORYSTATUSEX)) };
            if (GlobalMemoryStatusEx(ref m))
            {
                Sys.MemTotalGb = m.ullTotalPhys / 1073741824.0;
                Sys.MemUsedGb = (m.ullTotalPhys - m.ullAvailPhys) / 1073741824.0;
            }
            try
            {
                var d = new DriveInfo(Path.GetPathRoot(AppConfig.Folder) ?? "C:\\");
                Sys.DiskName = d.Name.TrimEnd('\\');
                Sys.DiskFreeGb = d.AvailableFreeSpace / 1073741824.0;
                Sys.DiskTotalGb = d.TotalSize / 1073741824.0;
            }
            catch { }
        }

        // ------------------------------------------------------------------ scheduler
        IEnumerable<ServiceRunner> Targets(ScheduleConfig s) =>
            string.IsNullOrEmpty(s.ServiceId)
                ? Runners.Where(r => r.Config.Kind == ServiceKinds.Minecraft)
                : Runners.Where(r => r.Config.Id == s.ServiceId);

        void ScheduleTick()
        {
            var now = DateTime.Now;
            string today = now.ToString("yyyyMMdd");
            scheduleDone.RemoveWhere(k => !k.Contains(today));
            foreach (var s in Config.Schedules.Where(x => x.Enabled).ToList())
            {
                if (s.Type == "interval")
                {
                    if (!intervalLast.TryGetValue(s.Id, out var last)) { intervalLast[s.Id] = now; continue; }
                    if (now - last < TimeSpan.FromMinutes(Math.Max(1, s.IntervalMin))) continue;
                    intervalLast[s.Id] = now;
                    foreach (var r in Targets(s).Where(r => r.State == ServiceState.Running)) r.Send(s.Command);
                    continue;
                }
                if (!TimeSpan.TryParse(s.Time, out var at)) continue;
                var due = now.Date + at;
                if (s.Type == "backup")
                {
                    if (now >= due && now - due < TimeSpan.FromMinutes(2) && scheduleDone.Add($"{s.Id}:{today}:backup"))
                        foreach (var r in Targets(s).Where(r => r.Config.Kind == ServiceKinds.Minecraft).ToList())
                            ThreadPool.QueueUserWorkItem(_ => { try { BackupService.Run(this, r); } catch { } });
                    continue;
                }
                if (s.Type == "command")
                {
                    string key = $"{s.Id}:{today}:cmd";
                    if (now >= due && now - due < TimeSpan.FromMinutes(2) && scheduleDone.Add(key))
                    {
                        foreach (var r in Targets(s).Where(r => r.State == ServiceState.Running)) r.Send(s.Command);
                        AddEvent(NameOf(s.ServiceId), "zamanlanmış komut: " + s.Command);
                    }
                    continue;
                }
                // daily restart with in-game warnings
                var warnings = new[] { (600, "10 dakika"), (300, "5 dakika"), (60, "1 dakika"), (10, "10 saniye") };
                foreach (var (sec, label) in warnings)
                {
                    var t = due.AddSeconds(-sec);
                    if (!s.Warn || now < t || now - t > TimeSpan.FromSeconds(30) || !scheduleDone.Add($"{s.Id}:{today}:w{sec}")) continue;
                    foreach (var r in Targets(s).Where(r => r.State == ServiceState.Running))
                    {
                        if (r.Config.Kind == ServiceKinds.Minecraft) r.Send($"say Sunucu {label} sonra yeniden başlatılacak!");
                        else if (r.Config.Kind == ServiceKinds.Proxy) r.Send($"alert Sunucu {label} sonra yeniden başlatılacak!");
                    }
                }
                if (now >= due && now - due < TimeSpan.FromMinutes(2) && scheduleDone.Add($"{s.Id}:{today}:restart"))
                {
                    foreach (var r in Targets(s).Where(r => r.IsOwned))
                    {
                        if (r.Config.Kind == ServiceKinds.Minecraft) r.Send("save-all");
                        r.Stop(restart: true);
                    }
                    AddEvent(NameOf(s.ServiceId), "zamanlanmış yeniden başlatma", true);
                }
            }
        }

        public void Dispose()
        {
            monitor?.Dispose();
            scheduler?.Dispose();
            cpuCounter?.Dispose();
        }
    }
}
