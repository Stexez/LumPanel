using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Web.Script.Serialization;

namespace LumPanel
{
    /// <summary>
    /// Talks to Claude through the locally installed Claude Code CLI (headless "claude -p"), so usage comes out of the
    /// user's own Claude plan instead of a paid API key. Tools are disabled: console text is only ever read, never acted on.
    /// </summary>
    public static class ClaudeCodeBackend
    {
        public const string InstallHint =
            "VDS'te PowerShell'i aç ve şunu çalıştır:  irm https://claude.ai/install.ps1 | iex\n" +
            "Sonra bir kez  claude  yazıp kendi Claude hesabınla giriş yap. Panel bundan sonra planını kullanır (ek ücret yok).";

        static string cached;

        /// <summary>Finds claude.exe / claude.cmd (native installer or npm).</summary>
        public static string Find(string configured = null)
        {
            if (!string.IsNullOrWhiteSpace(configured) && File.Exists(configured)) return configured;
            if (cached != null && File.Exists(cached)) return cached;
            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var candidates = new List<string>
            {
                Path.Combine(home, @".local\bin\claude.exe"),
                Path.Combine(appData, @"npm\claude.cmd"),
            };
            foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(';').Where(d => d.Trim().Length > 0))
            {
                candidates.Add(Path.Combine(dir.Trim(), "claude.exe"));
                candidates.Add(Path.Combine(dir.Trim(), "claude.cmd"));
            }
            return cached = candidates.FirstOrDefault(File.Exists);
        }

        public sealed class Result
        {
            public string Text;
            public string SessionId;
            public long InputTokens, OutputTokens;
        }

        /// <summary>One headless turn. Pass the previous session id to continue the same conversation.</summary>
        public static Result Ask(string exe, string model, string systemPrompt, string prompt, string resumeSession)
        {
            if (exe == null) throw new AiException("Claude Code bu bilgisayarda bulunamadı.\n" + InstallHint);
            string work = Path.Combine(Path.GetTempPath(), "LumPanel-ai");   // empty folder: no project files, no CLAUDE.md
            Directory.CreateDirectory(work);
            string sysFile = Path.Combine(work, "system.txt");
            File.WriteAllText(sysFile, systemPrompt, new UTF8Encoding(false));

            var args = new StringBuilder();
            args.Append("-p --output-format json --tools \"\" --strict-mcp-config --effort low");
            args.Append(" --model ").Append(string.IsNullOrWhiteSpace(model) ? "haiku" : model);
            args.Append(" --system-prompt-file \"").Append(sysFile).Append('"');
            if (!string.IsNullOrEmpty(resumeSession)) args.Append(" --resume ").Append(resumeSession);

            var psi = new ProcessStartInfo
            {
                UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = work,
                RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8,
            };
            if (exe.EndsWith(".cmd", StringComparison.OrdinalIgnoreCase))
            {
                psi.FileName = Environment.GetEnvironmentVariable("ComSpec") ?? "cmd.exe";
                psi.Arguments = $"/d /c \"\"{exe}\" {args}\"";
            }
            else
            {
                psi.FileName = exe;
                psi.Arguments = args.ToString();
            }

            using var p = Process.Start(psi);
            var stdin = p.StandardInput.BaseStream;
            byte[] bytes = new UTF8Encoding(false).GetBytes(prompt);
            stdin.Write(bytes, 0, bytes.Length);
            stdin.Close();
            var outTask = p.StandardOutput.ReadToEndAsync();
            var errTask = p.StandardError.ReadToEndAsync();
            if (!p.WaitForExit(180000))
            {
                try { ServiceRunner.KillTree(p.Id); } catch { }
                throw new AiException("Claude Code 3 dakikada yanıt vermedi.", true);
            }
            string stdout = outTask.Result, stderr = errTask.Result;

            Dictionary<string, object> json = null;
            try { json = new JavaScriptSerializer { MaxJsonLength = int.MaxValue }.Deserialize<Dictionary<string, object>>(stdout.Trim()); }
            catch { }
            if (json == null)
            {
                string all = (stdout + "\n" + stderr).Trim();
                if (all.IndexOf("login", StringComparison.OrdinalIgnoreCase) >= 0 || all.IndexOf("auth", StringComparison.OrdinalIgnoreCase) >= 0)
                    throw new AiException("Claude Code'a giriş yapılmamış. VDS'te bir kez 'claude' yazıp hesabınla giriş yap.");
                throw new AiException("Claude Code yanıtı okunamadı:\n" + (all.Length > 400 ? all.Substring(0, 400) : all));
            }
            string text = json.TryGetValue("result", out var r) ? r as string ?? "" : "";
            bool isError = json.TryGetValue("is_error", out var ie) && ie is bool b && b;
            if (isError)
            {
                if (text.IndexOf("limit", StringComparison.OrdinalIgnoreCase) >= 0)
                    throw new AiException("Claude planının kullanım limiti doldu; limit yenilenince tekrar dene.\n" + text);
                if (text.IndexOf("log", StringComparison.OrdinalIgnoreCase) >= 0 && text.IndexOf("in", StringComparison.OrdinalIgnoreCase) >= 0)
                    throw new AiException("Claude Code'a giriş yapılmamış. VDS'te bir kez 'claude' yazıp giriş yap.\n" + text);
                throw new AiException("Claude Code hata verdi: " + text, true);
            }
            var res = new Result { Text = text.Trim(), SessionId = json.TryGetValue("session_id", out var sid) ? sid as string : null };
            if (json.TryGetValue("usage", out var u) && u is Dictionary<string, object> usage)
            {
                long n(string k) => usage.TryGetValue(k, out var v) && v != null ? Convert.ToInt64(v) : 0;
                res.InputTokens = n("input_tokens") + n("cache_read_input_tokens") + n("cache_creation_input_tokens");
                res.OutputTokens = n("output_tokens");
            }
            return res;
        }
    }
}
