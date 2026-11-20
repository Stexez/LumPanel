using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Web.Script.Serialization;

namespace LumPanel
{
    /// <summary>Kinds decide default stop command, ready detection and quick commands.</summary>
    public static class ServiceKinds
    {
        public const string Minecraft = "minecraft";
        public const string Proxy = "proxy";
        public const string Python = "python";
        public const string Other = "other";
        public static readonly string[] All = { Minecraft, Proxy, Python, Other };

        public static string Label(string kind) => kind switch
        {
            Minecraft => "Minecraft sunucusu",
            Proxy => "Velocity / proxy",
            Python => "Python (site, bot, agent)",
            _ => "Diğer program",
        };
    }

    public class ServiceConfig
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N").Substring(0, 8);
        public string Name { get; set; } = "Yeni servis";
        public string Kind { get; set; } = ServiceKinds.Other;
        public string WorkingDir { get; set; } = "";
        /// <summary>Program to run: java, python.exe, caddy.exe or a .bat/.cmd file.</summary>
        public string Program { get; set; } = "";
        public string Arguments { get; set; } = "";
        /// <summary>Console command for a clean shutdown ("stop" for Paper, "end" for Velocity). Empty = kill.</summary>
        public string StopCommand { get; set; } = "";
        public int StopTimeoutSec { get; set; } = 60;
        public bool AutoStart { get; set; } = true;
        public bool AutoRestart { get; set; } = true;
        /// <summary>Seconds to wait after this one when starting everything in order.</summary>
        public int StartDelaySec { get; set; } = 5;
        public string Encoding { get; set; } = "utf-8";
        public string Priority { get; set; } = "Normal";
        /// <summary>Text in a process command line that identifies this service when it runs outside the panel.</summary>
        public string MatchText { get; set; } = "";
        /// <summary>Shown big on the first page (LumSMP).</summary>
        public bool Featured { get; set; }
        public List<string> QuickCommands { get; set; } = new List<string>();

        [ScriptIgnore]
        public string EffectiveStopCommand =>
            !string.IsNullOrWhiteSpace(StopCommand) ? StopCommand.Trim() :
            Kind == ServiceKinds.Minecraft ? "stop" : Kind == ServiceKinds.Proxy ? "end" : "";

        [ScriptIgnore]
        public IEnumerable<string> EffectiveQuickCommands
        {
            get
            {
                if (QuickCommands != null && QuickCommands.Count > 0) return QuickCommands;
                return Kind switch
                {
                    ServiceKinds.Minecraft => new[] { "list", "save-all", "tps", "mspt", "whitelist list", "whitelist reload", "lp sync", "plugins" },
                    ServiceKinds.Proxy => new[] { "glist", "velocity info", "velocity plugins", "velocity reload" },
                    _ => new string[0],
                };
            }
        }

        public ServiceConfig Clone() => (ServiceConfig)MemberwiseClone();
    }

    public class ScheduleConfig
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N").Substring(0, 8);
        public string ServiceId { get; set; } = "";
        /// <summary>restart = daily restart at Time, command = daily command at Time, interval = command every IntervalMin.</summary>
        public string Type { get; set; } = "restart";
        public string Time { get; set; } = "05:00";
        public int IntervalMin { get; set; } = 30;
        public string Command { get; set; } = "save-all";
        /// <summary>For restarts of Minecraft servers: announce 10m/5m/1m/10s before.</summary>
        public bool Warn { get; set; } = true;
        public bool Enabled { get; set; } = true;

        public string Describe(Func<string, string> serviceName)
        {
            string target = serviceName(ServiceId);
            return Type switch
            {
                "restart" => $"{target}: her gün {Time} yeniden başlat{(Warn ? " (oyunculara uyarı)" : "")}",
                "command" => $"{target}: her gün {Time} → {Command}",
                "backup" => $"{target}: her gün {Time} dünya yedeği",
                _ => $"{target}: her {IntervalMin} dk → {Command}",
            };
        }
    }

    public class AppConfig
    {
        public List<ServiceConfig> Services { get; set; } = new List<ServiceConfig>();
        public List<ScheduleConfig> Schedules { get; set; } = new List<ScheduleConfig>();
        public string DiscordWebhook { get; set; } = "";
        public bool StartWithWindows { get; set; }
        public bool AutoStartServicesOnLaunch { get; set; } = true;
        public bool MinimizeToTray { get; set; } = true;
        public int ConsoleMaxLines { get; set; } = 5000;
        public string BackupDir { get; set; } = "";
        public int BackupKeep { get; set; } = 7;
        /// <summary>Anthropic API key, encrypted for this Windows user (DPAPI). Never stored in plain text.</summary>
        public string AiKeyProtected { get; set; } = "";
        public string AiModel { get; set; } = "claude-haiku-5-5";
        /// <summary>"claude-code" = the user's own Claude plan through the Claude Code CLI (no extra cost); "api" = paid API key.</summary>
        public string AiBackend { get; set; } = "claude-code";
        public string AiClaudePath { get; set; } = "";
        public bool AiAutoExplain { get; set; } = true;
        public int AiDailyLimit { get; set; } = 40;

        public static string Folder => AppDomain.CurrentDomain.BaseDirectory;
        public static string PathOf => System.IO.Path.Combine(Folder, "lumpanel.json");
        /// <summary>File this config was loaded from (--config or lumpanel.json next to the exe).</summary>
        public static string ActivePath = PathOf;

        public static AppConfig Load(string path = null)
        {
            path ??= PathOf;
            ActivePath = path;
            if (!File.Exists(path)) return Defaults();
            try
            {
                var cfg = new JavaScriptSerializer().Deserialize<AppConfig>(File.ReadAllText(path, System.Text.Encoding.UTF8));
                cfg.Services ??= new List<ServiceConfig>();
                cfg.Schedules ??= new List<ScheduleConfig>();
                foreach (var s in cfg.Services) s.QuickCommands ??= new List<string>();
                return cfg;
            }
            catch (Exception ex)
            {
                File.Copy(path, path + ".bozuk-" + DateTime.Now.ToString("yyyyMMdd-HHmmss"), true);
                System.Windows.Forms.MessageBox.Show("lumpanel.json okunamadı, varsayılan ayarlar yüklendi.\n" + ex.Message,
                    "LumPanel", System.Windows.Forms.MessageBoxButtons.OK, System.Windows.Forms.MessageBoxIcon.Warning);
                return Defaults();
            }
        }

        public void Save(string path = null)
        {
            path ??= ActivePath;
            string json = JsonPretty.Format(new JavaScriptSerializer().Serialize(this));
            string tmp = path + ".tmp";
            File.WriteAllText(tmp, json, new UTF8Encoding(false));
            if (File.Exists(path)) File.Replace(tmp, path, null); else File.Move(tmp, path);
        }

        /// <summary>Known LUM Craft layout on the VDS; Minecraft servers, bot and agent come from "Otomatik bul".</summary>
        public static AppConfig Defaults()
        {
            var cfg = new AppConfig();
            string web = @"C:\LumCraft\Website";
            string python = "python";
            try
            {
                string pp = System.IO.Path.Combine(web, ".python-path");
                if (File.Exists(pp)) python = File.ReadAllText(pp).Trim();
            }
            catch { }
            cfg.Services.Add(new ServiceConfig
            {
                Id = "site", Name = "Web sitesi", Kind = ServiceKinds.Python, WorkingDir = web, Program = python,
                Arguments = "server.py", MatchText = @"Website\server.py", StartDelaySec = 3,
            });
            cfg.Services.Add(new ServiceConfig
            {
                Id = "caddy", Name = "Caddy (HTTPS)", Kind = ServiceKinds.Other, WorkingDir = @"C:\LumCraft\Caddy",
                Program = @"C:\LumCraft\Caddy\caddy.exe", Arguments = "run --config Caddyfile --adapter caddyfile",
                MatchText = "caddy.exe", StartDelaySec = 2,
            });
            cfg.Schedules.Add(new ScheduleConfig { ServiceId = "", Type = "interval", IntervalMin = 15, Command = "save-all", Enabled = false });
            return cfg;
        }
    }

    /// <summary>Indents compact JSON so lumpanel.json stays hand-editable.</summary>
    public static class JsonPretty
    {
        public static string Format(string json)
        {
            var sb = new StringBuilder();
            int indent = 0;
            bool quoted = false;
            for (int i = 0; i < json.Length; i++)
            {
                char c = json[i];
                if (c == '"' && (i == 0 || json[i - 1] != '\\' || (i > 1 && json[i - 2] == '\\'))) quoted = !quoted;
                if (quoted) { sb.Append(c); continue; }
                switch (c)
                {
                    case '{':
                    case '[':
                        sb.Append(c);
                        if (i + 1 < json.Length && (json[i + 1] == '}' || json[i + 1] == ']')) { sb.Append(json[++i]); break; }
                        sb.Append('\n').Append(' ', ++indent * 2);
                        break;
                    case '}':
                    case ']':
                        sb.Append('\n').Append(' ', --indent * 2).Append(c);
                        break;
                    case ',':
                        sb.Append(c).Append('\n').Append(' ', indent * 2);
                        break;
                    case ':':
                        sb.Append(": ");
                        break;
                    default:
                        sb.Append(c);
                        break;
                }
            }
            return sb.ToString();
        }
    }
}
