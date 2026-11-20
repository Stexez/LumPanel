using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace LumPanel
{
    public class DashboardPage : Page
    {
        public override string Title => "Genel Bakış";
        readonly Card hero = new Card { Dock = DockStyle.Top, Height = 168, Fill = Color.FromArgb(52, 34, 88), FillTo = Color.FromArgb(26, 24, 36), Padding = new Padding(22), Radius = 16 };
        readonly Sparkline heroSpark = new Sparkline { Size = new Size(260, 46), Color = Theme.Accent };
        readonly StatTile tileServices = new StatTile("Açık servis", Theme.Green);
        readonly StatTile tilePlayers = new StatTile("Çevrimiçi oyuncu", Theme.Accent);
        readonly StatTile tileCpu = new StatTile("Sistem CPU", Theme.Yellow);
        readonly StatTile tileRam = new StatTile("Bellek", Theme.Blue);
        readonly StatusLight heroLight = new StatusLight(64) { Location = new Point(24, 30) };
        readonly Label heroName = Theme.Label("LumSMP", new Font("Segoe UI Semibold", 22f));
        readonly Label heroState = Theme.Label("Kapalı", new Font("Segoe UI Semibold", 13f), Theme.Red);
        readonly Label heroInfo = Theme.Label("", Theme.Body, Theme.Muted);
        readonly DarkButton heroToggle = DarkButton.Sized("AÇ", 150, Theme.Green);
        readonly DarkButton heroRestart = DarkButton.Sized("↻ Yeniden başlat", 150);
        readonly DarkButton heroConsole = DarkButton.Sized("▤ Konsol", 150);
        readonly FlowLayoutPanel grid = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoScroll = true, BackColor = Theme.Bg, Padding = new Padding(0, 14, 0, 0) };
        readonly Dictionary<ServiceRunner, ServiceCard> cards = new Dictionary<ServiceRunner, ServiceCard>();
        ServiceRunner featured;

        public DashboardPage(ServiceManager m, MainForm f) : base(m, f)
        {
            var toolbar = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 46, BackColor = Theme.Bg, Padding = new Padding(0, 4, 0, 6) };
            var startAll = DarkButton.Sized("▶ Hepsini sırayla başlat", 190, Theme.Accent);
            var stopAll = DarkButton.Sized("■ Hepsini durdur", 150);
            startAll.Click += (s, e) => Manager.StartAll(false);
            stopAll.Click += (s, e) =>
            {
                if (MessageBox.Show(Main, "Bütün servisler kapatılsın mı?", "Hepsini durdur", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
                    Manager.StopAll();
            };
            toolbar.Controls.Add(startAll);
            toolbar.Controls.Add(stopAll);

            heroName.Location = new Point(110, 26);
            heroState.Location = new Point(112, 70);
            heroInfo.Location = new Point(113, 100);
            heroToggle.Height = 46;
            heroToggle.Font = new Font("Segoe UI Semibold", 13f);
            heroRestart.Height = heroConsole.Height = 34;
            hero.Controls.AddRange(new Control[] { heroLight, heroName, heroState, heroInfo, heroToggle, heroRestart, heroConsole, heroSpark });
            foreach (Control c in hero.Controls) if (c is Label) c.BackColor = Color.Transparent;
            hero.Resize += (s, e) => LayoutHero();
            heroToggle.Click += (s, e) => Toggle(featured);
            heroRestart.Click += (s, e) => featured?.Stop(restart: true);
            heroConsole.Click += (s, e) => { if (featured != null) Main.ShowConsole(featured); };

            Theme.Dark(grid);
            var spacer = new Panel { Dock = DockStyle.Top, Height = 8, BackColor = Theme.Bg };
            var tiles = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 104, BackColor = Theme.Bg, WrapContents = false, Padding = new Padding(0, 4, 0, 10) };
            tiles.Controls.AddRange(new Control[] { tileServices, tilePlayers, tileCpu, tileRam });
            Controls.Add(grid);
            Controls.Add(spacer);
            Controls.Add(hero);
            Controls.Add(tiles);
            Controls.Add(toolbar);
            Controls.Add(Header("Genel Bakış", "Sunucuların durumu. Yeşil: açık · Sarı: açılıyor/kapanıyor · Kırmızı: kapalı · Turuncu: panel dışında açık"));
            ServicesChanged();
        }

        void LayoutHero()
        {
            int right = hero.Width - 24 - 150;
            heroToggle.Location = new Point(right, 22);
            heroRestart.Location = new Point(right, 76);
            heroConsole.Location = new Point(right, 116);
            heroSpark.Location = new Point(Math.Max(420, right - 290), 100);
            heroSpark.Visible = hero.Width > 900;
        }

        public static void Toggle(ServiceRunner r)
        {
            if (r == null) return;
            if (r.IsAlive || r.State == ServiceState.External) r.Stop();
            else
            {
                string err = r.Start();
                if (err != null) MessageBox.Show(err, r.Config.Name, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        public override void ServicesChanged()
        {
            featured = Manager.Runners.FirstOrDefault(r => r.Config.Featured)
                       ?? Manager.Runners.FirstOrDefault(r => r.Config.Kind == ServiceKinds.Minecraft);
            grid.SuspendLayout();
            foreach (var c in cards.Values) c.Dispose();
            cards.Clear();
            grid.Controls.Clear();
            foreach (var r in Manager.Runners)
            {
                var card = new ServiceCard(r, Main);
                cards[r] = card;
                grid.Controls.Add(card);
            }
            if (Manager.Runners.Count == 0)
                grid.Controls.Add(Theme.Label("Henüz servis yok. Ayarlar → \"Otomatik bul\" ile sunucuları ekle.", Theme.H2, Theme.Muted));
            grid.ResumeLayout();
            LayoutHero();
            RefreshView();
        }

        public override void RefreshView()
        {
            if (featured == null)
            {
                heroName.Text = "LumSMP";
                heroState.Text = "Tanımlı değil";
                heroState.ForeColor = Theme.Muted;
                heroInfo.Text = "Ayarlar → Otomatik bul ile LumSMP sunucusunu ekle.";
                heroToggle.Enabled = heroRestart.Enabled = heroConsole.Enabled = false;
                heroLight.SetState(ServiceState.Stopped);
            }
            else
            {
                var r = featured;
                heroToggle.Enabled = heroRestart.Enabled = heroConsole.Enabled = true;
                heroName.Text = r.Config.Name;
                heroState.Text = Theme.StateText(r.State);
                heroState.ForeColor = Theme.StateColor(r.State);
                heroLight.SetState(r.State);
                bool on = r.IsAlive || r.State == ServiceState.External;
                heroToggle.Text = on ? "KAPAT" : "AÇ";
                heroToggle.SetColor(on ? Theme.Red : Theme.Green);
                heroToggle.Enabled = r.State != ServiceState.Stopping;
                string players = r.State == ServiceState.Running ? $"Oyuncu {r.Players.Length}{(r.MaxPlayers > 0 ? "/" + r.MaxPlayers : "")}   ·   " : "";
                heroInfo.Text = r.RootPid > 0
                    ? $"{players}Açık kalma {Theme.Uptime(r.StartedAt)}   ·   CPU %{r.CpuPercent:0}   ·   RAM {Theme.Bytes(r.MemoryBytes)}"
                    : r.LastExit.Length > 0 ? "Son kapanış: " + r.LastExit : "Kapalı";
            }
            foreach (var c in cards.Values) c.RefreshView();
            if (featured != null)
            {
                heroSpark.Values = featured.CpuHistory;
                heroSpark.Color = Theme.StateColor(featured.State) == Theme.Red ? Theme.Muted : Theme.Accent;
                heroSpark.Invalidate();
                hero.Accent = Theme.StateColor(featured.State);
                hero.Invalidate();
            }
            int up = Manager.Runners.Count(r => r.State == ServiceState.Running);
            int crashed = Manager.Runners.Count(r => r.State == ServiceState.Crashed);
            tileServices.Set($"{up} / {Manager.Runners.Count}", crashed > 0 ? $"{crashed} servis çöktü!" : "hepsi yolunda");
            var mc = Manager.Runners.Where(r => r.Config.Kind == ServiceKinds.Minecraft && r.State == ServiceState.Running).ToList();
            tilePlayers.Set(mc.Sum(r => r.Players.Length).ToString(), mc.Count == 0 ? "açık sunucu yok" : string.Join(" · ", mc.Select(r => $"{r.Config.Name} {r.Players.Length}")));
            var sys = Manager.Sys;
            tileCpu.Set($"%{sys.CpuPercent:0}", $"{Environment.ProcessorCount} çekirdek");
            tileRam.Set($"{sys.MemUsedGb:0.0} GB", $"/ {sys.MemTotalGb:0.0} GB · disk {sys.DiskFreeGb:0} GB boş");
        }
    }

    public class ServiceCard : Card
    {
        readonly ServiceRunner runner;
        readonly StatusLight light = new StatusLight(22) { Location = new Point(14, 16) };
        readonly Label name = Theme.Label("", Theme.H2);
        readonly Label kind = Theme.Label("", Theme.Small, Theme.Muted);
        readonly Label state = Theme.Label("", Theme.Bold);
        readonly Label info = Theme.Label("", Theme.Small, Theme.Muted);
        readonly DarkButton toggle = DarkButton.Sized("Başlat", 96, Theme.Green);
        readonly DarkButton restart = DarkButton.Sized("↻", 40);
        readonly DarkButton console = DarkButton.Sized("Konsol", 80);
        readonly Sparkline spark = new Sparkline { Size = new Size(92, 34), Location = new Point(222, 60) };

        public ServiceCard(ServiceRunner r, MainForm main)
        {
            runner = r;
            Size = new Size(330, 156);
            FillTo = Color.FromArgb(24, 22, 33);
            Margin = new Padding(0, 0, 14, 14);
            name.Location = new Point(44, 12);
            kind.Location = new Point(46, 36);
            state.Location = new Point(14, 62);
            info.Location = new Point(14, 84);
            light.Location = new Point(18, 16);
            name.Location = new Point(48, 12);
            kind.Location = new Point(50, 36);
            state.Location = new Point(18, 62);
            info.Location = new Point(18, 86);
            toggle.Location = new Point(18, 112);
            restart.Location = new Point(120, 112);
            console.Location = new Point(166, 112);
            Controls.AddRange(new Control[] { light, name, kind, state, info, toggle, restart, console, spark });
            foreach (Control c in Controls) if (c is Label) c.BackColor = Color.Transparent;
            toggle.Click += (s, e) => DashboardPage.Toggle(runner);
            restart.Click += (s, e) => runner.Stop(restart: true);
            console.Click += (s, e) => main.ShowConsole(runner);
            new ToolTip().SetToolTip(restart, "Yeniden başlat");
            RefreshView();
        }

        public void RefreshView()
        {
            var c = runner.Config;
            name.Text = c.Name + (c.Featured ? "  ★" : "");
            kind.Text = ServiceKinds.Label(c.Kind) + (c.AutoRestart ? " · çökünce yeniden başlar" : "");
            state.Text = Theme.StateText(runner.State);
            state.ForeColor = Theme.StateColor(runner.State);
            light.SetState(runner.State);
            var accent = Theme.StateColor(runner.State);
            if (Accent != accent) { Accent = accent; Invalidate(); }
            spark.Values = runner.CpuHistory;
            spark.Color = runner.RootPid > 0 ? Theme.Accent : Theme.Border;
            spark.Invalidate();
            bool on = runner.IsAlive || runner.State == ServiceState.External;
            toggle.Text = on ? "Durdur" : "Başlat";
            toggle.SetColor(on ? Theme.Red : Theme.Green);
            toggle.Enabled = runner.State != ServiceState.Stopping;
            string players = (c.Kind == ServiceKinds.Minecraft || c.Kind == ServiceKinds.Proxy) && runner.State == ServiceState.Running
                ? $"👥 {runner.Players.Length}   " : "";
            info.Text = runner.RootPid > 0
                ? $"{players}⏱ {Theme.Uptime(runner.StartedAt)}   CPU %{runner.CpuPercent:0}   RAM {Theme.Bytes(runner.MemoryBytes)}"
                : runner.LastExit.Length > 0 ? "Son kapanış: " + runner.LastExit : "";
        }
    }
}
