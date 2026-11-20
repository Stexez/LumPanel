using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Forms;

namespace LumPanel
{
    public class SettingsPage : Page
    {
        public override string Title => "Ayarlar";

        readonly ListBox services = new ListBox
        {
            Dock = DockStyle.Fill, BackColor = Theme.Input, ForeColor = Theme.Text, BorderStyle = BorderStyle.None, Font = Theme.Bold,
            ItemHeight = 30, DrawMode = DrawMode.OwnerDrawFixed, IntegralHeight = false,
        };
        readonly TextBox name = Theme.TextBox();
        readonly ComboBox kind = Theme.Combo();
        readonly TextBox dir = Theme.TextBox();
        readonly TextBox program = Theme.TextBox();
        readonly TextBox args = Theme.TextBox();
        readonly TextBox stopCmd = Theme.TextBox();
        readonly NumericUpDown stopTimeout = Num(5, 600);
        readonly NumericUpDown startDelay = Num(0, 600);
        readonly ComboBox encoding = Theme.Combo();
        readonly ComboBox priority = Theme.Combo();
        readonly TextBox match = Theme.TextBox();
        readonly TextBox quick = Theme.TextBox();
        readonly CheckBox autoStart = Theme.Check("Panel açılınca / 'Hepsini başlat' ile başlat");
        readonly CheckBox autoRestart = Theme.Check("Çökerse otomatik yeniden başlat");
        readonly CheckBox featured = Theme.Check("Genel Bakış'ta büyük göster (LumSMP)");

        readonly TextBox webhook = Theme.TextBox();
        readonly CheckBox withWindows = Theme.Check("Windows açılınca LumPanel'i başlat");
        readonly CheckBox autoServices = Theme.Check("LumPanel açılınca servisleri otomatik başlat");
        readonly CheckBox toTray = Theme.Check("Pencereyi kapatınca simge durumuna (tepsiye) küçült");
        readonly NumericUpDown maxLines = Num(500, 50000);
        readonly NumericUpDown aiLimit = Num(0, 1000);
        readonly ComboBox aiBackend = Theme.Combo();
        readonly TextBox updateRepo = Theme.TextBox();
        readonly CheckBox autoUpdate = Theme.Check("Yeni sürümü otomatik kur (sunucularda oyuncu yokken)");

        List<ServiceConfig> draft = new List<ServiceConfig>();
        ServiceConfig current;
        bool loading;

        static NumericUpDown Num(int min, int max) => Theme.Number(min, max, 100);

        public SettingsPage(ServiceManager m, MainForm f) : base(m, f)
        {
            foreach (var k in ServiceKinds.All) kind.Items.Add(ServiceKinds.Label(k));
            foreach (var e in new[] { "utf-8", "windows-1254", "ibm857" }) encoding.Items.Add(e);
            foreach (var p in new[] { "Normal", "AboveNormal", "High", "BelowNormal" }) priority.Items.Add(p);
            name.Width = kind.Width = 260;
            dir.Width = program.Width = args.Width = 470;
            stopCmd.Width = 140;
            encoding.Width = priority.Width = 150;
            match.Width = 260;
            quick.Width = 470;
            quick.Multiline = true;
            quick.Height = 70;
            quick.ScrollBars = ScrollBars.None;
            webhook.Width = 470;

            // left: service list
            var left = new Card { Dock = DockStyle.Left, Width = 290, Padding = new Padding(12) };
            var listButtons = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 84, BackColor = Theme.Card, Padding = new Padding(0, 8, 0, 0) };
            var add = DarkButton.Sized("+ Ekle", 84);
            var remove = DarkButton.Sized("Sil", 60, Theme.Red);
            var up = DarkButton.Sized("▲", 40);
            var down = DarkButton.Sized("▼", 40);
            var detect = DarkButton.Sized("🔍 Otomatik bul", 248, Theme.Accent);
            listButtons.Controls.AddRange(new Control[] { add, remove, up, down, detect });
            left.Controls.Add(services);
            left.Controls.Add(listButtons);
            left.Controls.Add(new Label { Text = "Servisler (başlatma sırası)", Dock = DockStyle.Top, Height = 28, ForeColor = Theme.Muted, Font = Theme.Bold });

            // right: editor + global settings
            var right = new Card { Dock = DockStyle.Fill, Padding = new Padding(20) };
            var flow = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true, BackColor = Theme.Card };
            right.Controls.Add(flow);
            // vertical scrolling only (the classic trick to drop the horizontal bar)
            flow.HorizontalScroll.Maximum = 0;
            flow.AutoScroll = false;
            flow.HorizontalScroll.Visible = false;
            flow.AutoScroll = true;
            Theme.Dark(flow);
            Theme.Dark(services);
            Theme.Dark(quick);

            var browseDir = DarkButton.Sized("Gözat", 84);
            var browseProgram = DarkButton.Sized("Gözat", 84);
            browseDir.Padding = browseProgram.Padding = Padding.Empty;
            flow.Controls.Add(Theme.Label("Seçili servis", Theme.H2));
            flow.Controls.Add(Pair(Field("Ad", name), Field("Tür", kind)));
            flow.Controls.Add(Field("Çalışma klasörü", Inline(dir, browseDir)));
            flow.Controls.Add(Field("Program (java, python.exe, caddy.exe veya .bat dosyası)", Inline(program, browseProgram)));
            flow.Controls.Add(Field("Argümanlar (ör. -Xms2G -Xmx6G -jar paper.jar nogui)", args));
            flow.Controls.Add(Pair(Field("Kapatma komutu (boş = türüne göre)", stopCmd), Field("Kapanma için bekle (sn)", stopTimeout), Field("Sonrakini başlatmadan bekle (sn)", startDelay)));
            flow.Controls.Add(Pair(Field("Çıktı kodlaması", encoding), Field("İşlemci önceliği", priority), Field("Panel dışında tanıma metni", match)));
            flow.Controls.Add(Field("Hızlı komut butonları (her satıra bir komut; boş = türüne göre)", quick));
            flow.Controls.Add(autoStart);
            flow.Controls.Add(autoRestart);
            flow.Controls.Add(featured);

            flow.Controls.Add(new Panel { Height = 1, Width = 620, BackColor = Theme.Border, Margin = new Padding(0, 18, 0, 10) });
            flow.Controls.Add(Theme.Label("Genel", Theme.H2));
            flow.Controls.Add(Field("Discord webhook adresi (çökme ve yeniden başlatma bildirimleri; boş = kapalı)", webhook));
            flow.Controls.Add(withWindows);
            flow.Controls.Add(autoServices);
            flow.Controls.Add(toTray);
            aiBackend.Width = 330;
            aiBackend.Items.Add("Claude Code · kendi Claude planım (ek ücret yok)");
            aiBackend.Items.Add("API anahtarı · kullanım başına ücretli");
            flow.Controls.Add(Field("Yapay zeka bağlantısı", aiBackend));
            flow.Controls.Add(Pair(Field("Konsolda tutulacak en fazla satır", maxLines), Field("Yapay zeka: günlük en fazla istek (0 = sınırsız)", aiLimit)));
            var forgetKey = DarkButton.Sized("Yapay zeka API anahtarını sil", 230);
            forgetKey.Margin = new Padding(0, 10, 0, 0);
            forgetKey.Click += (s, e) =>
            {
                Manager.Config.AiKeyProtected = "";
                Manager.Config.Save();
                MessageBox.Show(Main, "Anahtar silindi. Paneli yeniden açınca Yapay Zeka sayfası yeni anahtar ister.", "Ayarlar");
            };
            flow.Controls.Add(forgetKey);
            updateRepo.Width = 260;
            var checkNow = DarkButton.Sized("Güncellemeleri şimdi kontrol et", 250, Theme.Accent);
            checkNow.Margin = new Padding(0, 22, 0, 0);
            checkNow.Click += (s, e) => Main.CheckForUpdate(manual: true);
            flow.Controls.Add(new Panel { Height = 1, Width = 620, BackColor = Theme.Border, Margin = new Padding(0, 18, 0, 10) });
            flow.Controls.Add(Theme.Label($"Güncelleme · şu anki sürüm {Updater.CurrentText}", Theme.H2));
            flow.Controls.Add(Pair(Field("GitHub deposu (kullanıcı/depo)", updateRepo), checkNow));
            flow.Controls.Add(autoUpdate);

            var saveBar = new FlowLayoutPanel { AutoSize = true, BackColor = Theme.Card, Margin = new Padding(0, 16, 0, 0) };
            var save = DarkButton.Sized("💾 Kaydet", 130, Theme.Accent);
            var revert = DarkButton.Sized("Geri al", 90);
            var task = DarkButton.Sized("Sitenin eski zamanlanmış görevini kapat", 300);
            var openFolder = DarkButton.Sized("Panel klasörünü aç", 160);
            saveBar.Controls.AddRange(new Control[] { save, revert, task, openFolder });
            flow.Controls.Add(saveBar);
            flow.Controls.Add(new Label
            {
                Text = "Değişiklikler 'Kaydet' ile uygulanır. Çalışan bir servisin programı/argümanı değişirse yeni ayar bir sonraki başlatmada geçerli olur.\n" +
                       "'Panel dışında tanıma metni': panel açılmadan önce elle (CMD penceresinde) başlatılmış kopyayı bulmak için komut satırında aranır (ör. mc_agent.py).",
                ForeColor = Theme.Muted, Font = Theme.Small, AutoSize = true, MaximumSize = new Size(620, 0), Margin = new Padding(0, 12, 0, 0),
            });

            var gap = new Panel { Dock = DockStyle.Left, Width = 14, BackColor = Theme.Bg };
            Controls.Add(right);
            Controls.Add(gap);
            Controls.Add(left);
            Controls.Add(Header("Ayarlar", "Servisleri ekle, düzenle ve sırala. 'Otomatik bul' diskleri tarayıp sunucuları, botu ve agent'ı bulur."));

            services.DrawItem += (s, e) =>
            {
                if (e.Index < 0) return;
                var svc = (ServiceConfig)services.Items[e.Index];
                bool sel = (e.State & DrawItemState.Selected) != 0;
                using (var bg = new SolidBrush(sel ? Theme.CardHi : Theme.Input)) e.Graphics.FillRectangle(bg, e.Bounds);
                TextRenderer.DrawText(e.Graphics, $"{e.Index + 1}. {svc.Name}{(svc.Featured ? " ★" : "")}", Theme.Bold,
                    new Rectangle(e.Bounds.X + 8, e.Bounds.Y, e.Bounds.Width - 8, e.Bounds.Height), sel ? Theme.Text : Theme.Muted,
                    TextFormatFlags.VerticalCenter | TextFormatFlags.Left);
            };
            services.SelectedIndexChanged += (s, e) => ShowService(services.SelectedItem as ServiceConfig);
            foreach (Control c in new Control[] { name, dir, program, args, stopCmd, match, quick })
                c.TextChanged += (s, e) => Pull();
            foreach (var c in new[] { kind, encoding, priority }) c.SelectedIndexChanged += (s, e) => Pull();
            foreach (var c in new[] { stopTimeout, startDelay }) c.ValueChanged += (s, e) => Pull();
            foreach (var c in new[] { autoStart, autoRestart, featured }) c.CheckedChanged += (s, e) => Pull();
            browseDir.Click += (s, e) =>
            {
                using var d = new FolderBrowserDialog { SelectedPath = dir.Text };
                if (d.ShowDialog(Main) == DialogResult.OK) dir.Text = d.SelectedPath;
            };
            browseProgram.Click += (s, e) =>
            {
                using var d = new OpenFileDialog { Filter = "Program|*.exe;*.bat;*.cmd|Hepsi|*.*", InitialDirectory = Directory.Exists(dir.Text) ? dir.Text : "" };
                if (d.ShowDialog(Main) == DialogResult.OK) program.Text = d.FileName;
            };
            add.Click += (s, e) => { var svc = new ServiceConfig(); draft.Add(svc); FillList(svc); };
            remove.Click += (s, e) =>
            {
                if (current == null) return;
                if (MessageBox.Show(Main, $"{current.Name} silinsin mi? (Çalışıyorsa Kaydet'e basınca durdurulur.)", "Sil", MessageBoxButtons.YesNo) != DialogResult.Yes) return;
                draft.Remove(current);
                FillList(draft.FirstOrDefault());
            };
            up.Click += (s, e) => MoveService(-1);
            down.Click += (s, e) => MoveService(1);
            detect.Click += (s, e) => Detect(detect);
            save.Click += (s, e) => SaveAll();
            revert.Click += (s, e) => LoadFrom(Manager.Config);
            task.Click += (s, e) => DisableSiteTask();
            openFolder.Click += (s, e) => Process.Start("explorer.exe", $"\"{AppConfig.Folder}\"");
            LoadFrom(Manager.Config);
        }

        static Control Field(string label, Control input)
        {
            var p = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, BackColor = Theme.Card, Margin = new Padding(0, 8, 18, 0), WrapContents = false };
            p.Controls.Add(Theme.Label(label, Theme.Small, Theme.Muted));
            p.Controls.Add(input);
            return p;
        }

        static Control Pair(params Control[] fields)
        {
            var p = new FlowLayoutPanel { AutoSize = true, BackColor = Theme.Card, Margin = Padding.Empty, WrapContents = false };
            p.Controls.AddRange(fields);
            return p;
        }

        static Control Inline(Control a, Control b)
        {
            var p = new FlowLayoutPanel { AutoSize = true, BackColor = Theme.Card, Margin = Padding.Empty, WrapContents = false };
            b.Margin = new Padding(6, 0, 0, 0);
            b.Height = 26;
            p.Controls.Add(a);
            p.Controls.Add(b);
            return p;
        }

        void LoadFrom(AppConfig cfg)
        {
            draft = cfg.Services.Select(s => { var c = s.Clone(); c.QuickCommands = new List<string>(s.QuickCommands ?? new List<string>()); return c; }).ToList();
            webhook.Text = cfg.DiscordWebhook;
            withWindows.Checked = cfg.StartWithWindows;
            autoServices.Checked = cfg.AutoStartServicesOnLaunch;
            toTray.Checked = cfg.MinimizeToTray;
            maxLines.Value = Math.Max(500, Math.Min(50000, cfg.ConsoleMaxLines));
            aiLimit.Value = Math.Max(0, Math.Min(1000, cfg.AiDailyLimit));
            aiBackend.SelectedIndex = cfg.AiBackend == "api" ? 1 : 0;
            updateRepo.Text = cfg.UpdateRepo;
            autoUpdate.Checked = cfg.AutoUpdate;
            FillList(draft.FirstOrDefault());
        }

        void FillList(ServiceConfig select)
        {
            services.BeginUpdate();
            services.Items.Clear();
            foreach (var s in draft) services.Items.Add(s);
            services.EndUpdate();
            if (select != null) services.SelectedItem = select; else ShowService(null);
        }

        void ShowService(ServiceConfig s)
        {
            current = s;
            loading = true;
            bool has = s != null;
            foreach (Control c in new Control[] { name, kind, dir, program, args, stopCmd, stopTimeout, startDelay, encoding, priority, match, quick, autoStart, autoRestart, featured })
                c.Enabled = has;
            if (has)
            {
                name.Text = s.Name;
                kind.SelectedIndex = Math.Max(0, Array.IndexOf(ServiceKinds.All, s.Kind));
                dir.Text = s.WorkingDir;
                program.Text = s.Program;
                args.Text = s.Arguments;
                stopCmd.Text = s.StopCommand;
                stopTimeout.Value = Math.Max(5, Math.Min(600, s.StopTimeoutSec));
                startDelay.Value = Math.Max(0, Math.Min(600, s.StartDelaySec));
                encoding.SelectedItem = encoding.Items.Contains(s.Encoding) ? s.Encoding : "utf-8";
                priority.SelectedItem = priority.Items.Contains(s.Priority) ? s.Priority : "Normal";
                match.Text = s.MatchText;
                quick.Text = string.Join(Environment.NewLine, s.QuickCommands ?? new List<string>());
                autoStart.Checked = s.AutoStart;
                autoRestart.Checked = s.AutoRestart;
                featured.Checked = s.Featured;
            }
            loading = false;
        }

        void Pull()
        {
            if (loading || current == null) return;
            current.Name = name.Text.Trim().Length > 0 ? name.Text.Trim() : "Adsız";
            current.Kind = ServiceKinds.All[Math.Max(0, kind.SelectedIndex)];
            current.WorkingDir = dir.Text.Trim();
            current.Program = program.Text.Trim();
            current.Arguments = args.Text.Trim();
            current.StopCommand = stopCmd.Text.Trim();
            current.StopTimeoutSec = (int)stopTimeout.Value;
            current.StartDelaySec = (int)startDelay.Value;
            current.Encoding = encoding.SelectedItem as string ?? "utf-8";
            current.Priority = priority.SelectedItem as string ?? "Normal";
            current.MatchText = match.Text.Trim();
            current.QuickCommands = quick.Text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim().TrimStart('/')).Where(x => x.Length > 0).ToList();
            current.AutoStart = autoStart.Checked;
            current.AutoRestart = autoRestart.Checked;
            if (featured.Checked && !current.Featured) foreach (var o in draft) o.Featured = false;
            current.Featured = featured.Checked;
            services.Invalidate();
        }

        void MoveService(int delta)
        {
            if (current == null) return;
            int i = draft.IndexOf(current), j = i + delta;
            if (j < 0 || j >= draft.Count) return;
            draft.RemoveAt(i);
            draft.Insert(j, current);
            FillList(current);
        }

        void Detect(Button button)
        {
            button.Enabled = false;
            string old = button.Text;
            ThreadPool.QueueUserWorkItem(_ =>
            {
                List<ServiceConfig> found;
                try { found = Detector.Scan(msg => BeginInvoke(new Action(() => button.Text = msg.Length > 30 ? "…" + msg.Substring(msg.Length - 28) : msg))); }
                catch (Exception ex) { found = new List<ServiceConfig>(); BeginInvoke(new Action(() => MessageBox.Show(Main, ex.Message, "Otomatik bul"))); }
                BeginInvoke(new Action(() =>
                {
                    button.Text = old;
                    button.Enabled = true;
                    ChooseDetected(found);
                }));
            });
        }

        void ChooseDetected(List<ServiceConfig> found)
        {
            var fresh = found.Where(f => !draft.Any(d =>
                string.Equals(d.WorkingDir?.TrimEnd('\\'), f.WorkingDir.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase) &&
                string.Equals(d.Arguments, f.Arguments, StringComparison.OrdinalIgnoreCase))).ToList();
            if (fresh.Count == 0) { MessageBox.Show(Main, "Yeni bir sunucu, bot veya agent bulunamadı.", "Otomatik bul"); return; }
            using var dlg = new Form
            {
                Text = "Bulunanlar", Size = new Size(820, 440), StartPosition = FormStartPosition.CenterParent, BackColor = Theme.Bg, ForeColor = Theme.Text,
                FormBorderStyle = FormBorderStyle.FixedDialog, MaximizeBox = false, MinimizeBox = false,
            };
            var box = new CheckedListBox { Dock = DockStyle.Fill, BackColor = Theme.Input, ForeColor = Theme.Text, BorderStyle = BorderStyle.None, Font = Theme.Body, CheckOnClick = true };
            foreach (var f in fresh) box.Items.Add($"{f.Name}  —  {f.WorkingDir}  ({f.Program} {f.Arguments})", true);
            var ok = DarkButton.Sized("Seçilenleri ekle", 160, Theme.Accent);
            ok.Dock = DockStyle.Bottom;
            ok.DialogResult = DialogResult.OK;
            dlg.Controls.Add(box);
            dlg.Controls.Add(ok);
            dlg.Controls.Add(new Label { Text = "Eklenecekleri işaretle. Sonra ayarları kontrol edip Kaydet'e bas.", Dock = DockStyle.Top, Height = 30, ForeColor = Theme.Muted, Padding = new Padding(6) });
            dlg.AcceptButton = ok;
            if (dlg.ShowDialog(Main) != DialogResult.OK) return;
            ServiceConfig last = null;
            bool hasFeatured = draft.Any(d => d.Featured);
            for (int i = 0; i < fresh.Count; i++)
            {
                if (!box.GetItemChecked(i)) continue;
                var f = fresh[i];
                if (hasFeatured) f.Featured = false;
                hasFeatured |= f.Featured;
                while (draft.Any(d => d.Id == f.Id)) f.Id += "-2";
                // game servers and proxy first, python/other services after them
                int insertAt = f.Kind == ServiceKinds.Minecraft || f.Kind == ServiceKinds.Proxy
                    ? draft.FindIndex(d => d.Kind != ServiceKinds.Minecraft && d.Kind != ServiceKinds.Proxy) : -1;
                if (insertAt < 0) draft.Add(f); else draft.Insert(insertAt, f);
                last = f;
            }
            FillList(last ?? current);
        }

        void SaveAll()
        {
            var bad = draft.FirstOrDefault(d => string.IsNullOrWhiteSpace(d.Program));
            if (bad != null) { MessageBox.Show(Main, $"{bad.Name}: program boş olamaz.", "Ayarlar"); services.SelectedItem = bad; return; }
            var cfg = new AppConfig
            {
                Services = draft,
                Schedules = Manager.Config.Schedules,
                DiscordWebhook = webhook.Text.Trim(),
                StartWithWindows = withWindows.Checked,
                AutoStartServicesOnLaunch = autoServices.Checked,
                MinimizeToTray = toTray.Checked,
                ConsoleMaxLines = (int)maxLines.Value,
                BackupDir = Manager.Config.BackupDir,
                BackupKeep = Manager.Config.BackupKeep,
                AiKeyProtected = Manager.Config.AiKeyProtected,
                AiModel = Manager.Config.AiModel,
                AiAutoExplain = Manager.Config.AiAutoExplain,
                AiDailyLimit = (int)aiLimit.Value,
                AiBackend = aiBackend.SelectedIndex == 1 ? "api" : "claude-code",
                AiClaudePath = Manager.Config.AiClaudePath,
                UpdateRepo = updateRepo.Text.Trim(),
                UpdateToken = Manager.Config.UpdateToken,
                AutoUpdate = autoUpdate.Checked,
            };
            try { MainForm.SetStartWithWindows(cfg.StartWithWindows); }
            catch (Exception ex) { MessageBox.Show(Main, "Windows başlangıcına eklenemedi: " + ex.Message, "Ayarlar"); }
            Manager.ApplyConfig(cfg);
            Main.ServicesChanged();
            LoadFrom(Manager.Config);
            MessageBox.Show(Main, "Ayarlar kaydedildi." + (cfg.AiBackend != Manager.Config.AiBackend ? "" : "") +
                "\n(Yapay zeka bağlantısı değiştiyse paneli yeniden aç.)", "Ayarlar", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        void DisableSiteTask()
        {
            if (MessageBox.Show(Main,
                    "Site şu an Windows zamanlanmış görevi ('LUM Craft Website') ile de başlıyor olabilir. Panel yönetecekse bu görev kapatılmalı, yoksa iki kopya çakışır.\n\nGörev devre dışı bırakılsın ve çalışan kopyası durdurulsun mu?",
                    "Sitenin zamanlanmış görevi", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            var log = new List<string>();
            foreach (var cmd in new[] { "/End /TN \"LUM Craft Website\"", "/Change /TN \"LUM Craft Website\" /DISABLE" })
            {
                try
                {
                    using var p = Process.Start(new ProcessStartInfo("schtasks", cmd)
                    { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true });
                    string outText = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd();
                    p.WaitForExit(10000);
                    log.Add(outText.Trim());
                }
                catch (Exception ex) { log.Add(ex.Message); }
            }
            var site = Manager.Runners.FirstOrDefault(r => r.Config.Id == "site");
            if (site != null && site.State == ServiceState.External) site.Stop();
            Manager.AddEvent("Panel", "Sitenin zamanlanmış görevi devre dışı bırakıldı.");
            MessageBox.Show(Main, string.Join("\n", log.Where(l => l.Length > 0)) + "\n\nArtık siteyi Genel Bakış'tan başlat.", "Sitenin zamanlanmış görevi");
        }
    }
}
