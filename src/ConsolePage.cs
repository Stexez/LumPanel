using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

namespace LumPanel
{
    public class ConsolePage : Page
    {
        public override string Title => "Konsollar";

        readonly ListBox services = new ListBox
        {
            Dock = DockStyle.Left, Width = 220, BackColor = Theme.Sidebar, ForeColor = Theme.Text, BorderStyle = BorderStyle.None,
            DrawMode = DrawMode.OwnerDrawFixed, ItemHeight = 40, Font = Theme.Bold, IntegralHeight = false,
        };
        readonly RichTextBox output = new RichTextBox
        {
            Dock = DockStyle.Fill, ReadOnly = true, BackColor = Theme.Input, ForeColor = Theme.Text, BorderStyle = BorderStyle.None,
            Font = Theme.Mono, WordWrap = true, DetectUrls = false, HideSelection = false, ScrollBars = RichTextBoxScrollBars.Vertical,
        };
        readonly TextBox input = Theme.TextBox();
        readonly TextBox filter = Theme.TextBox();
        readonly CheckBox autoScroll = Theme.Check("Otomatik kaydır");
        readonly Label title = Theme.Label("", Theme.H2);
        readonly Label stateLabel = Theme.Label("", Theme.Bold);
        readonly StatusLight light = new StatusLight(20);
        readonly DarkButton toggle = DarkButton.Sized("Başlat", 90, Theme.Green);
        readonly DarkButton restart = DarkButton.Sized("↻ Yeniden başlat", 130);
        readonly DarkButton kill = DarkButton.Sized("Zorla kapat", 110);
        readonly FlowLayoutPanel quick = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 40, BackColor = Theme.Bg, Padding = new Padding(0, 6, 0, 0) };
        readonly Timer flush = new Timer { Interval = 250 };
        readonly List<string> history = new List<string>();
        int historyIndex = -1;
        ServiceRunner selected;
        long lastSeq;
        int shownLines;

        public ConsolePage(ServiceManager m, MainForm f) : base(m, f)
        {
            autoScroll.Checked = true;
            filter.Width = 120;
            var cue = Theme.Label("Ara:", Theme.Body, Theme.Muted);

            var top = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 40, BackColor = Theme.Bg, WrapContents = false };
            var tools = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 44, BackColor = Theme.Bg, WrapContents = false };
            light.Margin = new Padding(0, 10, 6, 0);
            title.Margin = new Padding(0, 6, 10, 0);
            stateLabel.Margin = new Padding(0, 10, 18, 0);
            cue.Margin = new Padding(16, 10, 4, 0);
            filter.Margin = new Padding(0, 7, 8, 0);
            autoScroll.Margin = new Padding(6, 10, 0, 0);
            var clear = DarkButton.Sized("Temizle", 84);
            var save = DarkButton.Sized("Kaydet", 80);
            var copy = DarkButton.Sized("Kopyala", 86);
            top.Controls.AddRange(new Control[] { light, title, stateLabel, autoScroll });
            tools.Controls.AddRange(new Control[] { toggle, restart, kill, cue, filter, clear, save, copy });

            var bottom = new Panel { Dock = DockStyle.Bottom, Height = 40, BackColor = Theme.Bg, Padding = new Padding(0, 8, 0, 0) };
            var send = DarkButton.Sized("Gönder ⏎", 100, Theme.Accent);
            send.Dock = DockStyle.Right;
            input.Dock = DockStyle.Fill;
            input.Font = Theme.Mono;
            var prompt = new Label { Text = "›", Dock = DockStyle.Left, Width = 18, ForeColor = Theme.Accent, Font = Theme.H2, TextAlign = ContentAlignment.MiddleCenter };
            bottom.Controls.Add(input);
            bottom.Controls.Add(prompt);
            bottom.Controls.Add(send);

            var right = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Bg, Padding = new Padding(16, 0, 0, 0) };
            right.Controls.Add(output);
            right.Controls.Add(quick);
            right.Controls.Add(bottom);
            right.Controls.Add(tools);
            right.Controls.Add(top);
            Theme.Dark(output);
            Theme.Dark(services);

            Controls.Add(right);
            Controls.Add(services);
            Controls.Add(Header("Konsollar", "Her programın canlı çıktısı. Komut yaz ve Enter'a bas; ↑/↓ ile önceki komutlar."));

            services.DrawItem += DrawService;
            services.SelectedIndexChanged += (s, e) => Select(services.SelectedItem as ServiceRunner);
            toggle.Click += (s, e) => { DashboardPage.Toggle(selected); RefreshView(); };
            restart.Click += (s, e) => selected?.Stop(restart: true);
            kill.Click += (s, e) =>
            {
                if (selected != null && MessageBox.Show(Main, $"{selected.Config.Name} zorla kapatılsın mı? (kaydedilmemiş veriler kaybolabilir)", "Zorla kapat",
                        MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes) selected.Kill();
            };
            clear.Click += (s, e) => { selected?.ClearLog(); Reload(); };
            save.Click += (s, e) => SaveLog();
            copy.Click += (s, e) => { if (output.TextLength > 0) Clipboard.SetText(output.SelectionLength > 0 ? output.SelectedText : output.Text); };
            filter.TextChanged += (s, e) => Reload();
            send.Click += (s, e) => SendInput();
            input.KeyDown += InputKeys;
            flush.Tick += (s, e) => Pump();
            flush.Start();
            ServicesChanged();
        }

        void DrawService(object sender, DrawItemEventArgs e)
        {
            if (e.Index < 0) return;
            var r = (ServiceRunner)services.Items[e.Index];
            bool sel = (e.State & DrawItemState.Selected) != 0;
            using (var bg = new SolidBrush(sel ? Theme.Card : Theme.Sidebar)) e.Graphics.FillRectangle(bg, e.Bounds);
            e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            using (var dot = new SolidBrush(Theme.StateColor(r.State))) e.Graphics.FillEllipse(dot, e.Bounds.X + 14, e.Bounds.Y + 15, 11, 11);
            TextRenderer.DrawText(e.Graphics, r.Config.Name, Theme.Bold, new Point(e.Bounds.X + 34, e.Bounds.Y + 4), Theme.Text);
            TextRenderer.DrawText(e.Graphics, Theme.StateText(r.State), Theme.Small, new Point(e.Bounds.X + 34, e.Bounds.Y + 21), Theme.Muted);
        }

        public override void ServicesChanged()
        {
            var keep = selected;
            services.BeginUpdate();
            services.Items.Clear();
            foreach (var r in Manager.Runners) services.Items.Add(r);
            services.EndUpdate();
            if (keep != null && Manager.Runners.Contains(keep)) services.SelectedItem = keep;
            else if (services.Items.Count > 0) services.SelectedIndex = 0;
            else Select(null);
        }

        public void Select(ServiceRunner r)
        {
            if (r != null && services.SelectedItem != r) { services.SelectedItem = r; return; }
            if (selected == r && r != null) return;
            selected = r;
            quick.Controls.Clear();
            if (r != null)
            {
                quick.Controls.Add(new Label { Text = "Hızlı:", AutoSize = true, ForeColor = Theme.Muted, Margin = new Padding(0, 8, 6, 0) });
                foreach (var cmd in r.Config.EffectiveQuickCommands)
                {
                    var b = new DarkButton(cmd) { AutoSize = true, Height = 28, Font = Theme.Small, Margin = new Padding(0, 0, 6, 0) };
                    string c = cmd;
                    b.Click += (s, e) => SendCommand(c);
                    quick.Controls.Add(b);
                }
            }
            Reload();
            RefreshView();
            input.Focus();
        }

        void Reload()
        {
            output.Clear();
            lastSeq = 0;
            shownLines = 0;
            Pump(full: true);
        }

        bool Matches(LogLine l) => filter.Text.Length == 0 || l.Text.IndexOf(filter.Text, StringComparison.OrdinalIgnoreCase) >= 0;

        void Pump(bool full = false)
        {
            if (selected == null || !Visible && !full) return;
            var batch = selected.LinesAfter(lastSeq);
            if (batch.Count == 0) return;
            lastSeq = batch[batch.Count - 1].Seq;
            var show = batch.Where(Matches).ToList();
            if (show.Count == 0) return;
            int max = Math.Max(500, Manager.Config.ConsoleMaxLines);
            if (show.Count > max) show = show.Skip(show.Count - max).ToList();

            var scroll = GetScroll();
            SendMessage(output.Handle, WM_SETREDRAW, IntPtr.Zero, IntPtr.Zero);
            try
            {
                if (shownLines + show.Count > max * 1.2)
                {
                    // drop the oldest lines in one go to keep the box fast
                    int drop = shownLines + show.Count - max;
                    int idx = drop < output.Lines.Length ? output.GetFirstCharIndexFromLine(Math.Min(drop, output.Lines.Length - 1)) : output.TextLength;
                    output.Select(0, Math.Max(0, idx));
                    output.ReadOnly = false;
                    output.SelectedText = "";
                    output.ReadOnly = true;
                    shownLines = Math.Max(0, shownLines - drop);
                }
                foreach (var l in show)
                {
                    output.SelectionStart = output.TextLength;
                    output.SelectionLength = 0;
                    output.SelectionColor = l.Level switch
                    {
                        LineLevel.Warn => Theme.Yellow,
                        LineLevel.Error => Theme.Red,
                        LineLevel.Input => Theme.Accent,
                        LineLevel.Panel => Theme.Blue,
                        _ => Theme.Text,
                    };
                    string prefix = l.Level == LineLevel.Panel || l.Level == LineLevel.Input ? $"[{l.Time:HH:mm:ss}] " : "";
                    output.AppendText(prefix + l.Text + "\n");
                    shownLines++;
                }
            }
            finally
            {
                SendMessage(output.Handle, WM_SETREDRAW, new IntPtr(1), IntPtr.Zero);
                if (autoScroll.Checked) { output.SelectionStart = output.TextLength; output.ScrollToCaret(); }
                else SetScroll(scroll);
                output.Invalidate();
            }
        }

        void InputKeys(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; SendInput(); }
            else if (e.KeyCode == Keys.Up && history.Count > 0)
            {
                historyIndex = historyIndex < 0 ? history.Count - 1 : Math.Max(0, historyIndex - 1);
                input.Text = history[historyIndex];
                input.SelectionStart = input.TextLength;
                e.SuppressKeyPress = true;
            }
            else if (e.KeyCode == Keys.Down && historyIndex >= 0)
            {
                historyIndex++;
                input.Text = historyIndex < history.Count ? history[historyIndex] : "";
                if (historyIndex >= history.Count) historyIndex = -1;
                input.SelectionStart = input.TextLength;
                e.SuppressKeyPress = true;
            }
        }

        void SendInput()
        {
            string cmd = input.Text.Trim();
            if (cmd.Length == 0) return;
            if (SendCommand(cmd))
            {
                history.Remove(cmd);
                history.Add(cmd);
                if (history.Count > 100) history.RemoveAt(0);
                historyIndex = -1;
                input.Clear();
            }
        }

        bool SendCommand(string cmd)
        {
            if (selected == null) return false;
            if (cmd.StartsWith("/")) cmd = cmd.Substring(1);
            string err = selected.Send(cmd);
            if (err != null) { MessageBox.Show(Main, err, selected.Config.Name, MessageBoxButtons.OK, MessageBoxIcon.Information); return false; }
            Pump();
            return true;
        }

        void SaveLog()
        {
            if (selected == null) return;
            using var dlg = new SaveFileDialog
            {
                FileName = $"{selected.Config.Name}-{DateTime.Now:yyyyMMdd-HHmm}.log".Replace(' ', '_'),
                Filter = "Log dosyası|*.log|Metin|*.txt",
            };
            if (dlg.ShowDialog(Main) != DialogResult.OK) return;
            var all = selected.LinesAfter(0).Select(l => $"{l.Time:yyyy-MM-dd HH:mm:ss} {l.Text}");
            File.WriteAllLines(dlg.FileName, all, Encoding.UTF8);
        }

        public override void RefreshView()
        {
            services.Invalidate();
            if (selected == null)
            {
                title.Text = "Servis yok";
                stateLabel.Text = "";
                toggle.Enabled = restart.Enabled = kill.Enabled = input.Enabled = false;
                return;
            }
            var r = selected;
            title.Text = r.Config.Name;
            stateLabel.Text = Theme.StateText(r.State) + (r.RootPid > 0 ? $" · PID {r.RootPid} · CPU %{r.CpuPercent:0} · {Theme.Bytes(r.MemoryBytes)}" : "");
            stateLabel.ForeColor = Theme.StateColor(r.State);
            light.SetState(r.State);
            bool on = r.IsAlive || r.State == ServiceState.External;
            toggle.Text = on ? "Durdur" : "Başlat";
            toggle.SetColor(on ? Theme.Red : Theme.Green);
            toggle.Enabled = r.State != ServiceState.Stopping;
            restart.Enabled = true;
            kill.Enabled = r.IsOwned || r.State == ServiceState.External;
            input.Enabled = r.IsOwned;
        }

        public override void OnShown()
        {
            base.OnShown();
            Pump();
            input.Focus();
        }

        // keep the user's scroll position while lines arrive and auto-scroll is off
        const int WM_SETREDRAW = 0x000B, EM_GETSCROLLPOS = 0x04DD, EM_SETSCROLLPOS = 0x04DE;
        [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr h, int msg, IntPtr w, IntPtr l);
        [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr h, int msg, IntPtr w, ref Point l);
        Point GetScroll() { var p = new Point(); SendMessage(output.Handle, EM_GETSCROLLPOS, IntPtr.Zero, ref p); return p; }
        void SetScroll(Point p) => SendMessage(output.Handle, EM_SETSCROLLPOS, IntPtr.Zero, ref p);
    }
}
