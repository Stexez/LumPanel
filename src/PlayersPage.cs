using System;
using System.Drawing;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace LumPanel
{
    public class PlayersPage : Page
    {
        public override string Title => "Oyuncular";
        static readonly Regex NameRe = new Regex("^[A-Za-z0-9_]{2,16}$");

        readonly ComboBox server = Theme.Combo();
        readonly ListView list = Theme.List();
        readonly TextBox player = Theme.TextBox();
        readonly TextBox reason = Theme.TextBox();
        readonly TextBox broadcast = Theme.TextBox();
        readonly Label count = Theme.Label("", Theme.Bold, Theme.Muted);
        string[] lastPlayers = new string[0];

        public PlayersPage(ServiceManager m, MainForm f) : base(m, f)
        {
            server.Width = 240;
            var top = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 44, BackColor = Theme.Bg, WrapContents = false };
            var refresh = DarkButton.Sized("↻ Listeyi yenile", 140);
            var serverLabel = Theme.Label("Sunucu:", Theme.Bold);
            serverLabel.Margin = new Padding(0, 10, 6, 0);
            server.Margin = new Padding(0, 6, 10, 0);
            count.Margin = new Padding(14, 10, 0, 0);
            top.Controls.AddRange(new Control[] { serverLabel, server, refresh, count });

            list.Columns.Add("Oyuncu", 260);
            list.Dock = DockStyle.Fill;
            var listCard = new Card { Dock = DockStyle.Left, Width = 320, Padding = new Padding(10) };
            listCard.Controls.Add(list);

            var actions = new Card { Dock = DockStyle.Fill, Padding = new Padding(20) };
            var flow = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true, BackColor = Theme.Card };
            actions.Controls.Add(flow);
            Theme.Dark(flow);

            player.Width = reason.Width = 420;
            broadcast.Width = 420;
            flow.Controls.Add(Section("Oyuncu", "Listeden seç ya da adını yaz (çevrimdışı oyuncular için de çalışır)."));
            flow.Controls.Add(player);
            flow.Controls.Add(Section("Sebep / mesaj", "At, yasakla ve özel mesaj için."));
            flow.Controls.Add(reason);
            flow.Controls.Add(Row(
                Act("✉ Özel mesaj", p => $"tell {p} {Need(reason.Text, "Mesaj yaz.")}"),
                Act("⏏ At (kick)", p => $"kick {p} {reason.Text}".Trim()),
                Act("⛔ Yasakla", p => $"ban {p} {reason.Text}".Trim(), confirm: true, color: Theme.Red),
                Act("✔ Yasağı kaldır", p => $"pardon {p}")));
            flow.Controls.Add(Row(
                Act("★ OP ver", p => $"op {p}", confirm: true),
                Act("☆ OP al", p => $"deop {p}"),
                Act("+ Whitelist", p => $"whitelist add {p}"),
                Act("− Whitelist", p => $"whitelist remove {p}")));
            flow.Controls.Add(Row(
                Act("Survival", p => $"gamemode survival {p}"),
                Act("Creative", p => $"gamemode creative {p}", confirm: true),
                Act("Spectator", p => $"gamemode spectator {p}"),
                Act("♥ İyileştir", p => $"effect give {p} minecraft:instant_health 1 10")));
            flow.Controls.Add(Section("Herkese", "Sunucudaki herkese gider."));
            flow.Controls.Add(broadcast);
            var say = DarkButton.Sized("📢 Duyuru (say)", 160, Theme.Accent);
            var title = DarkButton.Sized("Ekrana başlık", 140);
            var save = DarkButton.Sized("💾 Dünyayı kaydet", 160);
            say.Click += (s, e) => { if (broadcast.Text.Trim().Length > 0) Send("say " + broadcast.Text.Trim()); };
            title.Click += (s, e) =>
            {
                string t = broadcast.Text.Trim().Replace("\\", "\\\\").Replace("\"", "\\\"");
                if (t.Length > 0) Send("title @a title {\"text\":\"" + t + "\",\"color\":\"gold\"}");
            };
            save.Click += (s, e) => Send("save-all");
            flow.Controls.Add(Row(say, title, save));

            var body = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Bg };
            var gap = new Panel { Dock = DockStyle.Left, Width = 14, BackColor = Theme.Bg };
            body.Controls.Add(actions);
            body.Controls.Add(gap);
            body.Controls.Add(listCard);

            Controls.Add(body);
            Controls.Add(top);
            Controls.Add(Header("Oyuncular", "Çevrimiçi oyuncular (giriş/çıkış satırlarından ve 'list' komutundan). İşlemler seçili sunucunun konsoluna komut olarak gider."));

            server.SelectedIndexChanged += (s, e) => { lastPlayers = null; RefreshView(); };
            refresh.Click += (s, e) => Send("list");
            list.SelectedIndexChanged += (s, e) => { if (list.SelectedItems.Count > 0) player.Text = list.SelectedItems[0].Text; };
            ServicesChanged();
        }

        static Control Section(string title, string hint)
        {
            var p = new Panel { Width = 640, Height = 44, BackColor = Theme.Card, Margin = new Padding(0, 12, 0, 2) };
            p.Controls.Add(new Label { Text = hint, Font = Theme.Small, ForeColor = Theme.Muted, AutoSize = true, Location = new Point(0, 24) });
            p.Controls.Add(new Label { Text = title, Font = Theme.H2, ForeColor = Theme.Text, AutoSize = true, Location = new Point(-2, 0) });
            return p;
        }

        static Control Row(params Control[] controls)
        {
            var row = new FlowLayoutPanel { AutoSize = true, BackColor = Theme.Card, Margin = new Padding(0, 10, 0, 0), WrapContents = true, MaximumSize = new Size(680, 0) };
            foreach (var c in controls) { c.Margin = new Padding(0, 0, 8, 6); row.Controls.Add(c); }
            return row;
        }

        static string Need(string value, string message)
        {
            if (string.IsNullOrWhiteSpace(value)) throw new InvalidOperationException(message);
            return value.Trim();
        }

        DarkButton Act(string text, Func<string, string> command, bool confirm = false, Color? color = null)
        {
            var b = DarkButton.Sized(text, 150, color);
            b.Click += (s, e) =>
            {
                string name = player.Text.Trim();
                if (!NameRe.IsMatch(name)) { MessageBox.Show(Main, "Geçerli bir oyuncu adı seç ya da yaz.", "Oyuncular"); return; }
                string cmd;
                try { cmd = command(name); }
                catch (InvalidOperationException ex) { MessageBox.Show(Main, ex.Message, "Oyuncular"); return; }
                if (confirm && MessageBox.Show(Main, $"{cmd}\n\nGönderilsin mi?", "Onay", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
                Send(cmd);
            };
            return b;
        }

        ServiceRunner Current => server.SelectedItem as ServiceRunner;

        void Send(string cmd)
        {
            var r = Current;
            if (r == null) { MessageBox.Show(Main, "Önce bir sunucu seç.", "Oyuncular"); return; }
            string err = r.Send(cmd);
            if (err != null) MessageBox.Show(Main, err, r.Config.Name);
            else Main.Manager.AddEvent(r.Config.Name, "komut: " + cmd);
        }

        public override void ServicesChanged()
        {
            var keep = Current;
            server.Items.Clear();
            foreach (var r in Manager.Runners.Where(r => r.Config.Kind == ServiceKinds.Minecraft || r.Config.Kind == ServiceKinds.Proxy))
                server.Items.Add(r);
            if (keep != null && server.Items.Contains(keep)) server.SelectedItem = keep;
            else if (server.Items.Count > 0) server.SelectedIndex = 0;
        }

        public override void RefreshView()
        {
            var r = Current;
            var names = r?.Players ?? new string[0];
            count.Text = r == null ? "Minecraft sunucusu tanımlı değil" :
                r.State != ServiceState.Running ? Theme.StateText(r.State) :
                $"{names.Length} oyuncu çevrimiçi" + (r.MaxPlayers > 0 ? $" / {r.MaxPlayers}" : "");
            if (lastPlayers != null && names.SequenceEqual(lastPlayers)) return;
            lastPlayers = names;
            string sel = list.SelectedItems.Count > 0 ? list.SelectedItems[0].Text : null;
            list.BeginUpdate();
            list.Items.Clear();
            foreach (var n in names)
            {
                var item = list.Items.Add(n);
                if (n == sel) item.Selected = true;
            }
            list.EndUpdate();
        }
    }
}
