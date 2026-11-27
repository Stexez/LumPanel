using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;

namespace LumPanel
{
    /// <summary>
    /// Local control port for the LUM Craft auto-updater (oto-guncelle.ps1): it restarts / stops / starts services
    /// through the panel, so the panel knows the stop was wanted. (An RCON "stop" or a killed process looks like a
    /// crash: red light, "ÇÖKTÜ" event, Discord alert.)
    ///
    /// Listens on 127.0.0.1 only, on a free port chosen at start. Every request needs the token written to
    /// %ProgramData%\LumPanel\kontrol.json, which only SYSTEM and the user running the panel can read.
    ///   GET  /durum                         all services
    ///   GET  /servis/{hedef}                one service ("site", "lumsmp", id or name)
    ///   POST /servis/{hedef}/yeniden-baslat?sebep=...   (also: durdur, baslat)
    ///   POST /servis/{hedef}/komut?komut=...&sebep=...   console command (Minecraft / proxy only); returns new lines
    /// </summary>
    public sealed class ControlServer : IDisposable
    {
        readonly ServiceManager manager;
        TcpListener listener;
        Thread thread;
        volatile bool running;
        string token;

        public static string Folder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "LumPanel");
        public static string KeyFile => Path.Combine(Folder, "kontrol.json");

        public ControlServer(ServiceManager manager) { this.manager = manager; }

        public void Start()
        {
            var bytes = new byte[24];
            using (var rng = RandomNumberGenerator.Create()) rng.GetBytes(bytes);
            token = Convert.ToBase64String(bytes).Replace('+', '-').Replace('/', '_').TrimEnd('=');
            listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            WriteKeyFile(((IPEndPoint)listener.LocalEndpoint).Port);
            running = true;
            thread = new Thread(Loop) { IsBackground = true, Name = "LumPanel kontrol" };
            thread.Start();
        }

        void WriteKeyFile(int port)
        {
            Directory.CreateDirectory(Folder);
            var security = new FileSecurity();
            security.SetAccessRuleProtection(true, false);
            security.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null),
                FileSystemRights.FullControl, AccessControlType.Allow));
            security.AddAccessRule(new FileSystemAccessRule(WindowsIdentity.GetCurrent().User,
                FileSystemRights.FullControl, AccessControlType.Allow));
            string json = new JavaScriptSerializer().Serialize(new
            {
                port, token, pid = Process.GetCurrentProcess().Id, surum = Updater.CurrentText,
            });
            try { File.Delete(KeyFile); } catch { }
            // created with its final ACL, so it is never readable by others, not even for a moment
            using (var fs = new FileStream(KeyFile, FileMode.CreateNew, FileSystemRights.Write, FileShare.None, 4096, FileOptions.None, security))
            {
                var data = new UTF8Encoding(false).GetBytes(json);
                fs.Write(data, 0, data.Length);
            }
        }

        void Loop()
        {
            while (running)
            {
                TcpClient client;
                try { client = listener.AcceptTcpClient(); }
                catch { if (!running) return; Thread.Sleep(200); continue; }
                ThreadPool.QueueUserWorkItem(_ => Handle(client));
            }
        }

        void Handle(TcpClient client)
        {
            using (client)
            {
                try
                {
                    client.ReceiveTimeout = 5000;
                    client.SendTimeout = 5000;
                    var stream = client.GetStream();
                    var head = ReadHead(stream);
                    if (head == null) return;
                    var lines = head.Split(new[] { "\r\n" }, StringSplitOptions.None);
                    var first = lines[0].Split(' ');
                    if (first.Length < 2) { Reply(stream, 400, new { hata = "geçersiz istek" }); return; }
                    string auth = lines.Skip(1).Where(l => l.StartsWith("Authorization:", StringComparison.OrdinalIgnoreCase))
                        .Select(l => l.Substring(14).Trim()).FirstOrDefault() ?? "";
                    if (!SameToken(auth, "Bearer " + token)) { Reply(stream, 401, new { hata = "yetkisiz" }); return; }
                    var (status, body) = Route(first[0].ToUpperInvariant(), first[1]);
                    Reply(stream, status, body);
                }
                catch (Exception ex) { Trace.WriteLine("Kontrol kapısı: " + ex.Message); }
            }
        }

        static string ReadHead(NetworkStream stream)
        {
            var buffer = new List<byte>();
            int b;
            while ((b = stream.ReadByte()) != -1)
            {
                buffer.Add((byte)b);
                int n = buffer.Count;
                if (n >= 4 && buffer[n - 4] == '\r' && buffer[n - 3] == '\n' && buffer[n - 2] == '\r' && buffer[n - 1] == '\n')
                    return Encoding.UTF8.GetString(buffer.ToArray(), 0, n - 4);
                if (n > 8192) return null;
            }
            return null;
        }

        static bool SameToken(string a, string b)
        {
            var x = Encoding.UTF8.GetBytes(a ?? "");
            var y = Encoding.UTF8.GetBytes(b ?? "");
            int diff = x.Length ^ y.Length;
            for (int i = 0; i < Math.Min(x.Length, y.Length); i++) diff |= x[i] ^ y[i];
            return diff == 0;
        }

        static void Reply(NetworkStream stream, int status, object body)
        {
            var data = Encoding.UTF8.GetBytes(new JavaScriptSerializer().Serialize(body));
            string reason = status == 200 ? "OK" : status == 401 ? "Unauthorized" : status == 404 ? "Not Found" : "Bad Request";
            var headBytes = Encoding.ASCII.GetBytes($"HTTP/1.1 {status} {reason}\r\nContent-Type: application/json; charset=utf-8\r\nContent-Length: {data.Length}\r\nConnection: close\r\n\r\n");
            stream.Write(headBytes, 0, headBytes.Length);
            stream.Write(data, 0, data.Length);
        }

        static object Describe(ServiceRunner r) => new
        {
            id = r.Config.Id, ad = r.Config.Name, tur = r.Config.Kind, durum = r.State.ToString(),
            oyuncu = r.Players.Length, one_cikan = r.Config.Featured,
        };

        /// <summary>"site", "lumsmp", an id or a (part of a) name.</summary>
        ServiceRunner Resolve(string target)
        {
            target = Uri.UnescapeDataString(target ?? "").Trim();
            var all = manager.Runners.ToList();
            bool Has(string text, string part) => (text ?? "").IndexOf(part, StringComparison.OrdinalIgnoreCase) >= 0;
            if (target.Equals("site", StringComparison.OrdinalIgnoreCase))
                return all.FirstOrDefault(r => Has(r.Config.WorkingDir, "Website") || Has(r.Config.Arguments, "server.py") || Has(r.Config.Program, "server.py"))
                    ?? all.FirstOrDefault(r => r.Config.Kind == ServiceKinds.Python && Has(r.Config.Name, "site"));
            if (target.Equals("lumsmp", StringComparison.OrdinalIgnoreCase))
                return all.FirstOrDefault(r => r.Config.Kind == ServiceKinds.Minecraft && Has(r.Config.Name, "lumsmp"))
                    ?? all.FirstOrDefault(r => r.Config.Kind == ServiceKinds.Minecraft && Has(r.Config.WorkingDir, "lumsmp"));
            return all.FirstOrDefault(r => r.Config.Id == target)
                ?? all.FirstOrDefault(r => r.Config.Name.Equals(target, StringComparison.OrdinalIgnoreCase))
                ?? (all.Count(r => Has(r.Config.Name, target)) == 1 ? all.First(r => Has(r.Config.Name, target)) : null);
        }

        (int, object) Route(string method, string rawPath)
        {
            string path = rawPath, query = "";
            int q = rawPath.IndexOf('?');
            if (q >= 0) { path = rawPath.Substring(0, q); query = rawPath.Substring(q + 1); }
            string reason = query.Split('&').Where(p => p.StartsWith("sebep=")).Select(p => Uri.UnescapeDataString(p.Substring(6).Replace('+', ' '))).FirstOrDefault();
            reason = string.IsNullOrWhiteSpace(reason) ? "otomatik güncelleyici" : reason.Trim();
            if (reason.Length > 200) reason = reason.Substring(0, 200);
            var parts = path.Trim('/').Split('/');

            if (method == "GET" && path.TrimEnd('/') == "/durum")
                return (200, new { panel = Updater.CurrentText, servisler = manager.Runners.Select(Describe).ToList() });

            if (parts.Length >= 2 && parts[0] == "servis")
            {
                var runner = Resolve(parts[1]);
                if (runner == null) return (404, new { hata = $"'{parts[1]}' adlı servis panelde yok" });
                if (parts.Length == 2 && method == "GET") return (200, Describe(runner));
                if (parts.Length == 3 && method == "POST" && parts[2] == "komut")
                {
                    string command = query.Split('&').Where(p => p.StartsWith("komut="))
                        .Select(p => Uri.UnescapeDataString(p.Substring(6))).FirstOrDefault();
                    return RunCommand(runner, command, reason);
                }
                if (parts.Length == 3 && method == "POST")
                {
                    switch (parts[2])
                    {
                        case "yeniden-baslat":
                            manager.AddEvent(runner.Config.Name, $"yeniden başlatılıyor ({reason})");
                            runner.AddPanelLine($"» Yeniden başlatılıyor: {reason}");
                            runner.Stop(restart: true);
                            return (200, Describe(runner));
                        case "durdur":
                            manager.AddEvent(runner.Config.Name, $"durduruluyor ({reason})");
                            runner.AddPanelLine($"» Durduruluyor: {reason}");
                            runner.Stop();
                            return (200, Describe(runner));
                        case "baslat":
                            manager.AddEvent(runner.Config.Name, $"başlatılıyor ({reason})");
                            string error = runner.IsAlive ? null : runner.Start();
                            if (!string.IsNullOrEmpty(error)) return (400, new { hata = error });
                            return (200, Describe(runner));
                    }
                }
            }
            return (404, new { hata = "bilinmeyen istek" });
        }

        /// <summary>A line typed into the panel console from outside. Stop/restart go through the panel's own
        /// buttons, so they are not mistaken for a crash.</summary>
        (int, object) RunCommand(ServiceRunner runner, string command, string reason)
        {
            command = (command ?? "").Trim().TrimStart('/').Trim();
            if (runner.Config.Kind != ServiceKinds.Minecraft && runner.Config.Kind != ServiceKinds.Proxy)
                return (400, new { hata = "bu servise konsol komutu gönderilemez" });
            if (command.Length == 0 || command.Length > 500 || command.Any(char.IsControl))
                return (400, new { hata = "geçersiz komut" });
            if (!runner.IsOwned) return (400, new { hata = "sunucu panelde çalışmıyor" });

            long before = runner.LinesAfter(0).LastOrDefault()?.Seq ?? 0;
            manager.AddEvent(runner.Config.Name, $"komut: {command} ({reason})");
            runner.AddPanelLine($"» {reason}:");
            string verb = command.Split(' ')[0].ToLowerInvariant();
            if (verb == "restart") runner.Stop(restart: true);
            else if (verb == "stop" || verb == "end" || verb == "shutdown") runner.Stop();
            else
            {
                string error = runner.Send(command);
                if (error != null) return (400, new { hata = error });
            }
            Thread.Sleep(1500);
            var output = runner.LinesAfter(before).Where(l => l.Level != LineLevel.Input && l.Level != LineLevel.Panel)
                .Select(l => l.Text).Take(40).ToList();
            return (200, new { durum = runner.State.ToString(), cikti = output });
        }

        public void Dispose()
        {
            running = false;
            try { listener?.Stop(); } catch { }
            try { File.Delete(KeyFile); } catch { }
        }
    }
}
