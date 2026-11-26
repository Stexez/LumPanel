using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;

namespace LumPanel
{
    public class AiPage : Page
    {
        public override string Title => "Yapay Zeka";

        sealed class ErrorItem
        {
            public DateTime Time;
            public ServiceRunner Runner;
            public LogLine Line;
            public string Signature;
            public int Count = 1;
            public string Context = "";
            public bool Explained;
        }

        static readonly Regex Volatile = new Regex(@"\d+|0x[0-9a-f]+|\[[^\]]*\]", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        readonly AiClient ai = new AiClient();
        readonly List<ErrorItem> errors = new List<ErrorItem>();
        readonly List<Dictionary<string, object>> history = new List<Dictionary<string, object>>();
        readonly Dictionary<ServiceRunner, DateTime> lastCapture = new Dictionary<ServiceRunner, DateTime>();
        readonly HashSet<ServiceRunner> hooked = new HashSet<ServiceRunner>();
        int autoToday;
        string sessionId;   // Claude Code conversation to resume
        bool UseNvidia => Manager.Config.AiBackend == "nvidia";
        bool UseClaudeCode => !UseNvidia && Manager.Config.AiBackend != "api";
        string NvidiaKey => AiClient.Unprotect(Manager.Config.NvidiaKeyProtected);
        readonly ComboBox modelBox = Theme.Combo();
        DateTime autoDay = DateTime.Today;
        bool busy;

        readonly ListView errorList = Theme.List();
        readonly CheckBox autoExplain = Theme.Check("Yeni hataları otomatik açıkla");
        readonly Label usage = Theme.Label("", Theme.Small, Theme.Muted);
        readonly FlowLayoutPanel chat = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill, BackColor = Theme.Input, FlowDirection = FlowDirection.TopDown, WrapContents = false,
            AutoScroll = true, Padding = new Padding(14, 14, 6, 14),
        };
        ChatBubble typing;
        readonly TextBox input = Theme.TextBox();
        readonly CheckBox withContext = Theme.Check("Konsolun son satırlarını ekle:");
        readonly ComboBox contextService = Theme.Combo();
        readonly DarkButton send = DarkButton.Sized("Sor ⏎", 96, Theme.Accent);
        readonly Card keyCard = new Card { Dock = DockStyle.Top, Height = 132, Fill = Color.FromArgb(46, 32, 76), FillTo = Theme.Card, Accent = Theme.Accent };
        readonly TextBox keyBox = Theme.TextBox();
        readonly Label claudeHint = new Label { AutoSize = true, MaximumSize = new Size(620, 0), Font = Theme.Body, ForeColor = Theme.Text, BackColor = Color.Transparent };

        public AiPage(ServiceManager m, MainForm f) : base(m, f)
        {
            ai.Model = string.IsNullOrWhiteSpace(m.Config.AiModel) ? "claude-haiku-5-5" : m.Config.AiModel;
            ai.SetKey(AiClient.Unprotect(m.Config.AiKeyProtected));
            autoExplain.Checked = m.Config.AiAutoExplain;

            // ---- left: captured errors
            errorList.Columns.Add("Saat", 64);
            errorList.Columns.Add("Servis", 96);
            errorList.Columns.Add("Hata", 200);
            errorList.Dock = DockStyle.Fill;
            var left = new Card { Dock = DockStyle.Left, Width = 400, Padding = new Padding(14), FillTo = Color.FromArgb(24, 22, 33) };
            var leftTitle = new Label { Text = "YAKALANAN HATALAR", Dock = DockStyle.Top, Height = 26, ForeColor = Theme.Muted, Font = new Font("Segoe UI Semibold", 8f), BackColor = Color.Transparent };
            var leftBottom = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 104, BackColor = Theme.Card, FlowDirection = FlowDirection.TopDown, WrapContents = false, Padding = new Padding(0, 8, 0, 0) };
            var explain = DarkButton.Sized("✦ Seçili hatayı açıkla", 190, Theme.Accent);
            var clear = DarkButton.Sized("Temizle", 90);
            var row = new FlowLayoutPanel { AutoSize = true, BackColor = Theme.Card, Margin = Padding.Empty };
            row.Controls.Add(explain);
            row.Controls.Add(clear);
            leftBottom.Controls.Add(row);
            leftBottom.Controls.Add(autoExplain);
            leftBottom.Controls.Add(usage);
            autoExplain.Margin = new Padding(0, 8, 0, 0);
            usage.Margin = new Padding(0, 4, 0, 0);
            left.Controls.Add(errorList);
            left.Controls.Add(leftBottom);
            left.Controls.Add(leftTitle);

            // ---- right: chat
            var right = new Card { Dock = DockStyle.Fill, Padding = new Padding(16), FillTo = Color.FromArgb(24, 22, 33) };
            var composer = new Panel { Dock = DockStyle.Bottom, Height = 118, BackColor = Theme.Card, Padding = new Padding(0, 10, 0, 0) };
            var ctxRow = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 34, BackColor = Theme.Card, WrapContents = false };
            contextService.Width = 200;
            withContext.Margin = new Padding(0, 7, 6, 0);
            var newChat = DarkButton.Sized("Yeni sohbet", 110);
            newChat.Margin = new Padding(16, 0, 0, 0);
            ctxRow.Controls.AddRange(new Control[] { withContext, contextService, newChat });
            modelBox.Width = 320;
            modelBox.Margin = new Padding(0);
            modelBox.DropDownStyle = ComboBoxStyle.DropDown;  // any build.nvidia.com id can be typed
            modelBox.Items.AddRange(NvidiaClient.Models);
            modelBox.Text = string.IsNullOrWhiteSpace(m.Config.NvidiaModel) ? NvidiaClient.DefaultModel : m.Config.NvidiaModel;
            modelBox.Visible = UseNvidia;
            modelBox.TextChanged += (s, e) => { if (modelBox.Text.Trim().Length > 3) Manager.Config.NvidiaModel = modelBox.Text.Trim(); };
            modelBox.Leave += (s, e) => SaveConfig();
            modelBox.SelectionChangeCommitted += (s, e) => BeginInvoke(new Action(SaveConfig));
            // its own row: next to the context controls it does not fit and gets cut off
            var modelRow = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 34, BackColor = Theme.Card, WrapContents = false, Visible = UseNvidia };
            modelRow.Controls.Add(new Label { Text = "Model:", AutoSize = true, ForeColor = Theme.Muted, Margin = new Padding(0, 6, 8, 0) });
            modelRow.Controls.Add(modelBox);
            if (UseNvidia) composer.Height += modelRow.Height;
            input.Multiline = true;
            input.Dock = DockStyle.Fill;
            input.Font = new Font("Segoe UI", 10f);
            send.Dock = DockStyle.Right;
            send.Height = 64;
            var inputRow = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Card, Padding = new Padding(0, 6, 0, 0) };
            inputRow.Controls.Add(input);
            inputRow.Controls.Add(new Panel { Dock = DockStyle.Right, Width = 8, BackColor = Theme.Card });
            inputRow.Controls.Add(send);
            composer.Controls.Add(inputRow);
            composer.Controls.Add(ctxRow);
            composer.Controls.Add(modelRow);
            right.Controls.Add(chat);
            right.Controls.Add(composer);
            right.Controls.Add(BuildKeyCard());
            Theme.Dark(chat);
            chat.Resize += (s, e) => { foreach (var b in chat.Controls.OfType<ChatBubble>()) b.Fit(ChatWidth); };

            var gap = new Panel { Dock = DockStyle.Left, Width = 14, BackColor = Theme.Bg };
            Controls.Add(right);
            Controls.Add(gap);
            Controls.Add(left);
            Controls.Add(Header("Yapay Zeka Asistanı",
                (UseNvidia ? "NVIDIA ücretsiz katmanıyla çalışır (dakikalık istek sınırı var). " : UseClaudeCode ? "Claude Code ile kendi planından çalışır (ek ücret yok). " : "API anahtarıyla çalışır (kullanım başına ücretli). ") +
                "Konsol hatalarını yakalar, nasıl düzeltileceğini anlatır; aşağıdan soru da sorabilirsin."));

            explain.Click += (s, e) => { if (errorList.SelectedItems.Count > 0) Explain((ErrorItem)errorList.SelectedItems[0].Tag, manual: true); };
            errorList.DoubleClick += (s, e) => { if (errorList.SelectedItems.Count > 0) Explain((ErrorItem)errorList.SelectedItems[0].Tag, manual: true); };
            clear.Click += (s, e) => { errors.Clear(); FillErrors(); };
            autoExplain.CheckedChanged += (s, e) => { Manager.Config.AiAutoExplain = autoExplain.Checked; SaveConfig(); };
            send.Click += (s, e) => Ask();
            input.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter && !e.Shift) { e.SuppressKeyPress = true; Ask(); } };
            newChat.Click += (s, e) => { history.Clear(); sessionId = null; ClearChat(); Welcome(); };

            ServicesChanged();
            Welcome();
            UpdateKeyCard();
        }

        Control BuildKeyCard()
        {
            keyBox.Width = 420;
            keyBox.UseSystemPasswordChar = true;
            var save = DarkButton.Sized("Anahtarı kaydet", 140, Theme.Accent);
            var help = new LinkLabel { Text = UseNvidia ? "Ücretsiz anahtar al: build.nvidia.com → API Keys" : "Anahtar al: console.anthropic.com → API Keys", AutoSize = true, LinkColor = Theme.Accent, BackColor = Color.Transparent, Location = new Point(18, 98) };
            var title = new Label { Text = UseNvidia ? "NVIDIA API anahtarı gerekli" : UseClaudeCode ? "Claude Code kurulu değil" : "Anthropic API anahtarı gerekli", Font = Theme.H2, ForeColor = Theme.Text, AutoSize = true, Location = new Point(16, 12), BackColor = Color.Transparent, Tag = "title" };
            claudeHint.Text = ClaudeCodeBackend.InstallHint;
            claudeHint.Location = new Point(18, 42);
            keyCard.Controls.Add(claudeHint);
            var note = new Label
            {
                Text = UseNvidia ? "Anahtar bu bilgisayarda Windows şifrelemesiyle saklanır (nvapi- ile başlar)." : "Anahtar bu bilgisayarda Windows şifrelemesiyle saklanır. Model: en ucuz Claude (Haiku).",
                Font = Theme.Small, ForeColor = Theme.Muted, AutoSize = true, MaximumSize = new Size(560, 0), Location = new Point(18, 40), BackColor = Color.Transparent,
            };
            keyBox.Location = new Point(18, 64);
            save.Location = new Point(446, 60);
            keyCard.Controls.AddRange(new Control[] { title, note, keyBox, save, help });
            save.Click += (s, e) =>
            {
                string key = keyBox.Text.Trim();
                string prefix = UseNvidia ? "nvapi-" : "sk-ant-";
                if (!key.StartsWith(prefix)) { MessageBox.Show(Main, "Anahtar '" + prefix + "' ile başlamalı.", "Yapay Zeka"); return; }
                if (UseNvidia) Manager.Config.NvidiaKeyProtected = AiClient.Protect(key);
                else { Manager.Config.AiKeyProtected = AiClient.Protect(key); ai.SetKey(key); }
                SaveConfig();
                keyBox.Clear();
                UpdateKeyCard();
                AddChat("Panel", "Anahtar kaydedildi. Artık hataları açıklayabilir ve sorularını yanıtlayabilirim.", Theme.Blue);
            };
            help.LinkClicked += (s, e) => { try { Process.Start(UseNvidia ? "https://build.nvidia.com/settings/api-keys" : "https://console.anthropic.com/settings/keys"); } catch { } };
            return keyCard;
        }

        bool Ready => UseNvidia ? NvidiaKey.Length > 0 : UseClaudeCode ? ClaudeCodeBackend.Find(Manager.Config.AiClaudePath) != null : ai.HasKey;

        string NotReadyText => UseClaudeCode ? ClaudeCodeBackend.InstallHint : "Önce API anahtarını gir.";

        /// <summary>Base prompt plus a live snapshot of the services, so "why is X down?" can be answered.</summary>
        string SystemWithStatus() =>
            AiClient.SystemPrompt + "\n\nPaneldeki servislerin şu anki durumu (" + DateTime.Now.ToString("dd.MM.yyyy HH:mm") + "):\n" +
            string.Join("\n", Manager.Runners.Select(r => "- " + r.Config.Name + " (" + ServiceKinds.Label(r.Config.Kind) + "): " + r.State));

        void UpdateKeyCard()
        {
            keyCard.Visible = !Ready;
            foreach (Control c in keyCard.Controls) if (!(c is Label) || c.Tag as string == "title") c.Visible = !UseClaudeCode || c.Tag as string == "title";
            claudeHint.Visible = UseClaudeCode;
        }

        void SaveConfig() { try { Manager.Config.Save(); } catch { } }

        void Welcome()
        {
            AddChat("Asistan",
                "Merhaba! Konsollarda bir hata (ERROR / Exception) çıkınca soldaki listeye düşer; istersen otomatik açıklarım. " +
                "Bana sunucuyla ilgili her şeyi sorabilirsin, örneğin: \"LumSMP neden geç açılıyor?\" ya da \"Paper'da TPS düşükse ne yapmalıyım?\". " +
                "Konsolun son satırlarını eklersem neye baktığımı daha iyi anlarım.", Theme.Green);
        }

        // ------------------------------------------------------------------ errors
        public override void ServicesChanged()
        {
            foreach (var r in Manager.Runners.Where(r => hooked.Add(r)))
                r.ErrorLine += (runner, line) => { if (Main.IsHandleCreated) Main.BeginInvoke(new Action(() => Capture(runner, line))); };
            var keep = contextService.SelectedItem;
            contextService.Items.Clear();
            foreach (var r in Manager.Runners) contextService.Items.Add(r);
            if (keep != null && contextService.Items.Contains(keep)) contextService.SelectedItem = keep;
            else if (contextService.Items.Count > 0) contextService.SelectedIndex = 0;
        }

        void Capture(ServiceRunner r, LogLine line)
        {
            // lines of one stack trace arrive together: treat them as one error
            if (lastCapture.TryGetValue(r, out var last) && DateTime.Now - last < TimeSpan.FromSeconds(8)) return;
            lastCapture[r] = DateTime.Now;
            string sig = r.Config.Id + ":" + Volatile.Replace(line.Text, "#");
            var same = errors.FirstOrDefault(x => x.Signature == sig && DateTime.Now - x.Time < TimeSpan.FromMinutes(30));
            if (same != null) { same.Count++; FillErrors(); return; }
            var item = new ErrorItem { Time = DateTime.Now, Runner = r, Line = line, Signature = sig };
            errors.Insert(0, item);
            if (errors.Count > 200) errors.RemoveAt(errors.Count - 1);
            FillErrors();
            // wait for the rest of the stack trace, then take the surrounding lines as context
            var timer = new System.Windows.Forms.Timer { Interval = 2500 };
            timer.Tick += (s, e) =>
            {
                timer.Dispose();
                item.Context = ContextFor(r, line.Seq - 15, 45);
                if (autoExplain.Checked && Ready) Explain(item, manual: false);
            };
            timer.Start();
        }

        static string ContextFor(ServiceRunner r, long afterSeq, int max)
        {
            var lines = r.LinesAfter(Math.Max(0, afterSeq)).Where(l => l.Level != LineLevel.Panel).Take(max)
                .Select(l => l.Text.Length > 300 ? l.Text.Substring(0, 300) + "…" : l.Text);
            string text = string.Join("\n", lines);
            return text.Length > 6000 ? text.Substring(text.Length - 6000) : text;
        }

        void FillErrors()
        {
            errorList.BeginUpdate();
            errorList.Items.Clear();
            foreach (var e in errors)
            {
                var it = new ListViewItem(e.Time.ToString("HH:mm:ss")) { Tag = e, ForeColor = e.Explained ? Theme.Muted : Theme.Red };
                it.SubItems.Add(e.Runner.Config.Name);
                it.SubItems.Add((e.Count > 1 ? $"({e.Count}×) " : "") + e.Line.Text);
                errorList.Items.Add(it);
            }
            errorList.EndUpdate();
        }

        void Explain(ErrorItem item, bool manual)
        {
            if (!Ready) { MessageBox.Show(Main, NotReadyText, "Yapay Zeka"); return; }
            if (!manual)
            {
                if (autoDay != DateTime.Today) { autoDay = DateTime.Today; autoToday = 0; }
                // automatic explanations may use at most half of the daily budget
                if (busy || autoToday >= Math.Max(1, Manager.Config.AiDailyLimit / 2)) return;
                autoToday++;
            }
            if (string.IsNullOrEmpty(item.Context)) item.Context = ContextFor(item.Runner, item.Line.Seq - 15, 45);
            item.Explained = true;
            FillErrors();
            string prompt =
                $"'{item.Runner.Config.Name}' ({ServiceKinds.Label(item.Runner.Config.Kind)}) konsolunda bir hata oluştu" +
                (item.Count > 1 ? $" ({item.Count} kez tekrarlandı)" : "") + ". Konsol satırları:\n<konsol>\n" + item.Context +
                "\n</konsol>\nBu hata ne, neden oluyor ve nasıl düzeltilir?";
            AddChat("Hata · " + item.Runner.Config.Name, item.Line.Text, Theme.Red);
            Request(prompt);
        }

        // ------------------------------------------------------------------ chat
        void Ask()
        {
            string q = input.Text.Trim();
            if (q.Length == 0 || busy) return;
            if (!Ready) { MessageBox.Show(Main, NotReadyText, "Yapay Zeka"); return; }
            input.Clear();
            AddChat("Sen", q, Theme.Accent);
            string prompt = q;
            if (withContext.Checked && contextService.SelectedItem is ServiceRunner r)
            {
                var all = r.LinesAfter(0);
                long from = all.Count > 30 ? all[all.Count - 31].Seq : 0;
                prompt = $"Soru: {q}\n\n'{r.Config.Name}' konsolunun son satırları:\n<konsol>\n{ContextFor(r, from, 30)}\n</konsol>";
            }
            Request(prompt);
        }

        void Request(string prompt)
        {
            if (!ai.UnderLimit(Manager.Config.AiDailyLimit))
            {
                AddChat("Panel", $"Günlük istek sınırına ulaşıldı ({Manager.Config.AiDailyLimit}). Ayarlar'dan artırabilirsin.", Theme.Yellow);
                return;
            }
            busy = true;
            send.Enabled = false;
            history.Add(new Dictionary<string, object> { ["role"] = "user", ["content"] = prompt });
            TrimHistory();
            var snapshot = history.ToList();
            AddChat("Asistan", "yazıyor…", Theme.Muted);
            bool nvidia = UseNvidia;
            string nvidiaKey = nvidia ? NvidiaKey : "";
            string nvidiaModel = Manager.Config.NvidiaModel;
            string system = nvidia ? SystemWithStatus() : AiClient.SystemPrompt;
            ThreadPool.QueueUserWorkItem(_ =>
            {
                AiReply reply = null;
                string error = null;
                try
                {
                    if (nvidia)
                    {
                        var n = NvidiaClient.Ask(nvidiaKey, nvidiaModel, system, snapshot,
                            partial => Main.BeginInvoke(new Action(() => ShowPartial(Clean(partial)))));
                        ai.Record(n.InputTokens, n.OutputTokens);
                        reply = new AiReply { Text = n.Text, Content = new List<object> { new Dictionary<string, object> { ["type"] = "text", ["text"] = n.Text } } };
                    }
                    else if (UseClaudeCode)
                    {
                        var r = ClaudeCodeBackend.Ask(ClaudeCodeBackend.Find(Manager.Config.AiClaudePath), "haiku", AiClient.SystemPrompt, prompt, sessionId);
                        sessionId = r.SessionId ?? sessionId;
                        ai.Record(r.InputTokens, r.OutputTokens);
                        reply = new AiReply { Text = r.Text, Content = new List<object> { new Dictionary<string, object> { ["type"] = "text", ["text"] = r.Text } } };
                    }
                    else reply = ai.Send(snapshot);
                }
                catch (AiException ex) { error = ex.Message; }
                catch (Exception ex) { error = ex.Message; }
                Main.BeginInvoke(new Action(() =>
                {
                    RemoveTyping();
                    if (reply != null)
                    {
                        history.Add(new Dictionary<string, object> { ["role"] = "assistant", ["content"] = reply.Content });
                        AddChat("Asistan", Clean(reply.Text), Theme.Green);
                    }
                    else
                    {
                        history.RemoveAt(history.Count - 1);  // keep user/assistant alternation
                        AddChat("Panel", error, Theme.Yellow);
                    }
                    busy = false;
                    send.Enabled = true;
                    UpdateUsage();
                }));
            });
        }

        /// <summary>Keep the last few turns only (cheaper), always starting with a user message.</summary>
        void TrimHistory()
        {
            while (history.Count > 10) history.RemoveRange(0, 2);
            while (history.Count > 0 && (string)history[0]["role"] != "user") history.RemoveAt(0);
        }

        static string Clean(string text) =>
            Regex.Replace(Regex.Replace(text ?? "", @"\*\*|__|^#{1,6}\s*", "", RegexOptions.Multiline), @"\n{3,}", "\n\n").Trim();

        int ChatWidth => Math.Max(200, chat.ClientSize.Width - chat.Padding.Horizontal - 4);

        void AddChat(string who, string text, Color color)
        {
            var kind = who == "Sen" ? ChatBubble.Kind.User
                : who.StartsWith("Hata") ? ChatBubble.Kind.Error
                : who == "Panel" ? ChatBubble.Kind.System : ChatBubble.Kind.Assistant;
            var bubble = new ChatBubble($"{who}  ·  {DateTime.Now:HH:mm}", text, kind);
            bubble.Fit(ChatWidth);
            if (text == "yazıyor…") typing = bubble;
            chat.Controls.Add(bubble);
            while (chat.Controls.Count > 120) chat.Controls[0].Dispose();
            chat.ScrollControlIntoView(bubble);
        }

        /// <summary>Streams the answer into the "yazıyor…" bubble.</summary>
        void ShowPartial(string text)
        {
            if (typing == null || typing.IsDisposed) return;
            typing.Text = text;
            typing.Fit(ChatWidth);
            chat.ScrollControlIntoView(typing);
        }

        void RemoveTyping()
        {
            if (typing == null) return;
            chat.Controls.Remove(typing);
            typing.Dispose();
            typing = null;
        }

        void ClearChat()
        {
            foreach (var c in chat.Controls.OfType<Control>().ToList()) c.Dispose();
            chat.Controls.Clear();
        }

        void UpdateUsage()
        {
            usage.Text = !Ready ? (UseClaudeCode ? "Claude Code bulunamadı." : "API anahtarı girilmedi.")
                : UseNvidia ? $"Bugün {ai.RequestsToday}/{Manager.Config.AiDailyLimit} istek · NVIDIA ücretsiz katman"
                : UseClaudeCode ? $"Bugün {ai.RequestsToday}/{Manager.Config.AiDailyLimit} istek · {ai.InputTokensToday + ai.OutputTokensToday:N0} token · Claude planından (ek ücret yok)"
                : $"Bugün {ai.RequestsToday}/{Manager.Config.AiDailyLimit} istek · {ai.InputTokensToday + ai.OutputTokensToday:N0} token · ≈ ${ai.CostToday:0.0000}";
        }

        public override void RefreshView() => UpdateUsage();
    }
}
