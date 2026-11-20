using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace LumPanel
{
    /// <summary>
    /// Self-update from GitHub Releases: the latest release must carry a "LumPanel.exe" asset (and optionally
    /// "LumPanel.exe.sha256"). Settings, logs and backups live next to the exe in separate files and are never touched.
    /// </summary>
    public static class Updater
    {
        public static Version Current => Assembly.GetExecutingAssembly().GetName().Version;
        public static string CurrentText => $"v{Current.Major}.{Current.Minor}.{Current.Build}";

        public sealed class Release
        {
            public Version Version;
            public string Tag, Notes, ExeUrl, ShaUrl;
        }

        static WebClient Client(string token)
        {
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            var wc = new WebClient { Encoding = Encoding.UTF8 };
            wc.Headers[HttpRequestHeader.UserAgent] = "LumPanel-Updater";
            wc.Headers[HttpRequestHeader.Accept] = "application/vnd.github+json";
            if (!string.IsNullOrWhiteSpace(token)) wc.Headers[HttpRequestHeader.Authorization] = "Bearer " + token.Trim();
            return wc;
        }

        /// <summary>Returns the newest release if it is newer than this exe, else null.</summary>
        public static Release Check(string repo, string token)
        {
            repo = (repo ?? "").Trim().Trim('/');
            if (!System.Text.RegularExpressions.Regex.IsMatch(repo, @"^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$"))
                throw new InvalidOperationException("GitHub deposu 'kullanıcı/depo' biçiminde olmalı (ör. Stexez/LumPanel).");
            string json;
            using (var wc = Client(token))
            {
                try { json = wc.DownloadString($"https://api.github.com/repos/{repo}/releases/latest"); }
                catch (WebException ex) when ((ex.Response as HttpWebResponse)?.StatusCode == HttpStatusCode.NotFound)
                {
                    throw new InvalidOperationException("Depo veya yayınlanmış sürüm bulunamadı (depo gizliyse erişim anahtarı gerekir).");
                }
            }
            var rel = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(json);
            string tag = rel.TryGetValue("tag_name", out var t) ? t as string ?? "" : "";
            if (!Version.TryParse(tag.TrimStart('v', 'V'), out var version)) return null;
            if (version.Build < 0) version = new Version(version.Major, version.Minor, 0);
            var cur = new Version(Current.Major, Current.Minor, Math.Max(0, Current.Build));
            if (version <= cur) return null;
            var assets = rel.TryGetValue("assets", out var a) && a is System.Collections.IList list
                ? list.OfType<Dictionary<string, object>>().ToList() : new List<Dictionary<string, object>>();
            string Url(string name) => assets.FirstOrDefault(x => string.Equals(x["name"] as string, name, StringComparison.OrdinalIgnoreCase))?["browser_download_url"] as string;
            string exe = Url("LumPanel.exe");
            if (exe == null) throw new InvalidOperationException($"{tag} sürümünde 'LumPanel.exe' dosyası yok.");
            return new Release
            {
                Version = version, Tag = tag, ExeUrl = exe, ShaUrl = Url("LumPanel.exe.sha256"),
                Notes = rel.TryGetValue("body", out var b) ? (b as string ?? "").Trim() : "",
            };
        }

        /// <summary>Downloads next to the running exe as LumPanel.exe.new and checks it.</summary>
        public static string Download(Release r, string token)
        {
            string target = Application.ExecutablePath + ".new";
            using (var wc = Client(token))
            {
                wc.Headers[HttpRequestHeader.Accept] = "application/octet-stream";
                wc.DownloadFile(r.ExeUrl, target);
                var bytes = File.ReadAllBytes(target);
                if (bytes.Length < 50_000 || bytes[0] != 'M' || bytes[1] != 'Z')
                {
                    File.Delete(target);
                    throw new InvalidOperationException("İndirilen dosya geçerli bir program değil.");
                }
                if (r.ShaUrl != null)
                {
                    wc.Headers[HttpRequestHeader.Accept] = "application/octet-stream";
                    string expected = wc.DownloadString(r.ShaUrl).Trim().Split(' ', '\t')[0].ToLowerInvariant();
                    using var sha = SHA256.Create();
                    string actual = BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();
                    if (expected.Length == 64 && expected != actual)
                    {
                        File.Delete(target);
                        throw new InvalidOperationException("İndirilen dosyanın SHA-256 özeti tutmuyor; güncelleme iptal edildi.");
                    }
                }
            }
            return target;
        }

        /// <summary>Starts a small hidden script that swaps the exe after this process exits and restarts the panel.</summary>
        public static void ScheduleSwap(string newExe)
        {
            string exe = Application.ExecutablePath;
            int pid = Process.GetCurrentProcess().Id;
            string script = Path.Combine(Path.GetTempPath(), "lumpanel-guncelle.cmd");
            File.WriteAllText(script,
                "@echo off\r\n" +
                ":wait\r\n" +
                $"tasklist /FI \"PID eq {pid}\" 2>nul | find \"{pid}\" >nul && (timeout /t 1 /nobreak >nul & goto wait)\r\n" +
                $"copy /y \"{exe}\" \"{exe}.old\" >nul\r\n" +
                $"move /y \"{newExe}\" \"{exe}\" >nul\r\n" +
                $"start \"\" \"{exe}\" --after-update\r\n" +
                "del \"%~f0\"\r\n", Encoding.ASCII);
            Process.Start(new ProcessStartInfo("cmd.exe", $"/d /c \"{script}\"") { CreateNoWindow = true, UseShellExecute = false, WindowStyle = ProcessWindowStyle.Hidden });
        }
    }
}
