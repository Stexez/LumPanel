using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

[assembly: System.Reflection.AssemblyVersion("1.4.0.0")]
[assembly: System.Reflection.AssemblyTitle("LumPanel")]

namespace LumPanel
{
    static class Program
    {
        [DllImport("user32.dll")] static extern bool SetProcessDPIAware();

        [STAThread]
        static int Main(string[] args)
        {
            string aiTest = ArgValue(args, "--ai-test");
            if (aiTest != null)
            {
                var client = new AiClient();
                client.SetKey(Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY") ?? "sk-ant-gecersiz-test");
                string result;
                try
                {
                    var reply = client.Send(new List<Dictionary<string, object>> { new Dictionary<string, object> { ["role"] = "user", ["content"] = "Paper sunucusunda 'Can't keep up! Is the server overloaded?' ne demek? 2 cümle." } });
                    result = "OK: " + reply.Text + $" [token in={client.InputTokensToday} out={client.OutputTokensToday}]";
                }
                catch (Exception ex) { result = "HATA: " + ex.Message; }
                File.WriteAllText(aiTest, result, System.Text.Encoding.UTF8);
                return 0;
            }
            string optTest = ArgValue(args, "--opt-test");
            if (optTest != null)
            {
                var files = new ServerFiles(optTest);
                int ok = 0, miss = 0;
                foreach (var g in Optimizer.Generate(30, 0, 0, 8).GroupBy(t => t.File))
                {
                    if (!files.Exists(g.Key)) { miss += g.Count(); continue; }
                    string text = files.Read(g.Key);
                    if (g.Key == "server.properties")
                    {
                        var present = new HashSet<string>(ServerFiles.ParseProperties(text).Select(k => k.Key));
                        text = ServerFiles.SetProperties(text, g.Where(t => present.Contains(t.Key)).ToDictionary(t => t.Key, t => t.Value));
                        ok += g.Count(t => present.Contains(t.Key)); miss += g.Count(t => !present.Contains(t.Key));
                    }
                    else foreach (var t in g) { if (ServerFiles.SetYaml(ref text, t.Key, t.Value)) ok++; else miss++; }
                    files.Write(g.Key, text);
                }
                File.WriteAllText(Path.Combine(optTest, "opt-result.txt"), $"ok={ok} miss={miss}");
                try { files.Resolve(@"..\..\Windows"); File.AppendAllText(Path.Combine(optTest, "opt-result.txt"), " ESCAPE!"); }
                catch (InvalidOperationException) { File.AppendAllText(Path.Combine(optTest, "opt-result.txt"), " escape-blocked"); }
                return 0;
            }
            string updTest = ArgValue(args, "--update-check");
            if (updTest != null)
            {
                var lines = new List<string>();
                foreach (var repo in new[] { "Stexez/LumPanel-yok-boyle-depo", "PaperMC/Velocity" })
                {
                    try { var r = Updater.Check(repo, null); lines.Add(repo + ": " + (r == null ? "guncel" : r.Tag)); }
                    catch (Exception ex) { lines.Add(repo + ": " + ex.Message); }
                }
                File.WriteAllLines(updTest, lines);
                return 0;
            }
            string ccTest = ArgValue(args, "--ai-test-cc");
            if (ccTest != null)
            {
                string outText;
                try
                {
                    string exe = ClaudeCodeBackend.Find();
                    var turn1 = ClaudeCodeBackend.Ask(exe, "haiku", AiClient.SystemPrompt,
                        "'LumSMP' konsolunda hata:\n<konsol>\n[12:00:01 ERROR]: Could not load 'plugins/EssentialsX.jar' in folder 'plugins'\norg.bukkit.plugin.UnknownDependencyException: Unknown/missing dependency plugins: [Vault]\n</konsol>\nNe oluyor, nasıl düzeltirim?", null);
                    var second = ClaudeCodeBackend.Ask(exe, "haiku", AiClient.SystemPrompt, "Vault'u nereden indiririm? Tek cümle.", turn1.SessionId);
                    outText = "EXE: " + exe + "\n--- 1 ---\n" + turn1.Text + "\n--- 2 (ayni oturum) ---\n" + second.Text +
                        $"\n[token {turn1.InputTokens + second.InputTokens}/{turn1.OutputTokens + second.OutputTokens}]";
                }
                catch (Exception ex) { outText = "HATA: " + ex.Message; }
                File.WriteAllText(ccTest, outText, System.Text.Encoding.UTF8);
                return 0;
            }
            string detectOut = ArgValue(args, "--detect");
            if (detectOut != null)
            {
                var found = Detector.Scan();
                File.WriteAllLines(detectOut, found.Select(f => $"{f.Id}|{f.Name}|{f.Kind}|{f.WorkingDir}|{f.Program}|{f.Arguments}|{f.MatchText}|{f.Featured}"));
                return 0;
            }
            string shotDir = ArgValue(args, "--shot");
            string configPath = ArgValue(args, "--config");
            bool autostart = args.Contains("--autostart");

            using var mutex = new Mutex(true, shotDir == null ? @"Global\LumPanel-single" : @"Global\LumPanel-shot-" + Guid.NewGuid(), out bool first);
            if (!first)
            {
                MessageBox.Show("LumPanel zaten açık (sağ alttaki simgeye çift tıkla).", "LumPanel", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return 1;
            }
            try { SetProcessDPIAware(); } catch { }
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.ThreadException += (s, e) => Crash(e.Exception);
            AppDomain.CurrentDomain.UnhandledException += (s, e) => Crash(e.ExceptionObject as Exception);

            var config = AppConfig.Load(configPath);
            if (configPath == null && !File.Exists(AppConfig.PathOf)) { try { config.Save(); } catch { } }
            using var manager = new ServiceManager(config);
            manager.AddEvent("Panel", args.Contains("--after-update") ? $"LumPanel {Updater.CurrentText} sürümüne güncellendi." : $"LumPanel {Updater.CurrentText} açıldı.");
            var form = new MainForm(manager);

            if (shotDir != null) return Screenshots(form, manager, shotDir, args);

            // local control port for the auto-updater (restarts go through the panel, not "crashes")
            using var control = new ControlServer(manager);
            try { control.Start(); }
            catch (Exception ex) { manager.AddEvent("Panel", "Kontrol kapısı açılamadı: " + ex.Message); }

            if (autostart)
            {
                form.WindowState = FormWindowState.Minimized;
                form.ShowInTaskbar = true;
            }
            form.Shown += (s, e) =>
            {
                if (config.AutoStartServicesOnLaunch) manager.StartAll(onlyAutoStart: true);
                if (autostart && config.MinimizeToTray) form.Hide();
            };
            Application.Run(form);
            return 0;
        }

        static string ArgValue(string[] args, string name)
        {
            int i = Array.IndexOf(args, name);
            return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
        }

        static void Crash(Exception ex)
        {
            try
            {
                File.AppendAllText(Path.Combine(AppConfig.Folder, "lumpanel-hata.log"), $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {ex}\r\n\r\n");
            }
            catch { }
            MessageBox.Show("Beklenmeyen bir hata oldu (lumpanel-hata.log dosyasına yazıldı):\n\n" + ex?.Message, "LumPanel",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }

        /// <summary>Test helper: --shot DIR [--start ID ...] [--send ID CMD] renders every page to PNG files.</summary>
        static int Screenshots(MainForm form, ServiceManager manager, string dir, string[] args)
        {
            Directory.CreateDirectory(dir);
            form.StartPosition = FormStartPosition.Manual;
            form.Location = new Point(0, 0);
            form.TopMost = true;
            form.Show();
            void Pump(int ms) { var until = DateTime.Now.AddMilliseconds(ms); while (DateTime.Now < until) { Application.DoEvents(); Thread.Sleep(30); } }
            for (int i = 0; i < args.Length - 1; i++)
                if (args[i] == "--start") manager.Find(args[i + 1])?.Start();
            Pump(6000);
            for (int i = 0; i < args.Length - 2; i++)
                if (args[i] == "--send") manager.Find(args[i + 1])?.Send(args[i + 2]);
            Pump(2500);
            var buttons = form.Controls.OfType<Panel>().SelectMany(p => p.Controls.OfType<NavButton>()).Reverse().ToList();
            int n = 0;
            foreach (var b in buttons)
            {
                b.GetType().GetMethod("OnClick", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).Invoke(b, new object[] { EventArgs.Empty });
                if (b.Text.Contains("Konsollar"))
                {
                    var running = manager.Runners.FirstOrDefault(r => r.IsOwned);
                    if (running != null) form.Consoles.Select(running);
                }
                Pump(1500);
                using var bmp = new Bitmap(form.Width, form.Height);
                using (var g = Graphics.FromImage(bmp)) g.CopyFromScreen(form.Location, Point.Empty, form.Size);
                string file = Path.Combine(dir, $"{++n}-{new string(b.Text.Where(char.IsLetter).ToArray())}.png");
                bmp.Save(file, ImageFormat.Png);
            }
            manager.StopAll(wait: true);
            form.Dispose();
            return 0;
        }
    }
}
