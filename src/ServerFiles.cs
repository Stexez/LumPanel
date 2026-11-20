using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace LumPanel
{
    /// <summary>Safe file access inside one server folder: path checks, backups before every write, atomic replace.</summary>
    public sealed class ServerFiles
    {
        public string Root { get; }
        public ServerFiles(string root) { Root = Path.GetFullPath(root).TrimEnd('\\') + "\\"; }

        /// <summary>Resolves a relative path and refuses anything outside the server folder.</summary>
        public string Resolve(string relative)
        {
            string full = Path.GetFullPath(Path.Combine(Root, relative ?? ""));
            if (!full.StartsWith(Root, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Sunucu klasörünün dışına erişilemez.");
            return full;
        }

        public bool Exists(string rel) => File.Exists(Resolve(rel));

        public string Read(string rel)
        {
            using var fs = new FileStream(Resolve(rel), FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var sr = new StreamReader(fs, new UTF8Encoding(false), true);
            return sr.ReadToEnd();
        }

        /// <summary>Copies the current file to .lumpanel-yedek\ (keeps 20 per file) then writes atomically.</summary>
        public void Write(string rel, string content)
        {
            string path = Resolve(rel);
            if (File.Exists(path))
            {
                string dir = Path.Combine(Root, ".lumpanel-yedek");
                Directory.CreateDirectory(dir);
                string name = rel.Replace('\\', '_').Replace('/', '_');
                File.Copy(path, Path.Combine(dir, $"{name}.{DateTime.Now:yyyyMMdd-HHmmss}.bak"), true);
                foreach (var old in new DirectoryInfo(dir).GetFiles(name + ".*.bak").OrderByDescending(f => f.Name).Skip(20)) { try { old.Delete(); } catch { } }
            }
            string tmp = path + ".lumpanel-tmp";
            File.WriteAllText(tmp, content, new UTF8Encoding(false));
            if (File.Exists(path)) File.Replace(tmp, path, null); else File.Move(tmp, path);
        }

        // ------------------------------------------------------------------ server.properties
        public static List<KeyValuePair<string, string>> ParseProperties(string text) =>
            text.Split('\n').Select(l => l.TrimEnd('\r')).Where(l => l.Length > 0 && !l.StartsWith("#") && !l.StartsWith("!") && l.Contains("="))
                .Select(l => new KeyValuePair<string, string>(l.Substring(0, l.IndexOf('=')).Trim(), l.Substring(l.IndexOf('=') + 1)))
                .ToList();

        /// <summary>Replaces values in place; keeps comments, order and unknown keys; appends new keys.</summary>
        public static string SetProperties(string text, IDictionary<string, string> values)
        {
            var lines = text.Replace("\r\n", "\n").Split('\n').ToList();
            var done = new HashSet<string>();
            for (int i = 0; i < lines.Count; i++)
            {
                string l = lines[i];
                if (l.StartsWith("#") || !l.Contains("=")) continue;
                string key = l.Substring(0, l.IndexOf('=')).Trim();
                if (values.TryGetValue(key, out var v)) { lines[i] = key + "=" + Clean(v); done.Add(key); }
            }
            while (lines.Count > 0 && lines[lines.Count - 1].Length == 0) lines.RemoveAt(lines.Count - 1);
            foreach (var kv in values.Where(kv => !done.Contains(kv.Key))) lines.Add(kv.Key + "=" + Clean(kv.Value));
            return string.Join("\r\n", lines) + "\r\n";
        }

        static string Clean(string v) => (v ?? "").Replace("\r", "").Replace("\n", "");

        // ------------------------------------------------------------------ minimal YAML path setter
        /// <summary>
        /// Sets an existing scalar at a dotted path (e.g. "entity-activation-range.monsters") without a YAML library:
        /// walks indentation, replaces only the value, keeps comments. Returns false when the path is not found.
        /// Keys containing dots are written with '|' instead (e.g. "spawn-limits|monsters" style is not needed here).
        /// </summary>
        public static bool SetYaml(ref string text, string path, string value)
        {
            var parts = path.Split('.');
            var lines = text.Replace("\r\n", "\n").Split('\n');
            int depth = 0, parentIndent = -1;
            for (int i = 0; i < lines.Length && depth < parts.Length; i++)
            {
                string l = lines[i];
                string trimmed = l.TrimStart();
                if (trimmed.Length == 0 || trimmed.StartsWith("#")) continue;
                int indent = l.Length - trimmed.Length;
                if (indent <= parentIndent) return false;             // left the parent block: not found
                var m = Regex.Match(trimmed, @"^(['""]?)([^'"":#]+)\1:(\s*)(.*)$");
                if (!m.Success || m.Groups[2].Value.Trim() != parts[depth]) continue;
                if (depth == parts.Length - 1)
                {
                    string rest = m.Groups[4].Value;
                    if (rest.Length == 0) return false;               // a block, not a scalar
                    int hash = rest.IndexOf(" #", StringComparison.Ordinal);
                    string comment = hash >= 0 ? rest.Substring(hash) : "";
                    lines[i] = l.Substring(0, indent) + m.Groups[1].Value + m.Groups[2].Value + m.Groups[1].Value + ": " + value + comment;
                    text = string.Join("\n", lines);
                    return true;
                }
                depth++;
                parentIndent = indent;
            }
            return false;
        }

        public static string GetYaml(string text, string path)
        {

            var parts = path.Split('.');
            var lines = text.Replace("\r\n", "\n").Split('\n');
            int depth = 0, parentIndent = -1;
            foreach (var l in lines)
            {
                string trimmed = l.TrimStart();
                if (trimmed.Length == 0 || trimmed.StartsWith("#")) continue;
                int indent = l.Length - trimmed.Length;
                if (indent <= parentIndent) return null;
                var m = Regex.Match(trimmed, @"^(['""]?)([^'"":#]+)\1:(\s*)(.*)$");
                if (!m.Success || m.Groups[2].Value.Trim() != parts[depth]) continue;
                if (depth == parts.Length - 1)
                {
                    string rest = m.Groups[4].Value;
                    int hash = rest.IndexOf(" #", StringComparison.Ordinal);
                    return (hash >= 0 ? rest.Substring(0, hash) : rest).Trim();
                }
                depth++;
                parentIndent = indent;
            }
            return null;
        }
    }

    /// <summary>One change an optimization preset wants to make.</summary>
    public sealed class Tweak
    {
        public string File, Key, Value, Why;
        public bool IsProperties => File == "server.properties";
    }

    public static class Presets
    {
        /// <summary>Well-known, conservative performance settings for Paper/Purpur survival servers.</summary>
        public static readonly Dictionary<string, List<Tweak>> All = new Dictionary<string, List<Tweak>>
        {
            ["Dengeli (önerilen, survival SMP)"] = new List<Tweak>
            {
                T("server.properties", "view-distance", "8", "Görüş mesafesi: en büyük performans kazancı"),
                T("server.properties", "simulation-distance", "6", "Mobların/redstone'un işlendiği mesafe"),
                T("server.properties", "network-compression-threshold", "256", "Ağ paket sıkıştırma eşiği"),
                T("server.properties", "sync-chunk-writes", "false", "Chunk kaydı ana iş parçacığını bekletmez"),
                T("spigot.yml", "world-settings.default.entity-activation-range.animals", "16", "Uzaktaki hayvanlar daha seyrek işlenir"),
                T("spigot.yml", "world-settings.default.entity-activation-range.monsters", "24", "Uzaktaki canavarlar daha seyrek işlenir"),
                T("spigot.yml", "world-settings.default.entity-activation-range.misc", "8", "Diğer varlıklar"),
                T("spigot.yml", "world-settings.default.merge-radius.item", "3.5", "Yerdeki eşyalar birleşir"),
                T("spigot.yml", "world-settings.default.merge-radius.exp", "4.0", "XP küreleri birleşir"),
                T("spigot.yml", "world-settings.default.mob-spawn-range", "6", "Mob doğma mesafesi (simulation-distance ile uyumlu)"),
                T("bukkit.yml", "spawn-limits.monsters", "50", "Oyuncu başına canavar sınırı"),
                T("bukkit.yml", "spawn-limits.animals", "8", "Hayvan sınırı"),
                T("bukkit.yml", "ticks-per.monster-spawns", "2", "Canavar doğurma sıklığı"),
                T("config/paper-world-defaults.yml", "environment.optimize-explosions", "true", "Patlama hesabı hızlanır"),
                T("config/paper-world-defaults.yml", "misc.redstone-implementation", "ALTERNATE_CURRENT", "Çok daha hızlı redstone (davranış aynı kalır)"),
                T("config/paper-world-defaults.yml", "chunks.prevent-moving-into-unloaded-chunks", "true", "Yüklenmemiş chunk'a girme takılmaları"),
                T("config/paper-world-defaults.yml", "chunks.max-auto-save-chunks-per-tick", "8", "Otomatik kayıt takılmalarını yayar"),
                T("config/paper-world-defaults.yml", "entities.behavior.disable-chest-cat-detection", "true", "Sandık açarken kedi taraması kapalı"),
                T("purpur.yml", "settings.use-alternate-keepalive", "true", "Bağlantı kopmalarını azaltır (Purpur)"),
            },
            ["Güçlü (zayıf VDS / çok oyuncu)"] = new List<Tweak>
            {
                T("server.properties", "view-distance", "6", "Görüş mesafesi daha düşük"),
                T("server.properties", "simulation-distance", "4", "İşlenen alan daha küçük"),
                T("server.properties", "entity-broadcast-range-percentage", "75", "Varlıklar daha yakından görünür"),
                T("spigot.yml", "world-settings.default.entity-activation-range.monsters", "16", ""),
                T("spigot.yml", "world-settings.default.entity-activation-range.animals", "12", ""),
                T("spigot.yml", "world-settings.default.mob-spawn-range", "4", ""),
                T("bukkit.yml", "spawn-limits.monsters", "35", ""),
                T("bukkit.yml", "spawn-limits.animals", "6", ""),
                T("bukkit.yml", "ticks-per.monster-spawns", "4", ""),
                T("config/paper-world-defaults.yml", "misc.redstone-implementation", "ALTERNATE_CURRENT", ""),
                T("config/paper-world-defaults.yml", "environment.optimize-explosions", "true", ""),
                T("config/paper-world-defaults.yml", "tick-rates.mob-spawner", "2", "Spawner'lar daha seyrek çalışır"),
                T("config/paper-world-defaults.yml", "tick-rates.grass-spread", "4", "Çim yayılması daha seyrek"),
                T("config/paper-world-defaults.yml", "tick-rates.container-update", "1", ""),
            },
            ["Varsayılana yakın (kalite öncelikli)"] = new List<Tweak>
            {
                T("server.properties", "view-distance", "10", ""),
                T("server.properties", "simulation-distance", "8", ""),
                T("config/paper-world-defaults.yml", "misc.redstone-implementation", "ALTERNATE_CURRENT", "Davranışı bozmadan hızlı redstone"),
            },
        };

        static Tweak T(string file, string key, string value, string why) => new Tweak { File = file, Key = key, Value = value, Why = why };

        public static string AikarFlags(int xmxGb) =>
            xmxGb >= 12
                ? "-XX:+UseG1GC -XX:+ParallelRefProcEnabled -XX:MaxGCPauseMillis=200 -XX:+UnlockExperimentalVMOptions -XX:+DisableExplicitGC -XX:+AlwaysPreTouch -XX:G1NewSizePercent=40 -XX:G1MaxNewSizePercent=50 -XX:G1HeapRegionSize=16M -XX:G1ReservePercent=15 -XX:G1HeapWastePercent=5 -XX:G1MixedGCCountTarget=4 -XX:InitiatingHeapOccupancyPercent=20 -XX:G1MixedGCLiveThresholdPercent=90 -XX:G1RSetUpdatingPauseTimePercent=5 -XX:SurvivorRatio=32 -XX:+PerfDisableSharedMem -XX:MaxTenuringThreshold=1 -Dusing.aikars.flags=https://mcflags.emc.gs -Daikars.new.flags=true"
                : "-XX:+UseG1GC -XX:+ParallelRefProcEnabled -XX:MaxGCPauseMillis=200 -XX:+UnlockExperimentalVMOptions -XX:+DisableExplicitGC -XX:+AlwaysPreTouch -XX:G1NewSizePercent=30 -XX:G1MaxNewSizePercent=40 -XX:G1HeapRegionSize=8M -XX:G1ReservePercent=20 -XX:G1HeapWastePercent=5 -XX:G1MixedGCCountTarget=4 -XX:InitiatingHeapOccupancyPercent=15 -XX:G1MixedGCLiveThresholdPercent=90 -XX:G1RSetUpdatingPauseTimePercent=5 -XX:SurvivorRatio=32 -XX:+PerfDisableSharedMem -XX:MaxTenuringThreshold=1 -Dusing.aikars.flags=https://mcflags.emc.gs -Daikars.new.flags=true";

        public const string VelocityFlags = "-XX:+UseG1GC -XX:G1HeapRegionSize=4M -XX:+UnlockExperimentalVMOptions -XX:+ParallelRefProcEnabled -XX:+AlwaysPreTouch -XX:MaxInlineLevel=15";

        /// <summary>Turkish help for common server.properties keys.</summary>
        public static readonly Dictionary<string, string> PropertyHelp = new Dictionary<string, string>
        {
            ["motd"] = "Sunucu listesinde görünen açıklama", ["max-players"] = "En fazla oyuncu", ["server-port"] = "Port (Velocity arkasında farklı olmalı)",
            ["online-mode"] = "Mojang doğrulaması (Velocity arkasında false)", ["view-distance"] = "Görüş mesafesi (chunk)", ["simulation-distance"] = "İşlem mesafesi (chunk)",
            ["difficulty"] = "Zorluk: peaceful/easy/normal/hard", ["gamemode"] = "Varsayılan oyun modu", ["pvp"] = "Oyuncular birbirine vurabilir",
            ["white-list"] = "Whitelist açık", ["enforce-whitelist"] = "Whitelist'te olmayanı anında at", ["spawn-protection"] = "Spawn koruma yarıçapı (0 = kapalı)",
            ["allow-flight"] = "Uçma hilesi atması kapalı (true önerilir)", ["level-name"] = "Dünya klasörü", ["level-seed"] = "Dünya tohumu",
            ["hardcore"] = "Hardcore modu", ["allow-nether"] = "Nether açık", ["max-world-size"] = "Dünya sınırı yarıçapı",
            ["network-compression-threshold"] = "Ağ sıkıştırma eşiği", ["sync-chunk-writes"] = "Chunk'ları senkron yaz (false daha hızlı)",
            ["entity-broadcast-range-percentage"] = "Varlıkların görünme mesafesi %", ["enable-command-block"] = "Komut blokları",
            ["spawn-monsters"] = "Canavarlar doğar", ["spawn-animals"] = "Hayvanlar doğar", ["player-idle-timeout"] = "AFK atma süresi (dk, 0 = kapalı)",
            ["enable-rcon"] = "RCON (uzaktan konsol)", ["rate-limit"] = "Paket hız sınırı", ["resource-pack"] = "Kaynak paketi adresi",
            ["require-resource-pack"] = "Kaynak paketi zorunlu", ["prevent-proxy-connections"] = "Proxy/VPN engeli", ["enable-query"] = "Sunucu sorgu protokolü",
            ["max-tick-time"] = "Donma bekçisi (ms, -1 = kapalı)",
        };
    }
}
