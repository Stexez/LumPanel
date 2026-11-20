using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace LumPanel
{
    /// <summary>Finds Minecraft servers, Velocity, the Discord bot, the agent and the website on local disks.</summary>
    public static class Detector
    {
        static readonly HashSet<string> Skip = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Windows", "Program Files", "Program Files (x86)", "ProgramData", "$Recycle.Bin", "System Volume Information",
            "AppData", "node_modules", ".git", "plugins", "logs", "cache", "libraries", "versions", "world", "world_nether",
            "world_the_end", "crash-reports", "backups", "Recovery", "PerfLogs", "__pycache__", ".test-deps", "Microsoft",
            "Windows Kits", "dotnet", "bundler",
        };

        static readonly Regex ServerJar = new Regex(@"^(paper|purpur|pufferfish|folia|spigot|leaf|server)[^\\/]*\.jar$", RegexOptions.IgnoreCase);
        static readonly Regex ProxyJar = new Regex(@"^(velocity|waterfall|bungeecord)[^\\/]*\.jar$", RegexOptions.IgnoreCase);

        public static List<ServiceConfig> Scan(Action<string> progress = null)
        {
            var found = new List<ServiceConfig>();
            foreach (var drive in DriveInfo.GetDrives().Where(d => d.DriveType == DriveType.Fixed && d.IsReady))
            {
                progress?.Invoke("Taranıyor: " + drive.Name);
                Walk(drive.RootDirectory.FullName, 0, found, progress);
            }
            // a jar name used by several servers cannot identify an outside process
            foreach (var group in found.Where(s => s.MatchText.EndsWith(".jar")).GroupBy(s => s.MatchText.ToLowerInvariant()).Where(g => g.Count() > 1))
                foreach (var s in group) s.MatchText = "";
            int featured = found.FindIndex(s => s.Kind == ServiceKinds.Minecraft && s.Name.IndexOf("smp", StringComparison.OrdinalIgnoreCase) >= 0);
            if (featured < 0) featured = found.FindIndex(s => s.Kind == ServiceKinds.Minecraft);
            if (featured >= 0) found[featured].Featured = true;
            // start order: game servers, proxy, agent, bot
            int Rank(ServiceConfig s) => s.Kind == ServiceKinds.Minecraft ? 0 : s.Kind == ServiceKinds.Proxy ? 1 : s.Id == "agent" ? 2 : 3;
            return found.OrderBy(Rank).ToList();
        }

        static void Walk(string dir, int depth, List<ServiceConfig> found, Action<string> progress)
        {
            if (depth > 6) return;
            string[] files, dirs;
            try { files = Directory.GetFiles(dir); dirs = Directory.GetDirectories(dir); }
            catch { return; }
            var names = files.Select(Path.GetFileName).ToList();
            if (Inspect(dir, names, found)) return;  // a server folder: do not descend into it
            if (depth <= 1) progress?.Invoke("Taranıyor: " + dir);
            foreach (var d in dirs)
            {
                string n = Path.GetFileName(d);
                if (Skip.Contains(n) || n.StartsWith(".") || n.StartsWith("$")) continue;
                try { if ((new DirectoryInfo(d).Attributes & FileAttributes.ReparsePoint) != 0) continue; } catch { continue; }
                Walk(d, depth + 1, found, progress);
            }
        }

        static bool Inspect(string dir, List<string> names, List<ServiceConfig> found)
        {
            string folder = Path.GetFileName(dir.TrimEnd('\\')) ?? dir;
            bool has(string f) => names.Any(n => n.Equals(f, StringComparison.OrdinalIgnoreCase));

            string proxyJar = names.FirstOrDefault(n => ProxyJar.IsMatch(n));
            string serverJar = names.FirstOrDefault(n => ServerJar.IsMatch(n));
            if (proxyJar != null && (has("velocity.toml") || has("config.yml")))
            {
                found.Add(JavaService(dir, folder, proxyJar, names, ServiceKinds.Proxy, "Velocity (proxy)", "512M", "1G"));
                return true;
            }
            if (serverJar != null && (has("server.properties") || has("eula.txt")))
            {
                string name = folder.IndexOf("smp", StringComparison.OrdinalIgnoreCase) >= 0 ? "LumSMP"
                    : folder.IndexOf("lobby", StringComparison.OrdinalIgnoreCase) >= 0 || folder.IndexOf("lumcraft", StringComparison.OrdinalIgnoreCase) >= 0 ? "LumCraft"
                    : folder;
                found.Add(JavaService(dir, folder, serverJar, names, ServiceKinds.Minecraft, name, "2G", "4G"));
                return true;
            }
            if (has("mc_agent.py") && found.All(s => s.Id != "agent"))
            {
                found.Add(new ServiceConfig
                {
                    Id = "agent", Name = "Minecraft agent", Kind = ServiceKinds.Python, WorkingDir = dir, Program = Python(),
                    Arguments = "mc_agent.py", MatchText = "mc_agent.py", StartDelaySec = 2,
                });
                return true;
            }
            if (has("main.py") && Directory.Exists(Path.Combine(dir, "lumbot")) && has("config.yaml") && found.All(s => s.Id != "bot"))
            {
                found.Add(new ServiceConfig
                {
                    Id = "bot", Name = "Discord botu", Kind = ServiceKinds.Python, WorkingDir = dir, Program = Python(),
                    Arguments = "main.py", MatchText = "main.py", StartDelaySec = 3,
                });
                return true;
            }
            return false;
        }

        static ServiceConfig JavaService(string dir, string folder, string jar, List<string> names, string kind, string name, string xms, string xmx)
        {
            var svc = new ServiceConfig
            {
                Id = Regex.Replace(name.ToLowerInvariant(), "[^a-z0-9]+", "-").Trim('-'),
                Name = name, Kind = kind, WorkingDir = dir, MatchText = jar, StopTimeoutSec = kind == ServiceKinds.Minecraft ? 90 : 30,
                StartDelaySec = kind == ServiceKinds.Minecraft ? 20 : 5,
            };
            // Prefer the server's own start script, unless it pauses or opens new windows.
            string script = names.FirstOrDefault(n => n.EndsWith(".bat", StringComparison.OrdinalIgnoreCase) &&
                Regex.IsMatch(n, "start|run|baslat|başlat|launch", RegexOptions.IgnoreCase));
            string javaLine = null;
            if (script != null)
            {
                try
                {
                    javaLine = File.ReadAllLines(Path.Combine(dir, script))
                        .Select(l => l.Trim())
                        .FirstOrDefault(l => Regex.IsMatch(l, @"(^|[\\""/\s])java(w)?(\.exe)?""?\s", RegexOptions.IgnoreCase) && l.IndexOf(".jar", StringComparison.OrdinalIgnoreCase) > 0);
                }
                catch { }
            }
            if (javaLine != null)
            {
                // run the java line directly: no "pause", no extra cmd window
                var m = Regex.Match(javaLine, @"^(?:start\s+(?:""[^""]*""\s+)?(?:/\w+\s+)*)?(""[^""]+""|\S+)\s+(.*)$", RegexOptions.IgnoreCase);
                svc.Program = m.Success ? m.Groups[1].Value.Trim('"') : "java";
                svc.Arguments = m.Success ? m.Groups[2].Value.Replace("%*", "").Trim() : $"-jar {jar} nogui";
            }
            else
            {
                svc.Program = "java";
                svc.Arguments = $"-Xms{xms} -Xmx{xmx} -jar {jar}" + (kind == ServiceKinds.Minecraft ? " nogui" : "");
            }
            if (kind == ServiceKinds.Minecraft && svc.Arguments.IndexOf("nogui", StringComparison.OrdinalIgnoreCase) < 0) svc.Arguments += " nogui";
            return svc;
        }

        static string pythonCache;
        public static string Python()
        {
            if (pythonCache != null) return pythonCache;
            try
            {
                string pp = @"C:\LumCraft\Website\.python-path";
                if (File.Exists(pp))
                {
                    string p = File.ReadAllText(pp).Trim();
                    if (File.Exists(p)) return pythonCache = p;
                }
                var psi = new ProcessStartInfo("py", "-3 -c \"import sys;print(sys.executable)\"")
                { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true };
                using var proc = Process.Start(psi);
                string exe = proc.StandardOutput.ReadToEnd().Trim();
                proc.WaitForExit(5000);
                if (File.Exists(exe)) return pythonCache = exe;
            }
            catch { }
            return pythonCache = "python";
        }
    }
}
