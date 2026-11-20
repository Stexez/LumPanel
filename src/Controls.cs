using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace LumPanel
{
    static class Native
    {
        public const int WM_PAINT = 0x000F, WM_NCPAINT = 0x0085;
        [DllImport("user32.dll")] public static extern IntPtr GetWindowDC(IntPtr hWnd);
        [DllImport("user32.dll")] public static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);
    }

    /// <summary>TextBox whose 1-px border is repainted dark (accent when focused) instead of the white system border.</summary>
    public class DarkTextBox : TextBox
    {
        public DarkTextBox()
        {
            BorderStyle = BorderStyle.FixedSingle;
            BackColor = Theme.Input;
            ForeColor = Theme.Text;
            Font = new Font("Segoe UI", 10f);
        }

        protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); PaintBorder(); }
        protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); PaintBorder(); }

        protected override void WndProc(ref Message m)
        {
            base.WndProc(ref m);
            if (m.Msg == Native.WM_PAINT || m.Msg == Native.WM_NCPAINT) PaintBorder();
        }

        void PaintBorder()
        {
            if (!IsHandleCreated) return;
            IntPtr dc = Native.GetWindowDC(Handle);
            if (dc == IntPtr.Zero) return;
            try
            {
                using var g = Graphics.FromHdc(dc);
                using var p = new Pen(Focused ? Theme.Accent : Theme.Border);
                g.DrawRectangle(p, 0, 0, Width - 1, Height - 1);
            }
            finally { Native.ReleaseDC(Handle, dc); }
        }
    }

    /// <summary>Dark drop-down list: owner-drawn items, repainted frame and chevron.</summary>
    public class DarkCombo : ComboBox
    {
        public DarkCombo()
        {
            DropDownStyle = ComboBoxStyle.DropDownList;
            DrawMode = DrawMode.OwnerDrawFixed;
            FlatStyle = FlatStyle.Flat;
            BackColor = Theme.Input;
            ForeColor = Theme.Text;
            Font = new Font("Segoe UI", 10f);
            ItemHeight = 22;
        }

        protected override void OnDrawItem(DrawItemEventArgs e)
        {
            bool edit = (e.State & DrawItemState.ComboBoxEdit) != 0;
            bool sel = (e.State & DrawItemState.Selected) != 0 && !edit;
            using (var bg = new SolidBrush(sel ? Theme.Accent : edit ? Theme.Input : Theme.Card)) e.Graphics.FillRectangle(bg, e.Bounds);
            if (e.Index >= 0)
            {
                var r = new Rectangle(e.Bounds.X + 6, e.Bounds.Y, e.Bounds.Width - 8, e.Bounds.Height);
                TextRenderer.DrawText(e.Graphics, GetItemText(Items[e.Index]), Font, r, sel ? Color.White : Theme.Text,
                    TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
            }
        }

        protected override void WndProc(ref Message m)
        {
            base.WndProc(ref m);
            if (m.Msg != Native.WM_PAINT || !IsHandleCreated) return;
            using var g = Graphics.FromHwnd(Handle);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            int w = SystemInformation.VerticalScrollBarWidth + 2;
            var arrow = new Rectangle(Width - w - 1, 1, w, Height - 2);
            using (var b = new SolidBrush(Theme.Input)) g.FillRectangle(b, arrow);
            int cx = arrow.X + arrow.Width / 2, cy = Height / 2;
            using (var p = new Pen(Focused || DroppedDown ? Theme.Accent : Theme.Muted, 1.8f))
                g.DrawLines(p, new[] { new Point(cx - 4, cy - 2), new Point(cx, cy + 2), new Point(cx + 4, cy - 2) });
            g.SmoothingMode = SmoothingMode.None;
            using (var p = new Pen(Theme.Input, 2)) g.DrawRectangle(p, 1, 1, Width - 3, Height - 3);
            using (var p = new Pen(Focused || DroppedDown ? Theme.Accent : Theme.Border)) g.DrawRectangle(p, 0, 0, Width - 1, Height - 1);
        }

        protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
        protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }
    }

    /// <summary>Modern check box: rounded square, accent fill, white tick.</summary>
    public class DarkCheck : CheckBox
    {
        bool hover;
        public DarkCheck()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
            ForeColor = Theme.Text;
            Font = Theme.Body;
            Cursor = Cursors.Hand;
            AutoSize = true;
            Padding = new Padding(0, 2, 0, 2);
        }

        public override Size GetPreferredSize(Size proposed)
        {
            var t = TextRenderer.MeasureText(Text, Font);
            return new Size(t.Width + 30, Math.Max(22, t.Height + 4));
        }

        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            var bg = Parent is Card c ? c.Fill : Parent?.BackColor ?? Theme.Bg;
            if (bg == Color.Transparent) bg = Theme.Card;
            using (var b = new SolidBrush(bg)) g.FillRectangle(b, ClientRectangle);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var box = new Rectangle(1, (Height - 18) / 2, 18, 18);
            using (var path = Card.Round(box, 5))
            {
                if (Checked) using (var f = new SolidBrush(Theme.Accent)) g.FillPath(f, path);
                else using (var f = new SolidBrush(Theme.Input)) g.FillPath(f, path);
                using var p = new Pen(Checked ? Theme.Accent : hover ? Theme.Accent : Theme.Border, 1.4f);
                g.DrawPath(p, path);
            }
            if (Checked)
                using (var p = new Pen(Color.White, 2.2f) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
                    g.DrawLines(p, new[] { new Point(box.X + 4, box.Y + 9), new Point(box.X + 8, box.Y + 13), new Point(box.X + 14, box.Y + 5) });
            TextRenderer.DrawText(g, Text, Font, new Rectangle(26, 0, Width - 26, Height), Enabled ? ForeColor : Theme.Muted,
                TextFormatFlags.VerticalCenter | TextFormatFlags.Left);
        }
    }

    /// <summary>Number box without the white spinner: mouse wheel / arrow keys change the value.</summary>
    public class DarkNumber : NumericUpDown
    {
        public DarkNumber()
        {
            BorderStyle = BorderStyle.None;
            BackColor = Theme.Input;
            ForeColor = Theme.Text;
            Font = new Font("Segoe UI", 10f);
            TextAlign = HorizontalAlignment.Center;
            if (Controls.Count > 0) Controls[0].Visible = false;   // the spin buttons
            Padding = new Padding(4);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            using (var b = new SolidBrush(Theme.Input)) e.Graphics.FillRectangle(b, ClientRectangle);
            using var p = new Pen(Focused || ContainsFocus ? Theme.Accent : Theme.Border);
            e.Graphics.DrawRectangle(p, 0, 0, Width - 1, Height - 1);
        }

        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e);
            if (Controls.Count > 0) { Controls[0].Visible = false; Controls[0].Width = 0; }
            if (Controls.Count > 1)
            {
                Controls[1].BackColor = Theme.Input;
                Controls[1].SetBounds(4, (Height - Controls[1].Height) / 2, Width - 8, Controls[1].Height);
            }
        }

        protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
        protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }
    }

    /// <summary>One chat message as a rounded bubble.</summary>
    public class ChatBubble : Control
    {
        public enum Kind { User, Assistant, Error, System }
        static readonly Font HeadFont = new Font("Segoe UI Semibold", 8.5f);
        static readonly Font BodyFont = new Font("Segoe UI", 10f);
        public string Head;
        public Kind Role;
        readonly ContextMenuStrip menu = new ContextMenuStrip();

        public ChatBubble(string head, string text, Kind role)
        {
            Head = head; Text = text; Role = role;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            Margin = new Padding(0, 0, 0, 12);
            menu.Items.Add("Kopyala", null, (s, e) => { try { Clipboard.SetText(Text); } catch { } });
            ContextMenuStrip = menu;
        }

        int BubbleWidth(int avail) => Role == Kind.User ? Math.Min(avail - 60, Math.Max(160, Measure(avail - 60).Width + 30)) : avail - 40;

        Size Measure(int width) => TextRenderer.MeasureText(Text, BodyFont, new Size(Math.Max(60, width - 30), int.MaxValue),
            TextFormatFlags.WordBreak | TextFormatFlags.TextBoxControl);

        /// <summary>Lays the bubble out for the available width and returns its height.</summary>
        public void Fit(int avail)
        {
            Width = Math.Max(120, avail);
            int bw = BubbleWidth(Width);
            var size = Measure(bw);
            Height = size.Height + 46;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Parent?.BackColor ?? Theme.Input);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            int bw = BubbleWidth(Width);
            int x = Role == Kind.User ? Width - bw - 2 : 2;
            var r = new Rectangle(x, 0, bw, Height - 2);
            Color fill = Role switch
            {
                Kind.User => Color.FromArgb(74, 52, 120),
                Kind.Error => Color.FromArgb(58, 26, 30),
                Kind.System => Color.FromArgb(40, 38, 28),
                _ => Theme.CardHi,
            };
            Color edge = Role switch { Kind.User => Theme.Accent, Kind.Error => Theme.Red, Kind.System => Theme.Yellow, _ => Theme.Green };
            using (var path = Card.Round(r, 12))
            {
                using (var b = new SolidBrush(fill)) g.FillPath(b, path);
                using (var p = new Pen(Color.FromArgb(110, edge))) g.DrawPath(p, path);
            }
            TextRenderer.DrawText(g, Head, HeadFont, new Point(r.X + 14, r.Y + 9), edge);
            TextRenderer.DrawText(g, Text, BodyFont, new Rectangle(r.X + 14, r.Y + 28, r.Width - 28, r.Height - 32), Theme.Text,
                TextFormatFlags.WordBreak | TextFormatFlags.TextBoxControl);
        }
    }
}
