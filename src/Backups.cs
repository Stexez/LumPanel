using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;
using System.Windows.Forms;

namespace LumPanel
{
    /// <summary>Zips the worlds of a Minecraft server; safe while running (save-off / save-all flush / save-on).</summary>
    public static class BackupService
    {
        static readonly object busyGate = new object();
        static readonly HashSet<string> busy = new HashSet<string>();

        public static string Folder(AppConfig cfg) =>
            string.IsNullOrWhiteSpace(cfg.BackupDir) ? Path.Combine(AppConfig.Folder, "yedekler") : cfg.BackupDir.Trim();

        public static IEnumerable<string> Worlds(ServiceConfig s)
        {
            if (string.IsNullOrWhiteSpace(s.WorkingDir) || !Directory.Exists(s.WorkingDir)) return new string[0];
            return Directory.GetDirectories(s.WorkingDir).Where(d => File.Exists(Path.Combine(d, "level.dat")));
        }

        public static bool IsBusy(string id) { lock (busyGate) return busy.Contains(id); }

        /// <summary>Runs on the calling thread (use a worker). Returns the zip path or throws.</summary>
        public static string Run(ServiceManager manager, ServiceRunner r, Action<string> progress = null)
        {
            lock (busyGate) if (!busy.Add(r.Config.Id)) throw new InvalidOperationException("Bu sunucunun yedeği zaten alınıyor.");
            bool paused = false;
            try
            {
                var worlds = Worlds(r.Config).ToList();
                if (worlds.Count == 0) throw new InvalidOperationException("Sunucu klasöründe dünya (level.dat) bulunamadı.");
                if (r.State == ServiceState.Running && r.Config.Kind == ServiceKinds.Minecraft)
                {
                    progress?.Invoke("Dünya kaydediliyor...");
                    r.Send("save-off");
                    r.Send("save-all flush");
                    paused = true;
                    Thread.Sleep(8000);
                }
                string dir = Folder(manager.Config);
                Directory.CreateDirectory(dir);
                string safe = new string(r.Config.Name.Select(c => char.IsLetterOrDigit(c) ? c : '-').ToArray());
                string zip = Path.Combine(dir, $"{safe}-{DateTime.Now:yyyyMMdd-HHmmss}.zip");
                string tmp = zip + ".part";
                long total = 0;
                using (var fs = new FileStream(tmp, FileMode.Create))
                using (var archive = new ZipArchive(fs, ZipArchiveMode.Create))
                {
                    foreach (var world in worlds)
                    {
                        string root = Path.GetFileName(world);
                        var files = Directory.GetFiles(world, "*", SearchOption.AllDirectories);
                        int i = 0;
                        foreach (var file in files)
                        {
                            if (Path.GetFileName(file).Equals("session.lock", StringComparison.OrdinalIgnoreCase)) continue;
                            if (++i % 200 == 0) progress?.Invoke($"{root}: {i}/{files.Length} dosya");
                            string entryName = root + "/" + file.Substring(world.Length + 1).Replace('\\', '/');
                            try
                            {
                                using var src = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                                var entry = archive.CreateEntry(entryName, CompressionLevel.Fastest);
                                entry.LastWriteTime = File.GetLastWriteTime(file);
                                using var dst = entry.Open();
                                src.CopyTo(dst);
                                total += src.Length;
                            }
                            catch (IOException) { /* file vanished or locked: skip */ }
                        }
                    }
                }
                File.Move(tmp, zip);
                Prune(manager.Config, safe);
                manager.AddEvent(r.Config.Name, $"yedek alındı: {Path.GetFileName(zip)} ({Theme.Bytes(new FileInfo(zip).Length)})");
                return zip;
            }
            catch (Exception ex)
            {
                manager.AddEvent(r.Config.Name, "YEDEK ALINAMADI: " + ex.Message, true);
                throw;
            }
            finally
            {
                if (paused) r.Send("save-on");
                lock (busyGate) busy.Remove(r.Config.Id);
            }
        }

        static void Prune(AppConfig cfg, string prefix)
        {
            int keep = Math.Max(1, cfg.BackupKeep);
            var old = new DirectoryInfo(Folder(cfg)).GetFiles(prefix + "-*.zip").OrderByDescending(f => f.Name).Skip(keep);
            foreach (var f in old) { try { f.Delete(); } catch { } }
        }
    }

    public class BackupsPage : Page
    {
        public override string Title => "Yedekler";
        readonly ComboBox server = Theme.Combo();
        readonly ListView list = Theme.List();
        readonly Label status = Theme.Label("", Theme.Bold, Theme.Muted);
        readonly NumericUpDown keep = Theme.Number(1, 100, 80, 7);
        readonly TextBox folder = Theme.TextBox();
        DateTime lastList = DateTime.MinValue;

        public BackupsPage(ServiceManager m, MainForm f) : base(m, f)
        {
            server.Width = 220;
            folder.Width = 380;
            var top = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 44, BackColor = Theme.Bg, WrapContents = false };
            var lbl = Theme.Label("Sunucu:", Theme.Bold);
            lbl.Margin = new Padding(0, 10, 6, 0);
            server.Margin = new Padding(0, 6, 10, 0);
            var now = DarkButton.Sized("💾 Şimdi yedekle", 150, Theme.Accent);
            var open = DarkButton.Sized("Klasörü aç", 110);
            var del = DarkButton.Sized("Seçiliyi sil", 110, Theme.Red);
            status.Margin = new Padding(14, 10, 0, 0);
            top.Controls.AddRange(new Control[] { lbl, server, now, open, del, status });

            var opts = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 44, BackColor = Theme.Bg, WrapContents = false };
            var l1 = Theme.Label("Yedek klasörü (boş = panelin yanındaki 'yedekler'):", Theme.Small, Theme.Muted);
            var l2 = Theme.Label("Sunucu başına sakla:", Theme.Small, Theme.Muted);
            l1.Margin = new Padding(0, 12, 6, 0);
            l2.Margin = new Padding(14, 12, 6, 0);
            folder.Margin = new Padding(0, 8, 0, 0);
            keep.Margin = new Padding(0, 8, 8, 0);
            var save = DarkButton.Sized("Kaydet", 80);
            opts.Controls.AddRange(new Control[] { l1, folder, l2, keep, save });

            list.Columns.Add("Dosya", 360);
            list.Columns.Add("Boyut", 110);
            list.Columns.Add("Tarih", 300);
            list.Dock = DockStyle.Fill;
            var card = new Card { Dock = DockStyle.Fill, Padding = new Padding(10) };
            card.Controls.Add(list);

            var note = new Label
            {
                Dock = DockStyle.Bottom, Height = 40, ForeColor = Theme.Muted, Font = Theme.Small, Padding = new Padding(0, 8, 0, 0),
                Text = "Yedek sunucu açıkken de alınır (save-off → save-all flush → zip → save-on). Geri yüklemek için: sunucuyu durdur, dünya klasörünü yedekteki ile değiştir, başlat. Zamanlayıcı'dan \"Her gün yedek al\" görevi de eklenebilir.",
            };

            Controls.Add(card);
            Controls.Add(note);
            Controls.Add(opts);
            Controls.Add(top);
            Controls.Add(Header("Yedekler", "Minecraft dünyalarının zip yedekleri."));

            now.Click += (s, e) => BackupNow();
            open.Click += (s, e) =>
            {
                string dir = BackupService.Folder(Manager.Config);
                Directory.CreateDirectory(dir);
                Process.Start("explorer.exe", $"\"{dir}\"");
            };
            del.Click += (s, e) =>
            {
                if (list.SelectedItems.Count == 0) return;
                var file = (string)list.SelectedItems[0].Tag;
                if (MessageBox.Show(Main, Path.GetFileName(file) + " silinsin mi?", "Yedekler", MessageBoxButtons.YesNo) != DialogResult.Yes) return;
                try { File.Delete(file); } catch (Exception ex) { MessageBox.Show(Main, ex.Message); }
                lastList = DateTime.MinValue;
                RefreshView();
            };
            save.Click += (s, e) =>
            {
                Manager.Config.BackupDir = folder.Text.Trim();
                Manager.Config.BackupKeep = (int)keep.Value;
                Manager.Config.Save();
                lastList = DateTime.MinValue;
                RefreshView();
            };
            ServicesChanged();
        }

        void BackupNow()
        {
            var r = server.SelectedItem as ServiceRunner;
            if (r == null) return;
            if (BackupService.IsBusy(r.Config.Id)) return;
            status.Text = "Yedek alınıyor...";
            ThreadPool.QueueUserWorkItem(_ =>
            {
                string msg;
                try { msg = "Yedek hazır: " + Path.GetFileName(BackupService.Run(Manager, r, p => BeginInvoke(new Action(() => status.Text = p)))); }
                catch (Exception ex) { msg = "Hata: " + ex.Message; }
                BeginInvoke(new Action(() => { status.Text = msg; lastList = DateTime.MinValue; RefreshView(); }));
            });
        }

        public override void ServicesChanged()
        {
            server.Items.Clear();
            foreach (var r in Manager.Runners.Where(r => r.Config.Kind == ServiceKinds.Minecraft)) server.Items.Add(r);
            if (server.Items.Count > 0) server.SelectedIndex = 0;
            folder.Text = Manager.Config.BackupDir;
            keep.Value = Math.Max(1, Math.Min(100, Manager.Config.BackupKeep));
        }

        public override void RefreshView()
        {
            if (DateTime.Now - lastList < TimeSpan.FromSeconds(10)) return;
            lastList = DateTime.Now;
            string dir = BackupService.Folder(Manager.Config);
            var files = Directory.Exists(dir) ? new DirectoryInfo(dir).GetFiles("*.zip").OrderByDescending(x => x.LastWriteTime).ToList() : new List<FileInfo>();
            list.BeginUpdate();
            list.Items.Clear();
            foreach (var fi in files)
            {
                var item = new ListViewItem(fi.Name) { Tag = fi.FullName };
                item.SubItems.Add(Theme.Bytes(fi.Length));
                item.SubItems.Add(fi.LastWriteTime.ToString("dd.MM.yyyy HH:mm"));
                list.Items.Add(item);
            }
            list.EndUpdate();
        }
    }
}
