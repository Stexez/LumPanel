using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace LumPanel
{
    public class SchedulePage : Page
    {
        public override string Title => "Zamanlayıcı";
        readonly ListView list = Theme.List();
        readonly ComboBox service = Theme.Combo();
        readonly ComboBox type = Theme.Combo();
        readonly TextBox time = Theme.TextBox("05:00");
        readonly NumericUpDown interval = Theme.Number(1, 1440, 90, 30);
        readonly TextBox command = Theme.TextBox("save-all");
        readonly CheckBox warn = Theme.Check("Oyunculara 10 dk / 5 dk / 1 dk / 10 sn önce uyarı gönder");
        readonly CheckBox enabled = Theme.Check("Etkin");
        ScheduleConfig editing;
        static readonly object AllServers = new AllTarget();
        sealed class AllTarget { public override string ToString() => "Tüm Minecraft sunucuları"; }

        static readonly string[] Types = { "restart", "command", "interval", "backup" };
        static readonly string[] TypeNames = { "Her gün yeniden başlat", "Her gün komut çalıştır", "Belirli aralıkla komut çalıştır", "Her gün dünya yedeği al" };

        public SchedulePage(ServiceManager m, MainForm f) : base(m, f)
        {
            list.Columns.Add("Durum", 80);
            list.Columns.Add("Görev", 560);
            list.Dock = DockStyle.Fill;
            var listCard = new Card { Dock = DockStyle.Fill, Padding = new Padding(10) };
            listCard.Controls.Add(list);

            var form = new Card { Dock = DockStyle.Right, Width = 430, Padding = new Padding(18) };
            var flow = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, BackColor = Theme.Card, AutoScroll = true };
            form.Controls.Add(flow);
            service.Width = type.Width = command.Width = 380;
            time.Width = 80;
            foreach (var n in TypeNames) type.Items.Add(n);
            type.SelectedIndex = 0;
            flow.Controls.Add(Theme.Label("Görev", Theme.H2));
            flow.Controls.Add(Field("Hangi servis", service));
            flow.Controls.Add(Field("Ne yapılsın", type));
            flow.Controls.Add(Field("Saat (SS:dd)", time));
            flow.Controls.Add(Field("Kaç dakikada bir", interval));
            flow.Controls.Add(Field("Komut", command));
            warn.Margin = new Padding(0, 10, 0, 0);
            enabled.Margin = new Padding(0, 6, 0, 0);
            enabled.Checked = true;
            warn.Checked = true;
            flow.Controls.Add(warn);
            flow.Controls.Add(enabled);
            var buttons = new FlowLayoutPanel { AutoSize = true, BackColor = Theme.Card, Margin = new Padding(0, 16, 0, 0) };
            var add = DarkButton.Sized("+ Yeni", 90);
            var save = DarkButton.Sized("Kaydet", 100, Theme.Accent);
            var del = DarkButton.Sized("Sil", 80, Theme.Red);
            buttons.Controls.AddRange(new Control[] { add, save, del });
            flow.Controls.Add(buttons);
            flow.Controls.Add(new Label
            {
                Text = "İpuçları:\n• \"Tüm Minecraft sunucuları\" seçilirse görev hepsine uygulanır.\n• Yeniden başlatmadan önce dünya kaydedilir (save-all).\n• Örnek komutlar: save-all, say Sunucu yedekleniyor!, whitelist reload",
                ForeColor = Theme.Muted, Font = Theme.Small, AutoSize = true, MaximumSize = new Size(380, 0), Margin = new Padding(0, 18, 0, 0),
            });

            var gap = new Panel { Dock = DockStyle.Right, Width = 14, BackColor = Theme.Bg };
            Controls.Add(listCard);
            Controls.Add(gap);
            Controls.Add(form);
            Controls.Add(Header("Zamanlayıcı", "Otomatik yeniden başlatma ve düzenli komutlar (ör. her gece 05:00 yeniden başlat, 15 dakikada bir kaydet)."));

            type.SelectedIndexChanged += (s, e) => UpdateFields();
            list.SelectedIndexChanged += (s, e) => { if (list.SelectedItems.Count > 0) Edit((ScheduleConfig)list.SelectedItems[0].Tag); };
            add.Click += (s, e) => Edit(null);
            save.Click += (s, e) => Save();
            del.Click += (s, e) => Delete();
            ServicesChanged();
            Edit(null);
        }

        static Control Field(string label, Control input)
        {
            var p = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, BackColor = Theme.Card, Margin = new Padding(0, 10, 0, 0), WrapContents = false };
            p.Controls.Add(Theme.Label(label, Theme.Small, Theme.Muted));
            p.Controls.Add(input);
            return p;
        }

        void UpdateFields()
        {
            string t = Types[Math.Max(0, type.SelectedIndex)];
            time.Parent.Visible = t != "interval";
            interval.Parent.Visible = t == "interval";
            command.Parent.Visible = t != "restart" && t != "backup";
            warn.Visible = t == "restart";
        }

        public override void ServicesChanged()
        {
            service.Items.Clear();
            service.Items.Add(AllServers);
            foreach (var r in Manager.Runners) service.Items.Add(r);
            Fill();
        }

        void Fill()
        {
            list.BeginUpdate();
            list.Items.Clear();
            foreach (var s in Manager.Config.Schedules)
            {
                var item = new ListViewItem(s.Enabled ? "● Etkin" : "○ Kapalı") { Tag = s, ForeColor = s.Enabled ? Theme.Green : Theme.Muted };
                item.SubItems.Add(s.Describe(Manager.NameOf));
                list.Items.Add(item);
            }
            list.EndUpdate();
        }

        void Edit(ScheduleConfig s)
        {
            editing = s;
            var src = s ?? new ScheduleConfig();
            object target = Manager.Find(src.ServiceId);
            service.SelectedItem = target ?? AllServers;
            if (service.SelectedIndex < 0) service.SelectedIndex = 0;
            type.SelectedIndex = Math.Max(0, Array.IndexOf(Types, src.Type));
            time.Text = src.Time;
            interval.Value = Math.Max(1, Math.Min(1440, src.IntervalMin));
            command.Text = src.Command;
            warn.Checked = src.Warn;
            enabled.Checked = src.Enabled;
            UpdateFields();
        }

        void Save()
        {
            string t = Types[type.SelectedIndex];
            if (t != "interval" && !TimeSpan.TryParse(time.Text.Trim(), out var ts) || t != "interval" && time.Text.Trim().Length != 5)
            {
                MessageBox.Show(Main, "Saati SS:dd biçiminde yaz (ör. 05:00).", "Zamanlayıcı");
                return;
            }
            if (t != "restart" && t != "backup" && command.Text.Trim().Length == 0) { MessageBox.Show(Main, "Komut yaz.", "Zamanlayıcı"); return; }
            var s = editing ?? new ScheduleConfig();
            s.ServiceId = service.SelectedItem is ServiceRunner r ? r.Config.Id : "";
            s.Type = t;
            s.Time = time.Text.Trim();
            s.IntervalMin = (int)interval.Value;
            s.Command = command.Text.Trim().TrimStart('/');
            s.Warn = warn.Checked;
            s.Enabled = enabled.Checked;
            if (editing == null) Manager.Config.Schedules.Add(s);
            Manager.Config.Save();
            Manager.AddEvent("Zamanlayıcı", "kaydedildi: " + s.Describe(Manager.NameOf));
            editing = s;
            Fill();
        }

        void Delete()
        {
            if (editing == null) return;
            Manager.Config.Schedules.Remove(editing);
            Manager.Config.Save();
            Edit(null);
            Fill();
        }
    }

    public class EventsPage : Page
    {
        public override string Title => "Olay Kaydı";
        readonly ListView list = Theme.List();
        readonly CheckBox onlyImportant = Theme.Check("Sadece önemli (çökmeler)");
        readonly TextBox search = Theme.TextBox();
        int shown = -1;

        public EventsPage(ServiceManager m, MainForm f) : base(m, f)
        {
            list.Columns.Add("Zaman", 150);
            list.Columns.Add("Servis", 180);
            list.Columns.Add("Olay", 700);
            list.Dock = DockStyle.Fill;
            var card = new Card { Dock = DockStyle.Fill, Padding = new Padding(10) };
            card.Controls.Add(list);

            var top = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 44, BackColor = Theme.Bg, WrapContents = false };
            var cue = Theme.Label("Ara:", Theme.Body, Theme.Muted);
            cue.Margin = new Padding(0, 10, 4, 0);
            search.Width = 220;
            search.Margin = new Padding(0, 7, 12, 0);
            onlyImportant.Margin = new Padding(0, 9, 12, 0);
            var clear = DarkButton.Sized("Listeyi temizle", 130);
            var open = DarkButton.Sized("Log dosyasını aç", 140);
            top.Controls.AddRange(new Control[] { cue, search, onlyImportant, clear, open });

            Controls.Add(card);
            Controls.Add(top);
            Controls.Add(Header("Olay Kaydı", "Panelin yaptığı her şey: başlatma, durdurma, çökme, zamanlanmış görevler. Dosya: lumpanel-olaylar.log"));

            search.TextChanged += (s, e) => { shown = -1; RefreshView(); };
            onlyImportant.CheckedChanged += (s, e) => { shown = -1; RefreshView(); };
            clear.Click += (s, e) => { Manager.ClearEvents(); shown = -1; RefreshView(); };
            open.Click += (s, e) =>
            {
                string path = Path.Combine(AppConfig.Folder, "lumpanel-olaylar.log");
                if (File.Exists(path)) Process.Start("notepad.exe", $"\"{path}\"");
            };
        }

        public override void RefreshView()
        {
            var all = Manager.Events;
            if (all.Count == shown) return;
            shown = all.Count;
            string q = search.Text.Trim();
            var rows = all.AsEnumerable().Reverse()
                .Where(e => !onlyImportant.Checked || e.Important)
                .Where(e => q.Length == 0 || e.Message.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0 || e.Service.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0)
                .Take(1000);
            list.BeginUpdate();
            list.Items.Clear();
            foreach (var e in rows)
            {
                var item = new ListViewItem(e.Time.ToString("dd.MM HH:mm:ss")) { ForeColor = e.Important ? Theme.Red : Theme.Text };
                item.SubItems.Add(e.Service);
                item.SubItems.Add(e.Message);
                list.Items.Add(item);
            }
            list.EndUpdate();
        }
    }
}
