using System;
using System.Collections.Generic;

namespace LumPanel
{
    /// <summary>Builds a tailored list of Paper/Spigot/Bukkit/Purpur settings from player count, server type and priority.</summary>
    public static class Optimizer
    {
        public static readonly string[] Types = { "Survival SMP", "Lobi / Minigame", "Yaratıcı (Creative)", "Farm / teknik (redstone)", "PvP / Anarşi" };
        public static readonly string[] Priorities = { "En yüksek performans", "Dengeli", "Görsel kalite" };

        public static List<Tweak> Generate(int players, int type, int priority, int cores)
        {
            var list = new List<Tweak>();
            void Add(string file, string key, object value, string why) => list.Add(new Tweak { File = file, Key = key, Value = Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture), Why = why });
            int P(int perf, int bal, int qual) => priority == 0 ? perf : priority == 1 ? bal : qual;
            double D(double perf, double bal, double qual) => priority == 0 ? perf : priority == 1 ? bal : qual;
            bool lobby = type == 1, creative = type == 2, farm = type == 3, pvp = type == 4;
            int crowd = players > 80 ? 2 : players > 35 ? 1 : 0;   // more players -> trim harder

            // ---- server.properties
            int view = Math.Max(4, P(6, 8, 10) - crowd + (lobby ? -1 : 0));
            int sim = Math.Min(view, Math.Max(3, P(4, 6, 8) - crowd + (farm ? 2 : 0) + (lobby ? -1 : 0)));
            const string sp = "server.properties";
            Add(sp, "view-distance", view, $"Görüş mesafesi ({players} oyuncu için)");
            Add(sp, "simulation-distance", sim, farm ? "Farmların çalışması için daha geniş tutuldu" : "Mob/redstone işlenen mesafe");
            Add(sp, "max-players", Math.Max(players, 10), "Beklenen oyuncu sayısı");
            Add(sp, "network-compression-threshold", 256, "Ağ yükü dengesi");
            Add(sp, "sync-chunk-writes", "false", "Chunk kaydı ana iş parçacığını bekletmez");
            Add(sp, "entity-broadcast-range-percentage", P(70, 90, 100), "Varlıkların görünme mesafesi");
            if (lobby) { Add(sp, "spawn-monsters", "false", "Lobide canavar yok"); Add(sp, "spawn-animals", "false", "Lobide hayvan yok"); }

            // ---- spigot.yml
            const string sy = "spigot.yml", w = "world-settings.default.";
            bool keepFarms = farm || priority == 2;
            Add(sy, w + "entity-activation-range.animals", keepFarms ? 32 : P(12, 16, 32), "Uzak hayvanlar seyrek işlenir");
            Add(sy, w + "entity-activation-range.monsters", keepFarms ? 32 : P(18, 24, 32), "Uzak canavarlar seyrek işlenir");
            Add(sy, w + "entity-activation-range.raiders", P(32, 48, 64), "Baskın mobları");
            Add(sy, w + "entity-activation-range.misc", P(6, 8, 16), "Eşya, ok vb.");
            Add(sy, w + "entity-activation-range.water", P(8, 12, 16), "Su canlıları");
            Add(sy, w + "entity-activation-range.villagers", farm ? 32 : P(12, 16, 32), "Köylüler (demir farmı varsa yüksek kalmalı)");
            Add(sy, w + "entity-activation-range.flying-monsters", P(32, 48, 48), "Uçan canavarlar");
            Add(sy, w + "entity-tracking-range.players", P(48, 64, 128), "Oyuncuların görünme mesafesi");
            Add(sy, w + "entity-tracking-range.animals", P(32, 48, 96), "");
            Add(sy, w + "entity-tracking-range.monsters", P(32, 48, 96), "");
            Add(sy, w + "entity-tracking-range.misc", P(16, 32, 96), "");
            Add(sy, w + "merge-radius.item", creative ? 2.5 : D(4.0, 3.5, 2.5), "Yerdeki eşyalar birleşir");
            Add(sy, w + "merge-radius.exp", D(6.0, 4.0, 3.0), "XP küreleri birleşir");
            Add(sy, w + "mob-spawn-range", Math.Max(3, Math.Min(8, sim - 1)), "simulation-distance ile uyumlu doğma mesafesi");
            Add(sy, w + "nerf-spawner-mobs", farm ? "false" : (priority == 0 ? "true" : "false"), "Spawner mobları yapay zekasız (farm sunucusunda kapalı)");
            Add(sy, w + "item-despawn-rate", P(3000, 6000, 6000), "Yerdeki eşya kaybolma süresi (tick)");
            Add(sy, w + "arrow-despawn-rate", P(200, 600, 1200), "Saplanan okların kaybolması");
            Add(sy, w + "tick-inactive-villagers", farm ? "true" : (priority == 0 ? "false" : "true"), "Uzak köylüler işlenir mi");
            Add(sy, w + "hopper-amount", 1, "");
            Add(sy, w + "ticks-per.hopper-transfer", 8, "Hopper aktarım hızı (vanilla 8)");

            // ---- bukkit.yml
            const string by = "bukkit.yml";
            double scale = crowd == 2 ? 0.7 : crowd == 1 ? 0.85 : 1.0;
            int Lim(int v) => lobby ? 0 : (int)Math.Round(v * scale);
            Add(by, "spawn-limits.monsters", Lim(P(35, 50, 70)), "Oyuncu başına canavar sınırı");
            Add(by, "spawn-limits.animals", Lim(P(6, 8, 10)), "Hayvan sınırı");
            Add(by, "spawn-limits.water-animals", Lim(P(3, 5, 5)), "Su hayvanları");
            Add(by, "spawn-limits.water-ambient", Lim(P(2, 10, 20)), "Balık sürüleri");
            Add(by, "spawn-limits.water-underground-creature", Lim(P(3, 5, 5)), "");
            Add(by, "spawn-limits.axolotls", Lim(P(3, 5, 5)), "");
            Add(by, "spawn-limits.ambient", Lim(P(1, 5, 15)), "Yarasalar (sadece görsel)");
            Add(by, "ticks-per.monster-spawns", P(4, 2, 1), "Canavar doğurma sıklığı");
            Add(by, "ticks-per.animal-spawns", 400, "");
            Add(by, "ticks-per.water-spawns", P(400, 1, 1), "");
            Add(by, "ticks-per.ambient-spawns", P(400, 1, 1), "");
            Add(by, "chunk-gc.period-in-ticks", 400, "Boş chunk'ları bellekten atma");

            // ---- paper-world-defaults.yml
            const string pw = "config/paper-world-defaults.yml";
            Add(pw, "environment.optimize-explosions", "true", "Patlama hesabı hızlı");
            Add(pw, "misc.redstone-implementation", "ALTERNATE_CURRENT", "Hızlı redstone, davranış aynı");
            Add(pw, "chunks.prevent-moving-into-unloaded-chunks", "true", "Yüklenmemiş chunk'a girme takılmaları");
            Add(pw, "chunks.max-auto-save-chunks-per-tick", P(6, 8, 12), "Kayıt takılmalarını yayar");
            Add(pw, "chunks.delay-chunk-unloads-by", "10s", "Chunk'lar hemen boşaltılmaz (titreme azalır)");
            Add(pw, "tick-rates.mob-spawner", P(2, 1, 1), "Spawner çalışma sıklığı");
            Add(pw, "tick-rates.grass-spread", P(4, 2, 1), "Çim yayılma sıklığı");
            Add(pw, "tick-rates.container-update", 1, "");
            Add(pw, "entities.behavior.disable-chest-cat-detection", "true", "Sandık açarken kedi taraması");
            Add(pw, "collisions.max-entity-collisions", P(2, 4, 8), "Varlık çarpışma hesabı");
            Add(pw, "entities.armor-stands.tick", lobby || creative ? "false" : "true", "Zırh askıları işlenmez (lobide gereksiz)");
            Add(pw, "entities.armor-stands.do-collision-entity-lookups", "false", "");
            Add(pw, "hopper.cooldown-when-full", "true", "Dolu hopper boşuna çalışmaz");
            Add(pw, "hopper.disable-move-event", priority == 0 && !farm ? "true" : "false", "Hopper olayı (bazı koruma eklentileri ihtiyaç duyar)");
            Add(pw, "misc.update-pathfinding-on-block-update", priority == 0 ? "false" : "true", "Blok değişince mob yol bulma güncellemesi");
            Add(pw, "environment.treasure-maps.enabled", priority == 0 ? "false" : "true", "Hazine haritaları (yapı aramak ağır)");
            if (pvp) Add(pw, "entities.behavior.disable-player-crits", "false", "");

            // ---- paper-global.yml
            const string pg = "config/paper-global.yml";
            if (cores >= 4) Add(pg, "chunk-system.worker-threads", Math.Max(1, cores / 2), $"{cores} çekirdeğin yarısı chunk üretimine");
            Add(pg, "chunk-loading-basic.player-max-chunk-load-rate", D(100.0, 200.0, -1.0), "Oyuncu başına chunk yükleme hızı");
            if (lobby) Add(pg, "collisions.enable-player-collisions", "false", "Lobide oyuncular birbirini itmez");

            // ---- purpur.yml
            const string pu = "purpur.yml";
            Add(pu, "settings.use-alternate-keepalive", "true", "Bağlantı kopmalarını azaltır");
            Add(pu, "world-settings.default.mobs.villager.lobotomize.enabled", farm ? "false" : (priority == 0 ? "true" : "false"), "Hapsedilmiş köylüleri hafifletir");
            Add(pu, "world-settings.default.mobs.zombie.aggressive-towards-villager-when-lagging", "false", "Lag anında köylü avı yok");
            return list;
        }
    }
}
