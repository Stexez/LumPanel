using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using Microsoft.Win32;

namespace LumPanel
{
    public class MainForm : Form
    {
        public readonly ServiceManager Manager;
        readonly Panel content = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Bg };
        readonly GradientPanel sidebar = new GradientPanel { Dock = DockStyle.Left, Width = 224 };
        readonly Label sidebarStatus = new Label { Dock = DockStyle.Bottom, Height = 130, ForeColor = Theme.Muted, Font = Theme.Small, BackColor = Color.Transparent, Padding = new Padding(22, 8, 8, 8) };
        readonly Label statusBar = new Label
        {
            Dock = DockStyle.Bottom, Height = 26, BackColor = Theme.Sidebar, ForeColor = Theme.Muted, Font = Theme.Small,
            TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(12, 0, 0, 0),
        };
        readonly List<(NavButton button, Page page)> pages = new List<(NavButton, Page)>();
        readonly NotifyIcon tray = new NotifyIcon();
        readonly Timer uiTimer = new Timer { Interval = 1000 };
        Page current;
        bool reallyExit;

        public DashboardPage Dashboard { get; }
        public ConsolePage Consoles { get; }
        public PlayersPage Players { get; }

        public MainForm(ServiceManager manager)
        {
            Manager = manager;
            Text = "LumPanel · LUM Craft sunucu yönetimi";
            BackColor = Theme.Bg;
            ForeColor = Theme.Text;
            Font = Theme.Body;
            MinimumSize = new Size(1100, 700);
            Size = new Size(1320, 820);
            StartPosition = FormStartPosition.CenterScreen;
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }
            DoubleBuffered = true;

            BuildSidebar();
            Dashboard = new DashboardPage(manager, this);
            Consoles = new ConsolePage(manager, this);
            Players = new PlayersPage(manager, this);
            AddPage("◉", "Genel Bakış", Dashboard);
            AddPage("▤", "Konsollar", Consoles);
            AddPage("☺", "Oyuncular", Players);
            AddPage("✦", "Yapay Zeka", new AiPage(manager, this));
            AddPage("⏱", "Zamanlayıcı", new SchedulePage(manager, this));
            AddPage("⛁", "Yedekler", new BackupsPage(manager, this));
            AddPage("☰", "Olay Kaydı", new EventsPage(manager, this));
            AddPage("⚙", "Ayarlar", new SettingsPage(manager, this));

            Controls.Add(content);
            Controls.Add(sidebar);
            Controls.Add(statusBar);

            BuildTray();
            uiTimer.Tick += (s, e) => Tick();
            uiTimer.Start();
            ShowPage(pages[0].page);
            Manager.EventAdded += ev =>
            {
                if (ev.Important && IsHandleCreated)
                    BeginInvoke(new Action(() => tray.ShowBalloonTip(5000, ev.Service, ev.Message, ToolTipIcon.Warning)));
            };
        }

        void BuildSidebar()
        {
            var logo = new Panel { Dock = DockStyle.Top, Height = 104, BackColor = Color.Transparent };
            var pic = new PictureBox { Size = new Size(44, 44), Location = new Point(20, 26), SizeMode = PictureBoxSizeMode.Zoom, BackColor = Color.Transparent };
            try { pic.Image = new Icon(Icon ?? SystemIcons.Application, 64, 64).ToBitmap(); } catch { }
            logo.Controls.Add(pic);
            logo.Controls.Add(new Label { Text = "LumPanel", Font = new Font("Segoe UI Semibold", 17f), ForeColor = Theme.Text, AutoSize = true, Location = new Point(70, 24), BackColor = Color.Transparent });
            logo.Controls.Add(new Label { Text = "LUM CRAFT", Font = new Font("Segoe UI Semibold", 7.5f), ForeColor = Theme.Accent, AutoSize = true, Location = new Point(73, 56), BackColor = Color.Transparent });
            sidebar.Controls.Add(logo);
            sidebar.Controls.Add(sidebarStatus);
            sidebarStatus.Paint += PaintSidebarStatus;
        }

        void PaintSidebarStatus(object sender, PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            TextRenderer.DrawText(g, "SERVİSLER", new Font("Segoe UI Semibold", 7.5f), new Point(22, 4), Theme.Muted);
            int y = 24;
            foreach (var r in Manager.Runners.Take(6))
            {
                using (var b = new SolidBrush(Theme.StateColor(r.State))) g.FillEllipse(b, 24, y + 4, 8, 8);
                TextRenderer.DrawText(g, r.Config.Name, Theme.Small, new Rectangle(40, y, sidebarStatus.Width - 46, 16), Theme.Text, TextFormatFlags.EndEllipsis);
                y += 17;
            }
        }

        void AddPage(string icon, string text, Page page)
        {
            var b = new NavButton(icon, text) { Dock = DockStyle.Top };
            b.Click += (s, e) => ShowPage(page);
            pages.Add((b, page));
            // Dock=Top stacks in reverse add order: insert right after the logo
            sidebar.Controls.Add(b);
            sidebar.Controls.SetChildIndex(b, 0);
            page.Visible = false;
            content.Controls.Add(page);
        }

        public void ShowPage(Page page)
        {
            foreach (var (button, p) in pages)
            {
                bool on = p == page;
                button.Active = on;
                p.Visible = on;
            }
            current = page;
            page.OnShown();
        }

        public void ShowConsole(ServiceRunner r)
        {
            ShowPage(Consoles);
            Consoles.Select(r);
        }

        public void ServicesChanged()
        {
            foreach (var (_, p) in pages) p.ServicesChanged();
        }

        void Tick()
        {
            current?.RefreshView();
            sidebarStatus.Invalidate();
            var s = Manager.Sys;
            int up = Manager.Runners.Count(r => r.State == ServiceState.Running);
            statusBar.Text = $"  CPU %{s.CpuPercent:0}    RAM {s.MemUsedGb:0.0} / {s.MemTotalGb:0.0} GB    Disk {s.DiskName} {s.DiskFreeGb:0} GB boş    ·    {up}/{Manager.Runners.Count} servis açık    ·    {DateTime.Now:HH:mm:ss}";
            tray.Text = $"LumPanel · {up}/{Manager.Runners.Count} servis açık";
        }

        void BuildTray()
        {
            tray.Icon = Icon ?? SystemIcons.Application;
            tray.Visible = true;
            var menu = new ContextMenuStrip();
            menu.Items.Add("Paneli göster", null, (s, e) => ShowFromTray());
            menu.Items.Add("Hepsini başlat", null, (s, e) => Manager.StartAll(false));
            menu.Items.Add("Hepsini durdur", null, (s, e) => Manager.StopAll());
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Çıkış", null, (s, e) => ExitApp());
            tray.ContextMenuStrip = menu;
            tray.DoubleClick += (s, e) => ShowFromTray();
        }

        void ShowFromTray()
        {
            Show();
            if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
            Activate();
        }

        public void ExitApp()
        {
            int alive = Manager.Runners.Count(r => r.IsOwned);
            if (alive > 0)
            {
                var answer = MessageBox.Show(this,
                    $"{alive} servis panelden çalışıyor.\n\nEvet: hepsini düzgünce kapat ve çık\nHayır: açık bırak ve çık (çıktıları görünmez olur)\nİptal: vazgeç",
                    "LumPanel'den çık", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
                if (answer == DialogResult.Cancel) return;
                if (answer == DialogResult.Yes)
                {
                    Cursor = Cursors.WaitCursor;
                    statusBar.Text = "  Servisler kapatılıyor, lütfen bekle...";
                    statusBar.Refresh();
                    Manager.StopAll(wait: true);
                }
            }
            reallyExit = true;
            tray.Visible = false;
            Close();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (e.CloseReason == CloseReason.WindowsShutDown)
            {
                Manager.StopAll(wait: true);
                tray.Visible = false;
                base.OnFormClosing(e);
                return;
            }
            if (!reallyExit && Manager.Config.MinimizeToTray && e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                Hide();
                tray.ShowBalloonTip(3000, "LumPanel arka planda", "Servisler çalışmaya devam ediyor. Simgeye çift tıklayarak geri aç.", ToolTipIcon.Info);
                return;
            }
            if (!reallyExit) { e.Cancel = true; ExitApp(); return; }
            tray.Visible = false;
            base.OnFormClosing(e);
        }

        public static void SetStartWithWindows(bool on)
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true);
            if (key == null) return;
            if (on) key.SetValue("LumPanel", $"\"{Application.ExecutablePath}\" --autostart");
            else key.DeleteValue("LumPanel", false);
        }
    }
}
