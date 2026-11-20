using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace LumPanel
{
    public class ServerConfigPage : Page
    {
        public override string Title => "Sunucu Ayarları";

        readonly ComboBox server = Theme.Combo();
        readonly Label note = Theme.Label("", Theme.Small, Theme.Yellow);
        readonly Panel host = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Bg, Padding = new Padding(0, 10, 0, 0) };
        readonly List<(DarkButton tab, Control view)> tabs = new List<(DarkButton, Control)>();

        // general
        readonly DataGridView grid = new DataGridView();
        // optimization
        readonly ComboBox optType = Theme.Combo(), optPriority = Theme.Combo();
        readonly NumericUpDown optPlayers = Theme.Number(1, 1000, 90, 30);
        readonly DataGridView tweakGrid = new DataGridView();
        // startup
        readonly NumericUpDown xms = Theme.Number(1, 64, 90, 2), xmx = Theme.Number(1, 64, 90, 4);
        readonly CheckBox aikar = Theme.Check("Aikar JVM bayrakları (önerilen, takılmaları azaltır)");
        readonly ComboBox jar = Theme.Combo();
        readonly TextBox java = Theme.TextBox("java");
        readonly TextBox cmdPreview = Theme.TextBox();
        readonly Label ramPlan = Theme.Label("", Theme.Body, Theme.Muted);
        // plugins
        readonly ListView plugins = Theme.List();
        // files
        readonly ComboBox fileList = Theme.Combo();
        readonly RichTextBox editor = new RichTextBox
        {
            Dock = DockStyle.Fill, BackColor = Theme.Input, ForeColor = Theme.Text, BorderStyle = BorderStyle.None, Font = Theme.Mono,
            WordWrap = false, AcceptsTab = false, DetectUrls = false,
        };
        string editorFile;
        bool editorDirty;

        ServiceRunner Current => server.SelectedItem as ServiceRunner;
        ServerFiles Files => Current == null || !Directory.Exists(Current.Config.WorkingDir) ? null : new ServerFiles(Current.Config.WorkingDir);

        public ServerConfigPage(ServiceManager m, MainForm f) : base(m, f)
        {
            server.Width = 230;
            var top = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 46, BackColor = Theme.Bg, WrapContents = false };
            var lbl = Theme.Label("Sunucu:", Theme.Bold);
            lbl.Margin = new Padding(0, 11, 6, 0);
            server.Margin = new Padding(0, 6, 14, 0);
            note.Margin = new Padding(0, 12, 0, 0);
            top.Controls.AddRange(new Control[] { lbl, server, note });

            var tabRow = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 44, BackColor = Theme.Bg, WrapContents = false };
            AddTab(tabRow, "Genel (server.properties)", BuildGeneral());
            AddTab(tabRow, "⚡ Optimizasyon", BuildOptimize());
            AddTab(tabRow, "Başlatma ve RAM", BuildStartup());
            AddTab(tabRow, "Eklentiler", BuildPlugins());
            AddTab(tabRow, "Dosya düzenleyici", BuildFiles());

            Controls.Add(host);
            Controls.Add(tabRow);
            Controls.Add(top);
            Controls.Add(Header("Sunucu Ayarları", "Her sunucunun ayarları, optimizasyonu, RAM'i ve eklentileri. Her kayıttan önce yedek alınır (.lumpanel-yedek klasörü)."));

            server.SelectedIndexChanged += (s, e) => LoadServer();
            ShowTab(tabs[0].view);
            ServicesChanged();
        }

        void AddTab(FlowLayoutPanel row, string text, Control view)
        {
            var b = DarkButton.Sized(text, TextRenderer.MeasureText(text, Theme.Bold).Width + 34);
            b.Margin = new Padding(0, 0, 8, 0);
            b.Click += (s, e) => ShowTab(view);
            view.Dock = DockStyle.Fill;
            view.Visible = false;
            host.Controls.Add(view);
            tabs.Add((b, view));
            row.Controls.Add(b);
        }

        void ShowTab(Control view)
        {
            if (editorDirty && view != tabs[4].view && tabs[4].view.Visible && !ConfirmDiscard()) return;
            foreach (var (tab, v) in tabs)
            {
                v.Visible = v == view;
                tab.SetColor(v == view ? Theme.Accent : Theme.CardHi);
            }
        }

        bool ConfirmDiscard() =>
            MessageBox.Show(Main, "Dosya düzenleyicide kaydedilmemiş değişiklik var. Vazgeçilsin mi?", "Sunucu Ayarları", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes;

        static Card Box(Control content, int pad = 14) { var c = new Card { Padding = new Padding(pad), FillTo = Color.FromArgb(24, 22, 33) }; content.Dock = DockStyle.Fill; c.Controls.Add(content); return c; }

        static FlowLayoutPanel Bar(params Control[] items)
        {
            var bar = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 46, BackColor = Theme.Card, Padding = new Padding(0, 8, 0, 0), WrapContents = false };
            foreach (var c in items) { c.Margin = new Padding(0, 0, 8, 0); bar.Controls.Add(c); }
            return bar;
        }

        // ================================================================== general (server.properties)
        Control BuildGeneral()
        {
            StyleGrid(grid);
            grid.SelectionMode = DataGridViewSelectionMode.CellSelect;
            grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Ayar", ReadOnly = true, FillWeight = 30 });
            grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Değer", FillWeight = 30 });
            grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Açıklama", ReadOnly = true, FillWeight = 40, DefaultCellStyle = new DataGridViewCellStyle { ForeColor = Theme.Muted } });
            var save = DarkButton.Sized("💾 Kaydet", 120, Theme.Accent);
            var reload = DarkButton.Sized("Yeniden oku", 110);
            save.Click += (s, e) => SaveProperties();
            reload.Click += (s, e) => LoadProperties();
            var card = Box(grid);
            card.Controls.Add(Bar(save, reload, Theme.Label("Değişiklikler sunucu yeniden başlayınca geçerli olur.", Theme.Small, Theme.Muted)));
            return card;
        }

        void LoadProperties()
        {
            grid.Rows.Clear();
            var files = Files;
            if (files == null || !files.Exists("server.properties")) { note.Text = Current == null ? "" : "Bu sunucuda server.properties yok."; return; }
            foreach (var kv in ServerFiles.ParseProperties(files.Read("server.properties")).OrderBy(k => Presets.PropertyHelp.ContainsKey(k.Key) ? 0 : 1).ThenBy(k => k.Key))
                grid.Rows.Add(kv.Key, kv.Value, Presets.PropertyHelp.TryGetValue(kv.Key, out var h) ? h : "");
        }

        void SaveProperties()
        {
            var files = Files;
            if (files == null || !files.Exists("server.properties")) return;
            grid.EndEdit();
            var values = new Dictionary<string, string>();
            foreach (DataGridViewRow row in grid.Rows) values[(string)row.Cells[0].Value] = Convert.ToString(row.Cells[1].Value) ?? "";
            int port;
            if (values.TryGetValue("server-port", out var p) && (!int.TryParse(p, out port) || port < 1 || port > 65535)) { MessageBox.Show(Main, "server-port 1-65535 arasında olmalı.", "Kaydet"); return; }
            files.Write("server.properties", ServerFiles.SetProperties(files.Read("server.properties"), values));
            AfterSave("server.properties kaydedildi.");
        }

        void AfterSave(string what)
        {
            Manager.AddEvent(Current.Config.Name, what);
            if (Current.IsAlive && MessageBox.Show(Main, what + "\n\nSunucu açık; değişikliklerin geçerli olması için yeniden başlatılsın mı?", "Sunucu Ayarları",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
                Current.Stop(restart: true);
            else if (!Current.IsAlive) MessageBox.Show(Main, what, "Sunucu Ayarları", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        // ================================================================== optimization presets
        Control BuildOptimize()
        {
            optType.Width = 220;
            optPriority.Width = 200;
            foreach (var t in Optimizer.Types) optType.Items.Add(t);
            foreach (var t in Optimizer.Priorities) optPriority.Items.Add(t);
            optType.SelectedIndex = 0;
            optPriority.SelectedIndex = 1;
            StyleGrid(tweakGrid);
            tweakGrid.Columns.Add(new DataGridViewCheckBoxColumn { HeaderText = "✓", FillWeight = 6 });
            tweakGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Dosya", ReadOnly = true, FillWeight = 22 });
            tweakGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Ayar", ReadOnly = true, FillWeight = 34 });
            tweakGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Şimdi", ReadOnly = true, FillWeight = 12 });
            tweakGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Yeni (düzenlenebilir)", FillWeight = 14 });
            tweakGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Neden", ReadOnly = true, FillWeight = 32, DefaultCellStyle = new DataGridViewCellStyle { ForeColor = Theme.Muted } });
            var calc = DarkButton.Sized("Ayarları hesapla", 150, Theme.Accent);
            var apply = DarkButton.Sized("⚡ Seçilenleri uygula", 170, Theme.Green);
            var all = DarkButton.Sized("Hepsini seç", 100);
            var none = DarkButton.Sized("Hiçbirini", 90);
            calc.Click += (s, e) => Preview();
            apply.Click += (s, e) => ApplyPreset();
            all.Click += (s, e) => { foreach (DataGridViewRow r in tweakGrid.Rows) r.Cells[0].Value = !((string)r.Cells[3].Value).StartsWith("("); };
            none.Click += (s, e) => { foreach (DataGridViewRow r in tweakGrid.Rows) r.Cells[0].Value = false; };
            var head = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 44, BackColor = Theme.Card, WrapContents = false };
            Control L(string t) { var l = Theme.Label(t, Theme.Bold); l.Margin = new Padding(0, 10, 6, 0); return l; }
            foreach (var c in new Control[] { optPlayers, optType, optPriority }) c.Margin = new Padding(0, 6, 16, 0);
            calc.Margin = new Padding(0, 4, 0, 0);
            head.Controls.AddRange(new Control[] { L("Oyuncu:"), optPlayers, L("Tür:"), optType, L("Öncelik:"), optPriority, calc });
            var card = Box(tweakGrid);
            card.Controls.Add(Bar(apply, all, none, Theme.Label("Yeşil: zaten öyle · Sarı: değişecek · Gri: dosyada yok (atlanır). 'Yeni' sütununu elle değiştirebilirsin.", Theme.Small, Theme.Muted)));
            card.Controls.Add(head);
            return card;
        }

        static void StyleGrid(DataGridView g)
        {
            g.BackgroundColor = Theme.Input;
            g.BorderStyle = BorderStyle.None;
            g.GridColor = Theme.Card;
            g.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.None;
            g.CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal;
            g.EnableHeadersVisualStyles = false;
            g.ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle { BackColor = Theme.Card, ForeColor = Theme.Muted, Font = Theme.Bold, SelectionBackColor = Theme.Card };
            g.DefaultCellStyle = new DataGridViewCellStyle { BackColor = Theme.Input, ForeColor = Theme.Text, SelectionBackColor = Color.FromArgb(74, 52, 120), SelectionForeColor = Color.White, Font = Theme.Body };
            g.RowHeadersVisible = false;
            g.AllowUserToAddRows = g.AllowUserToDeleteRows = g.AllowUserToResizeRows = false;
            g.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
            g.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
            g.ColumnHeadersHeight = 32;
            g.RowTemplate.Height = 28;
            Theme.Dark(g);
        }

        void Preview()
        {
            tweakGrid.Rows.Clear();
            var files = Files;
            if (files == null || Current?.Config.Kind != ServiceKinds.Minecraft) return;
            var cache = new Dictionary<string, string>();
            foreach (var t in Optimizer.Generate((int)optPlayers.Value, optType.SelectedIndex, optPriority.SelectedIndex, Environment.ProcessorCount))
            {
                if (!cache.TryGetValue(t.File, out var text)) cache[t.File] = text = files.Exists(t.File) ? files.Read(t.File) : null;
                string now = text == null ? "(dosya yok)"
                    : t.IsProperties ? ServerFiles.ParseProperties(text).Where(k => k.Key == t.Key).Select(k => k.Value).FirstOrDefault() ?? "(yok)"
                    : ServerFiles.GetYaml(text, t.Key) ?? "(yok)";
                bool missing = now.StartsWith("("), same = string.Equals(now.Trim('"', '\''), t.Value, StringComparison.OrdinalIgnoreCase);
                int i = tweakGrid.Rows.Add(!missing && !same, t.File, t.Key, now, t.Value, t.Why);
                tweakGrid.Rows[i].DefaultCellStyle.ForeColor = missing ? Theme.Muted : same ? Theme.Green : Theme.Yellow;
                tweakGrid.Rows[i].Cells[0].ReadOnly = missing;
            }
        }

        void ApplyPreset()
        {
            var files = Files;
            if (files == null) return;
            tweakGrid.EndEdit();
            var chosen = tweakGrid.Rows.Cast<DataGridViewRow>().Where(r => r.Cells[0].Value is bool b && b)
                .Select(r => new Tweak { File = (string)r.Cells[1].Value, Key = (string)r.Cells[2].Value, Value = (Convert.ToString(r.Cells[4].Value) ?? "").Trim() })
                .Where(t => t.Value.Length > 0 && !t.Value.Contains("\n")).ToList();
            if (chosen.Count == 0) { MessageBox.Show(Main, "Uygulanacak satır seçilmedi.", "Optimizasyon"); return; }
            if (MessageBox.Show(Main, $"{chosen.Count} ayar uygulansın mı? Değişen dosyalar önce yedeklenir.", "Optimizasyon", MessageBoxButtons.YesNo) != DialogResult.Yes) return;
            int changed = 0, skipped = 0;
            foreach (var group in chosen.GroupBy(t => t.File))
            {
                if (!files.Exists(group.Key)) { skipped += group.Count(); continue; }
                string text = files.Read(group.Key), before = text;
                if (group.Key == "server.properties")
                {
                    var present = new HashSet<string>(ServerFiles.ParseProperties(text).Select(k => k.Key));
                    text = ServerFiles.SetProperties(text, group.Where(t => present.Contains(t.Key)).ToDictionary(t => t.Key, t => t.Value));
                    changed += group.Count(t => present.Contains(t.Key));
                    skipped += group.Count(t => !present.Contains(t.Key));
                }
                else foreach (var t in group) { if (ServerFiles.SetYaml(ref text, t.Key, t.Value)) changed++; else skipped++; }
                if (text != before) files.Write(group.Key, text.Replace("\r\n", "\n").Replace("\n", "\r\n"));
            }
            Preview();
            LoadProperties();
            AfterSave($"Optimizasyon ({optPlayers.Value} oyuncu, {optType.SelectedItem}, {optPriority.SelectedItem}): {changed} ayar uygulandı, {skipped} atlandı.");
        }

        // ================================================================== startup / RAM / JVM
        Control BuildStartup()
        {
            jar.Width = 260;
            java.Width = 420;
            cmdPreview.Width = 760;
            cmdPreview.Height = 90;
            cmdPreview.Multiline = true;
            cmdPreview.ReadOnly = true;
            cmdPreview.Font = Theme.Mono;
            var flow = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true, BackColor = Theme.Card };
            Theme.Dark(flow);
            Control Field(string label, Control c) { var p = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, BackColor = Theme.Card, Margin = new Padding(0, 8, 18, 0) }; p.Controls.Add(Theme.Label(label, Theme.Small, Theme.Muted)); p.Controls.Add(c); return p; }
            var row = new FlowLayoutPanel { AutoSize = true, BackColor = Theme.Card, WrapContents = false };
            row.Controls.AddRange(new[] { Field("Başlangıç RAM (Xms, GB)", xms), Field("En fazla RAM (Xmx, GB)", xmx), Field("Sunucu jar dosyası", jar) });
            flow.Controls.Add(Theme.Label("Bellek ve JVM", Theme.H2));
            flow.Controls.Add(row);
            flow.Controls.Add(Field("Java (java ya da java.exe yolu)", java));
            aikar.Margin = new Padding(0, 12, 0, 0);
            flow.Controls.Add(aikar);
            flow.Controls.Add(Field("Oluşacak komut", cmdPreview));
            ramPlan.MaximumSize = new Size(760, 0);
            ramPlan.Margin = new Padding(0, 12, 0, 0);
            flow.Controls.Add(ramPlan);
            var apply = DarkButton.Sized("💾 Başlatma ayarını uygula", 220, Theme.Accent);
            apply.Margin = new Padding(0, 14, 0, 0);
            flow.Controls.Add(apply);
            flow.Controls.Add(new Label
            {
                Text = "Uygulanınca bu servis .bat yerine Java'yı doğrudan çalıştırır (pencere ve 'pause' olmaz). Xms ve Xmx'i eşit tutmak takılmaları azaltır. Bütün sunucuların Xmx toplamı, VDS belleğinden en az 3-4 GB az olmalı.",
                AutoSize = true, MaximumSize = new Size(760, 0), ForeColor = Theme.Muted, Font = Theme.Small, Margin = new Padding(0, 10, 0, 0),
            });
            foreach (Control c in new Control[] { xms, xmx }) ((NumericUpDown)c).ValueChanged += (s, e) => UpdateCmd();
            aikar.CheckedChanged += (s, e) => UpdateCmd();
            jar.SelectedIndexChanged += (s, e) => UpdateCmd();
            java.TextChanged += (s, e) => UpdateCmd();
            apply.Click += (s, e) => ApplyStartup();
            return Box(flow, 20);
        }

        static readonly Regex XmsRe = new Regex(@"-Xms(\d+)([GgMm])"), XmxRe = new Regex(@"-Xmx(\d+)([GgMm])");
        static int Gb(Match m) => !m.Success ? 0 : m.Groups[2].Value.ToUpperInvariant() == "G" ? int.Parse(m.Groups[1].Value) : Math.Max(1, int.Parse(m.Groups[1].Value) / 1024);

        void LoadStartup()
        {
            var c = Current?.Config;
            jar.Items.Clear();
            if (c == null) return;
            var files = Files;
            if (files != null) foreach (var j in Directory.GetFiles(files.Root, "*.jar").Select(Path.GetFileName)) jar.Items.Add(j);
            string args = c.Arguments ?? "";
            if (c.Program.EndsWith(".bat", StringComparison.OrdinalIgnoreCase) && files != null)
                try { args = File.ReadAllLines(c.Program).FirstOrDefault(l => l.IndexOf("-jar", StringComparison.OrdinalIgnoreCase) >= 0) ?? args; } catch { }
            xms.Value = Math.Max(1, Math.Min(64, Gb(XmsRe.Match(args)) == 0 ? 2 : Gb(XmsRe.Match(args))));
            xmx.Value = Math.Max(1, Math.Min(64, Gb(XmxRe.Match(args)) == 0 ? 4 : Gb(XmxRe.Match(args))));
            aikar.Checked = args.Contains("UseG1GC");
            var jm = Regex.Match(args, @"-jar\s+""?([^""\s]+\.jar)");
            string current = jm.Success ? Path.GetFileName(jm.Groups[1].Value) : null;
            if (current != null && jar.Items.Contains(current)) jar.SelectedItem = current; else if (jar.Items.Count > 0) jar.SelectedIndex = 0;
            java.Text = c.Program.EndsWith(".bat", StringComparison.OrdinalIgnoreCase) || c.Program.Length == 0 ? "java" : c.Program;
            UpdateCmd();
        }

        string BuildArgs()
        {
            bool proxy = Current?.Config.Kind == ServiceKinds.Proxy;
            int max = (int)xmx.Value, min = (int)Math.Min(xms.Value, xmx.Value);
            string flags = aikar.Checked ? " " + (proxy ? Presets.VelocityFlags : Presets.AikarFlags(max)) : "";
            return $"-Xms{min}G -Xmx{max}G{flags} -jar {jar.SelectedItem}" + (proxy ? "" : " nogui");
        }

        void UpdateCmd()
        {
            if (Current == null) return;
            cmdPreview.Text = $"{java.Text} {BuildArgs()}";
            long totalGb = Manager.Runners.Where(r => r != Current && (r.Config.Kind == ServiceKinds.Minecraft || r.Config.Kind == ServiceKinds.Proxy))
                .Sum(r => (long)Gb(XmxRe.Match(r.Config.Arguments ?? ""))) + (long)xmx.Value;
            double sys = Manager.Sys.MemTotalGb;
            bool tight = sys > 0 && totalGb > sys - 3;
            ramPlan.Text = $"RAM planı: bütün Java sunucularının Xmx toplamı ≈ {totalGb} GB, VDS belleği {sys:0} GB." +
                           (tight ? "  ⚠ Çok sıkı! Windows ve diğer programlara en az 3-4 GB bırak, yoksa donma olur." : "  ✔ Yeterli boşluk var.");
            ramPlan.ForeColor = tight ? Theme.Red : Theme.Green;
        }

        void ApplyStartup()
        {
            var r = Current;
            if (r == null || jar.SelectedItem == null) { MessageBox.Show(Main, "Sunucu klasöründe jar dosyası bulunamadı.", "Başlatma"); return; }
            if (xms.Value > xmx.Value) { MessageBox.Show(Main, "Xms, Xmx'ten büyük olamaz.", "Başlatma"); return; }
            var cfg = CloneConfig();
            var svc = cfg.Services.First(s => s.Id == r.Config.Id);
            svc.Program = java.Text.Trim().Length == 0 ? "java" : java.Text.Trim();
            svc.Arguments = BuildArgs();
            svc.MatchText = cfg.Services.Count(s => s.Arguments?.Contains(jar.SelectedItem.ToString()) == true) > 1 ? "" : jar.SelectedItem.ToString();
            Manager.ApplyConfig(cfg);
            Main.ServicesChanged();
            AfterSave("Başlatma ayarı güncellendi: " + svc.Arguments.Split(' ').FirstOrDefault(a => a.StartsWith("-Xmx")));
        }

        AppConfig CloneConfig()
        {
            var json = new System.Web.Script.Serialization.JavaScriptSerializer();
            return json.Deserialize<AppConfig>(json.Serialize(Manager.Config));
        }

        // ================================================================== plugins
        Control BuildPlugins()
        {
            plugins.Columns.Add("Eklenti", 300);
            plugins.Columns.Add("Durum", 100);
            plugins.Columns.Add("Boyut", 90);
            plugins.Columns.Add("Tarih", 200);
            plugins.MultiSelect = true;
            var toggle = DarkButton.Sized("Aç / Kapat", 110, Theme.Accent);
            var add = DarkButton.Sized("+ Eklenti ekle", 130);
            var del = DarkButton.Sized("Sil", 70, Theme.Red);
            var open = DarkButton.Sized("Klasörü aç", 110);
            var refresh = DarkButton.Sized("Yenile", 80);
            toggle.Click += (s, e) => TogglePlugins();
            add.Click += (s, e) => AddPlugins();
            del.Click += (s, e) => DeletePlugins();
            open.Click += (s, e) => { var d = PluginDir(); if (d != null) Process.Start("explorer.exe", $"\"{d}\""); };
            refresh.Click += (s, e) => LoadPlugins();
            var card = Box(plugins);
            card.Controls.Add(Bar(toggle, add, del, open, refresh, Theme.Label("Kapatılan eklenti .jar.disabled olur; silinen .lumpanel-silinen klasörüne taşınır.", Theme.Small, Theme.Muted)));
            return card;
        }

        string PluginDir() { var f = Files; if (f == null) return null; string d = f.Resolve("plugins"); return Directory.Exists(d) ? d : null; }

        void LoadPlugins()
        {
            plugins.BeginUpdate();
            plugins.Items.Clear();
            string dir = PluginDir();
            if (dir != null)
                foreach (var fi in new DirectoryInfo(dir).GetFiles().Where(x => x.Name.EndsWith(".jar", StringComparison.OrdinalIgnoreCase) || x.Name.EndsWith(".jar.disabled", StringComparison.OrdinalIgnoreCase)).OrderBy(x => x.Name))
                {
                    bool on = fi.Name.EndsWith(".jar", StringComparison.OrdinalIgnoreCase);
                    var it = new ListViewItem(on ? fi.Name : fi.Name.Substring(0, fi.Name.Length - ".disabled".Length)) { Tag = fi.FullName, ForeColor = on ? Theme.Text : Theme.Muted };
                    it.SubItems.Add(on ? "● Açık" : "○ Kapalı");
                    it.SubItems.Add(Theme.Bytes(fi.Length));
                    it.SubItems.Add(fi.LastWriteTime.ToString("dd.MM.yyyy HH:mm"));
                    plugins.Items.Add(it);
                }
            plugins.EndUpdate();
        }

        IEnumerable<string> SelectedPlugins() => plugins.SelectedItems.Cast<ListViewItem>().Select(i => (string)i.Tag).ToList();

        void TogglePlugins()
        {
            foreach (var path in SelectedPlugins())
            {
                try
                {
                    string target = path.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase) ? path.Substring(0, path.Length - ".disabled".Length) : path + ".disabled";
                    if (File.Exists(target)) { MessageBox.Show(Main, Path.GetFileName(target) + " zaten var.", "Eklentiler"); continue; }
                    File.Move(path, target);
                }
                catch (IOException ex) { MessageBox.Show(Main, "Dosya kullanımda olabilir; sunucuyu durdurup tekrar dene.\n" + ex.Message, "Eklentiler"); }
            }
            LoadPlugins();
            AfterSave("Eklenti durumu değişti.");
        }

        void AddPlugins()
        {
            string dir = PluginDir();
            if (dir == null) return;
            using var dlg = new OpenFileDialog { Filter = "Eklenti (*.jar)|*.jar", Multiselect = true };
            if (dlg.ShowDialog(Main) != DialogResult.OK) return;
            foreach (var src in dlg.FileNames)
            {
                string name = Path.GetFileName(src);
                if (!name.EndsWith(".jar", StringComparison.OrdinalIgnoreCase)) continue;
                string dst = Path.Combine(dir, name);
                if (File.Exists(dst) && MessageBox.Show(Main, name + " zaten var; üzerine yazılsın mı?", "Eklentiler", MessageBoxButtons.YesNo) != DialogResult.Yes) continue;
                File.Copy(src, dst, true);
            }
            LoadPlugins();
            AfterSave("Eklenti eklendi.");
        }

        void DeletePlugins()
        {
            var sel = SelectedPlugins().ToList();
            if (sel.Count == 0 || MessageBox.Show(Main, $"{sel.Count} eklenti kaldırılsın mı? (.lumpanel-silinen klasörüne taşınır, geri alınabilir)", "Eklentiler", MessageBoxButtons.YesNo) != DialogResult.Yes) return;
            string bin = Path.Combine(PluginDir(), ".lumpanel-silinen");
            Directory.CreateDirectory(bin);
            foreach (var p in sel)
            {
                try { File.Move(p, Path.Combine(bin, $"{DateTime.Now:yyyyMMdd-HHmmss}-{Path.GetFileName(p)}")); }
                catch (IOException ex) { MessageBox.Show(Main, "Taşınamadı (sunucu açıksa durdur): " + ex.Message, "Eklentiler"); }
            }
            LoadPlugins();
            AfterSave("Eklenti kaldırıldı.");
        }

        // ================================================================== file editor
        Control BuildFiles()
        {
            fileList.Width = 460;
            var save = DarkButton.Sized("💾 Kaydet", 110, Theme.Accent);
            var backups = DarkButton.Sized("Yedekleri aç", 120);
            var head = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 40, BackColor = Theme.Card, WrapContents = false };
            var l = Theme.Label("Dosya:", Theme.Bold);
            l.Margin = new Padding(0, 9, 8, 0);
            head.Controls.AddRange(new Control[] { l, fileList });
            fileList.SelectedIndexChanged += (s, e) => OpenFile(fileList.SelectedItem as string);
            editor.TextChanged += (s, e) => editorDirty = editorFile != null;
            save.Click += (s, e) => SaveFile();
            backups.Click += (s, e) =>
            {
                var f = Files;
                if (f == null) return;
                string d = f.Resolve(".lumpanel-yedek");
                Directory.CreateDirectory(d);
                Process.Start("explorer.exe", $"\"{d}\"");
            };
            Theme.Dark(editor);
            var card = Box(editor);
            card.Controls.Add(Bar(save, backups, Theme.Label("YAML dosyalarında TAB kullanılamaz; girintiler boşlukla olmalı.", Theme.Small, Theme.Muted)));
            card.Controls.Add(head);
            return card;
        }

        void LoadFileList()
        {
            fileList.Items.Clear();
            var files = Files;
            if (files == null) return;
            var exts = new[] { ".yml", ".yaml", ".toml", ".properties", ".json", ".txt", ".conf" };
            IEnumerable<string> Scan(string rel, int depth)
            {
                string dir = files.Resolve(rel);
                if (!Directory.Exists(dir)) yield break;
                foreach (var f in Directory.GetFiles(dir).Where(x => exts.Contains(Path.GetExtension(x).ToLowerInvariant())).OrderBy(x => x))
                {
                    var fi = new FileInfo(f);
                    if (fi.Length < 2_000_000) yield return f.Substring(files.Root.Length);
                }
                if (depth <= 0) yield break;
                foreach (var d in Directory.GetDirectories(dir).OrderBy(x => x))
                {
                    string n = Path.GetFileName(d);
                    if (n.StartsWith(".") || n == "logs" || n == "cache" || n == "libraries" || n == "versions" || File.Exists(Path.Combine(d, "level.dat"))) continue;
                    foreach (var x in Scan(d.Substring(files.Root.Length), depth - 1)) yield return x;
                }
            }
            foreach (var f in Scan("", 2).Take(400)) fileList.Items.Add(f);
        }

        void OpenFile(string rel)
        {
            if (rel == null || rel == editorFile) return;
            if (editorDirty && !ConfirmDiscard()) { fileList.SelectedItem = editorFile; return; }
            try
            {
                editorFile = null;
                editor.Text = Files.Read(rel);
                editorFile = rel;
                editorDirty = false;
            }
            catch (Exception ex) { MessageBox.Show(Main, ex.Message, "Dosya"); }
        }

        void SaveFile()
        {
            if (editorFile == null) return;
            string text = editor.Text.Replace("\r\n", "\n").Replace("\n", "\r\n");
            bool yaml = editorFile.EndsWith(".yml", StringComparison.OrdinalIgnoreCase) || editorFile.EndsWith(".yaml", StringComparison.OrdinalIgnoreCase);
            if (yaml && Regex.IsMatch(text, @"^\t", RegexOptions.Multiline)) { MessageBox.Show(Main, "YAML dosyasında satır başında TAB var; boşlukla değiştir.", "Kaydet"); return; }
            if (editorFile.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                try { new System.Web.Script.Serialization.JavaScriptSerializer().DeserializeObject(text); }
                catch (Exception ex) { MessageBox.Show(Main, "JSON geçersiz: " + ex.Message, "Kaydet"); return; }
            Files.Write(editorFile, text);
            editorDirty = false;
            AfterSave(editorFile + " kaydedildi.");
        }

        // ================================================================== common
        void LoadServer()
        {
            if (editorDirty && !ConfirmDiscard()) return;
            editorDirty = false;
            editorFile = null;
            editor.Clear();
            var r = Current;
            note.Text = r == null ? "" : !Directory.Exists(r.Config.WorkingDir) ? "Sunucu klasörü bulunamadı." :
                r.IsAlive ? "Sunucu açık: değişiklikler yeniden başlatınca geçerli olur." : "";
            LoadProperties();
            Preview();
            LoadStartup();
            LoadPlugins();
            LoadFileList();
        }

        public override void ServicesChanged()
        {
            var keep = Current;
            server.Items.Clear();
            foreach (var r in Manager.Runners.Where(r => r.Config.Kind == ServiceKinds.Minecraft || r.Config.Kind == ServiceKinds.Proxy)) server.Items.Add(r);
            if (keep != null && server.Items.Contains(keep)) server.SelectedItem = keep;
            else if (server.Items.Count > 0) server.SelectedIndex = 0;
            else LoadServer();
        }
    }
}
