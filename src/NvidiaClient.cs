using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;

namespace LumPanel
{
    /// <summary>NVIDIA Build (integrate.api.nvidia.com) chat client: OpenAI-compatible, streamed, no dependencies.</summary>
    public static class NvidiaClient
    {
        const string Endpoint = "https://integrate.api.nvidia.com/v1/chat/completions";

        public const string DefaultModel = "nvidia/nemotron-3-ultra-550b-a55b";

        /// <summary>Suggestions for the model box; any id from build.nvidia.com can be typed in.</summary>
        public static readonly string[] Models =
        {
            DefaultModel,
            "nvidia/nemotron-3.5-lightning-30b-a3b",
            "moonshotai/kimi-k3.1-pro",
            "z-ai/glm-5.3-flash",
            "minimaxai/minimax-m3.1-flash",
            "mistralai/mistral-large-4-1t-a52b",
        };

        static readonly Regex Ip = new Regex(@"\b(?!127\.0\.0\.1\b)(?:\d{1,3}\.){3}\d{1,3}\b", RegexOptions.Compiled);

        /// <summary>Console lines carry player IP addresses; they are not sent to a third party.</summary>
        public static string HideAddresses(string text) => Ip.Replace(text ?? "", "x.x.x.x");

        /// <summary>
        /// Sends the conversation and calls <paramref name="progress"/> with the whole answer so far while it streams.
        /// History entries are {role, content}; content is a string or a list of {type:"text", text} blocks.
        /// </summary>
        public static NvidiaReply Ask(string key, string model, string system, List<Dictionary<string, object>> history, Action<string> progress)
        {
            var messages = new List<object> { new Dictionary<string, object> { ["role"] = "system", ["content"] = system } };
            foreach (var m in history)
                messages.Add(new Dictionary<string, object> { ["role"] = m["role"], ["content"] = HideAddresses(TextOf(m["content"])) });
            var json = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
            string payload = json.Serialize(new Dictionary<string, object>
            {
                ["model"] = model,
                ["messages"] = messages,
                ["max_tokens"] = 4096,
                ["temperature"] = 0.3,
                ["stream"] = true,
            });

            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            var req = (HttpWebRequest)WebRequest.Create(Endpoint);
            req.Method = "POST";
            req.ContentType = "application/json";
            req.Accept = "text/event-stream";
            req.Headers["Authorization"] = "Bearer " + key;
            req.Timeout = 120000;
            req.ReadWriteTimeout = 120000;
            var answer = new StringBuilder();
            var reply = new NvidiaReply();
            try
            {
                byte[] bytes = Encoding.UTF8.GetBytes(payload);
                using (var s = req.GetRequestStream()) s.Write(bytes, 0, bytes.Length);
                using var resp = (HttpWebResponse)req.GetResponse();
                using var reader = new StreamReader(resp.GetResponseStream(), Encoding.UTF8);
                var lastShown = DateTime.MinValue;
                string line;
                while ((line = reader.ReadLine()) != null)
                {
                    if (!line.StartsWith("data:")) continue;
                    string data = line.Substring(5).Trim();
                    if (data == "[DONE]") break;
                    var chunk = json.Deserialize<Dictionary<string, object>>(data);
                    if (chunk.TryGetValue("usage", out var u) && u is Dictionary<string, object> usage)
                    {
                        reply.InputTokens = Num(usage, "prompt_tokens");
                        reply.OutputTokens = Num(usage, "completion_tokens");
                    }
                    if (!(chunk.TryGetValue("choices", out var c) && c is IList choices && choices.Count > 0)) continue;
                    var choice = (Dictionary<string, object>)choices[0];
                    if (choice.TryGetValue("finish_reason", out var fr) && fr as string == "length") reply.Cut = true;
                    if (choice.TryGetValue("delta", out var d) && d is Dictionary<string, object> delta
                        && delta.TryGetValue("content", out var piece) && piece is string text)
                    {
                        answer.Append(text);
                        if ((DateTime.UtcNow - lastShown).TotalMilliseconds > 150)
                        {
                            lastShown = DateTime.UtcNow;
                            string visible = Visible(answer.ToString());
                            if (visible.Length > 0) progress?.Invoke(visible);
                        }
                    }
                }
            }
            catch (WebException ex) when (ex.Response is HttpWebResponse r)
            {
                int code = (int)r.StatusCode;
                string msg = code switch
                {
                    401 => "NVIDIA API anahtarı geçersiz (Ayarlar'dan silip yeniden gir).",
                    403 => "NVIDIA bu anahtara izin vermedi (anahtarın süresi veya ücretsiz hakkı bitmiş olabilir).",
                    404 => "Model bulunamadı: " + model + " (listeden başka bir model seç).",
                    429 => "NVIDIA ücretsiz katman istek sınırına takıldı; bir dakika sonra tekrar dene.",
                    _ when code >= 500 => "NVIDIA tarafında geçici bir hata var; tekrar dene ya da başka model seç.",
                    _ => "NVIDIA isteği reddetti (HTTP " + code + ").",
                };
                throw new AiException(msg, code == 429 || code >= 500);
            }
            catch (WebException ex) { throw new AiException("NVIDIA'ya bağlanılamadı: " + ex.Message, true); }
            catch (IOException ex) { throw new AiException("NVIDIA bağlantısı yarıda kesildi: " + ex.Message, true); }

            reply.Text = Visible(answer.ToString());
            if (reply.Text.Length == 0) throw new AiException("Model boş yanıt verdi; tekrar dene ya da başka model seç.");
            if (reply.Cut) reply.Text += "\n\n(yanıt uzunluk sınırında kesildi; 'devam et' yazabilirsin)";
            return reply;
        }

        /// <summary>Reasoning models may put their thinking inside think tags; only the answer is shown.</summary>
        static string Visible(string text)
        {
            text = Regex.Replace(text, @"<think>.*?</think>", "", RegexOptions.Singleline);
            int open = text.IndexOf("<think>", StringComparison.Ordinal);
            if (open >= 0) text = text.Substring(0, open);
            return text.Trim();
        }

        static string TextOf(object content)
        {
            if (content is string s) return s;
            if (content is IEnumerable list)
                return string.Join("\n", list.Cast<object>().OfType<Dictionary<string, object>>()
                    .Where(b => b.TryGetValue("text", out _)).Select(b => b["text"] as string ?? ""));
            return "";
        }

        static long Num(Dictionary<string, object> d, string k) =>
            d.TryGetValue(k, out var v) && v != null ? Convert.ToInt64(v) : 0;
    }

    public sealed class NvidiaReply
    {
        public string Text = "";
        public long InputTokens, OutputTokens;
        public bool Cut;
    }
}
