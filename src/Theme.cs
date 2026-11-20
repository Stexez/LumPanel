using System;
using System.Linq;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace LumPanel
{
    public static class Theme
    {
        [System.Runtime.InteropServices.DllImport("uxtheme.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        static extern int SetWindowTheme(IntPtr hwnd, string app, string idList);

        /// <summary>Dark native scrollbars (Windows 10 1809+); harmless elsewhere.</summary>
        public static T Dark<T>(T c) where T : Control
        {
            void apply() { try { SetWindowTheme(c.Handle, "DarkMode_Explorer", null); } catch { } }
            if (c.IsHandleCreated) apply(); else c.HandleCreated += (s, e) => apply();
            return c;
        }

        public static readonly Color Bg = Color.FromArgb(19, 19, 25);
        public static readonly Color Sidebar = Color.FromArgb(14, 13, 20);
        public static readonly Color Card = Color.FromArgb(31, 30, 41);
        public static readonly Color CardHi = Color.FromArgb(42, 40, 56);
        public static readonly Color Input = Color.FromArgb(12, 12, 17);
        public static readonly Color Border = Color.FromArgb(55, 52, 72);
        public static readonly Color Text = Color.FromArgb(236, 234, 243);
        public static readonly Color Muted = Color.FromArgb(150, 146, 168);
        public static readonly Color Accent = Color.FromArgb(167, 123, 243);
        public static readonly Color Green = Color.FromArgb(47, 209, 106);
        public static readonly Color Red = Color.FromArgb(255, 93, 80);
        public static readonly Color Yellow = Color.FromArgb(247, 209, 74);
        public static readonly Color Orange = Color.FromArgb(255, 160, 70);
        public static readonly Color Blue = Color.FromArgb(92, 200, 240);

        public static readonly Font Body = new Font("Segoe UI", 9.5f);
        public static readonly Font Bold = new Font("Segoe UI Semibold", 9.5f);
        public static readonly Font H1 = new Font("Segoe UI Semibold", 18f);
        public static readonly Font H2 = new Font("Segoe UI Semibold", 12.5f);
        public static readonly Font Small = new Font("Segoe UI", 8.5f);
        public static readonly Font Mono = new Font("Consolas", 9.75f);

        public static Color StateColor(ServiceState s) => s switch
        {
            ServiceState.Running => Green,
            ServiceState.Starting => Yellow,
            ServiceState.Stopping => Yellow,
            ServiceState.External => Orange,
            _ => Red,
        };

        public static string StateText(ServiceState s) => s switch
        {
            ServiceState.Running => "Açık",
            ServiceState.Starting => "Açılıyor...",
            ServiceState.Stopping => "Kapanıyor...",
            ServiceState.Crashed => "Çöktü",
            ServiceState.External => "Panel dışında açık",
            _ => "Kapalı",
        };

        public static Label Label(string text, Font font = null, Color? color = null) => new Label
        {
            Text = text, AutoSize = true, Font = font ?? Body, ForeColor = color ?? Text, BackColor = Color.Transparent,
        };

        public static TextBox TextBox(string text = "") => new DarkTextBox { Text = text };

        public static ComboBox Combo() => new DarkCombo();

        public static CheckBox Check(string text) => new DarkCheck { Text = text };

        public static NumericUpDown Number(int min, int max, int width = 90, int value = -1) =>
            new DarkNumber { Minimum = min, Maximum = max, Width = width, Value = value < min ? min : Math.Min(max, value) };

        public static ListView List()
        {
            var lv = new ListView
            {
                View = View.Details, FullRowSelect = true, HideSelection = false, BackColor = Input, ForeColor = Text,
                BorderStyle = BorderStyle.None, Font = Body, OwnerDraw = true, HeaderStyle = ColumnHeaderStyle.Nonclickable,
            };
            lv.DrawColumnHeader += (s, e) =>
            {
                using (var b = new SolidBrush(Card)) e.Graphics.FillRectangle(b, e.Bounds);
                TextRenderer.DrawText(e.Graphics, e.Header.Text, Bold, e.Bounds, Muted, TextFormatFlags.VerticalCenter | TextFormatFlags.Left);
            };
            lv.DrawItem += (s, e) => e.DrawDefault = true;
            lv.DrawSubItem += (s, e) => e.DrawDefault = true;
            // last column fills the width: no white header gap, no horizontal scrollbar
            void stretch()
            {
                if (lv.Columns.Count == 0) return;
                int used = 0;
                for (int i = 0; i < lv.Columns.Count - 1; i++) used += lv.Columns[i].Width;
                lv.Columns[lv.Columns.Count - 1].Width = Math.Max(80, lv.ClientSize.Width - used);
            }
            lv.Resize += (s, e) => stretch();
            lv.HandleCreated += (s, e) => stretch();
            return Dark(lv);
        }

        public static string Bytes(long b) => b >= 1L << 30 ? $"{b / 1073741824.0:0.0} GB" : $"{b / 1048576.0:0} MB";

        public static string Uptime(DateTime? since)
        {
            if (since == null) return "-";
            var t = DateTime.Now - since.Value;
            return t.TotalDays >= 1 ? $"{(int)t.TotalDays} g {t.Hours} sa" : t.TotalHours >= 1 ? $"{(int)t.TotalHours} sa {t.Minutes} dk" : $"{t.Minutes} dk {t.Seconds} sn";
        }
    }

    /// <summary>Rounded button with hover/press states and a colour role.</summary>
    public class DarkButton : Button
    {
        Color baseColor;
        bool hover, down;
        public int Radius { get; set; } = 7;

        public DarkButton(string text, Color? color = null)
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.SupportsTransparentBackColor, true);
            Text = text;
            baseColor = color ?? Theme.CardHi;
            FlatStyle = FlatStyle.Flat;
            FlatAppearance.BorderSize = 0;
            BackColor = baseColor;
            ForeColor = IsLight(baseColor) ? Color.FromArgb(20, 18, 28) : Theme.Text;
            Font = Theme.Bold;
            Cursor = Cursors.Hand;
            AutoSize = false;
            Height = 32;
            Padding = new Padding(4, 0, 4, 0);
            FlatAppearance.MouseOverBackColor = ControlPaint.Light(baseColor, 0.25f);
            FlatAppearance.MouseDownBackColor = ControlPaint.Dark(baseColor, 0.05f);
            UseVisualStyleBackColor = false;
        }

        public void SetColor(Color c)
        {
            baseColor = c;
            BackColor = c;
            ForeColor = IsLight(c) ? Color.FromArgb(20, 18, 28) : Theme.Text;
            FlatAppearance.MouseOverBackColor = ControlPaint.Light(c, 0.25f);
        }

        static bool IsLight(Color c) => c.R * 0.299 + c.G * 0.587 + c.B * 0.114 > 150;

        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = down = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { down = true; Invalidate(); base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { down = false; Invalidate(); base.OnMouseUp(e); }
        protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Parent?.BackColor ?? Theme.Bg);
            Color fill = !Enabled ? Blend(baseColor, Theme.Bg, 0.55f)
                : down ? ControlPaint.Dark(baseColor, 0.08f)
                : hover ? ControlPaint.Light(baseColor, 0.22f) : baseColor;
            var r = new Rectangle(0, 0, Width - 1, Height - 1);
            if (r.Width < 4 || r.Height < 4) return;
            using (var path = Card.Round(r, Math.Min(Radius, Height / 2)))
            {
                using (var b = new LinearGradientBrush(r, ControlPaint.Light(fill, 0.08f), fill, LinearGradientMode.Vertical)) g.FillPath(b, path);
                using (var p = new Pen(Color.FromArgb(Enabled ? 40 : 15, 255, 255, 255))) g.DrawPath(p, path);
            }
            var fg = Enabled ? ForeColor : Color.FromArgb(120, ForeColor);
            TextRenderer.DrawText(g, Text, Font, r, fg, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine);
        }

        static Color Blend(Color a, Color b, float t) =>
            Color.FromArgb((int)(a.R + (b.R - a.R) * t), (int)(a.G + (b.G - a.G) * t), (int)(a.B + (b.B - a.B) * t));

        public static DarkButton Sized(string text, int width, Color? color = null)
        {
            var b = new DarkButton(text, color) { Width = width };
            return b;
        }
    }

    /// <summary>Round status lamp with a soft glow.</summary>
    public class StatusLight : Control
    {
        Color color = Theme.Red;
        bool pulse;
        int phase;
        readonly Timer timer = new Timer { Interval = 120 };

        public StatusLight(int size = 18)
        {
            Size = new Size(size, size);
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
            timer.Tick += (s, e) => { phase = (phase + 1) % 12; Invalidate(); };
        }

        public void SetState(ServiceState state)
        {
            var c = Theme.StateColor(state);
            bool p = state == ServiceState.Starting || state == ServiceState.Stopping;
            if (c == color && p == pulse) return;
            color = c;
            pulse = p;
            timer.Enabled = p;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            int alpha = pulse ? 60 + (int)(50 * Math.Abs(Math.Sin(phase * Math.PI / 6))) : 70;
            using (var glow = new SolidBrush(Color.FromArgb(alpha, color))) g.FillEllipse(glow, 0, 0, Width - 1, Height - 1);
            int inset = Math.Max(3, Width / 5);
            var r = new Rectangle(inset, inset, Width - 2 * inset - 1, Height - 2 * inset - 1);
            using (var path = new GraphicsPath())
            {
                path.AddEllipse(r);
                using var pg = new PathGradientBrush(path)
                {
                    CenterColor = ControlPaint.Light(color, 0.6f),
                    SurroundColors = new[] { color },
                };
                g.FillEllipse(pg, r);
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) timer.Dispose();
            base.Dispose(disposing);
        }
    }

    /// <summary>Rounded card background.</summary>
    public class Card : Panel
    {
        public Color Fill { get; set; } = Theme.Card;
        public Color? FillTo { get; set; }
        /// <summary>Coloured strip on the left edge (state / role).</summary>
        public Color? Accent { get; set; }
        public int Radius { get; set; } = 12;

        public Card()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            BackColor = Theme.Bg;
            Padding = new Padding(14);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var rect = new Rectangle(0, 0, Width - 1, Height - 1);
            if (rect.Width < 4 || rect.Height < 4) return;
            using var path = Round(rect, Radius);
            using (Brush b = FillTo.HasValue ? new LinearGradientBrush(rect, Fill, FillTo.Value, LinearGradientMode.ForwardDiagonal) : (Brush)new SolidBrush(Fill))
                g.FillPath(b, path);
            if (Accent.HasValue)
            {
                var old = g.Clip;
                g.SetClip(path);
                using (var a = new SolidBrush(Accent.Value)) g.FillRectangle(a, 0, 0, 4, Height);
                using (var glow = new LinearGradientBrush(new Rectangle(4, 0, 80, Math.Max(1, Height)), Color.FromArgb(30, Accent.Value), Color.FromArgb(0, Accent.Value), LinearGradientMode.Horizontal))
                    g.FillRectangle(glow, 4, 0, 80, Height);
                g.Clip = old;
            }
            using (var p = new Pen(Color.FromArgb(90, Theme.Border))) g.DrawPath(p, path);
        }

        public static GraphicsPath Round(Rectangle r, int radius)
        {
            var path = new GraphicsPath();
            int d = radius * 2;
            path.AddArc(r.X, r.Y, d, d, 180, 90);
            path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }
    }

    /// <summary>Base for the pages shown right of the sidebar.</summary>
    public abstract class Page : UserControl
    {
        protected readonly ServiceManager Manager;
        protected readonly MainForm Main;

        protected Page(ServiceManager manager, MainForm main)
        {
            Manager = manager;
            Main = main;
            BackColor = Theme.Bg;
            ForeColor = Theme.Text;
            Font = Theme.Body;
            Dock = DockStyle.Fill;
            Padding = new Padding(22, 18, 22, 18);
            DoubleBuffered = true;
        }

        public abstract string Title { get; }
        /// <summary>Called every second while visible.</summary>
        public virtual void RefreshView() { }
        public virtual void OnShown() => RefreshView();
        /// <summary>Services were added/removed/renamed.</summary>
        public virtual void ServicesChanged() { }

        protected static Panel Header(string title, string subtitle)
        {
            var p = new Panel { Dock = DockStyle.Top, Height = 64, BackColor = Theme.Bg };
            p.Controls.Add(new Label { Text = subtitle, Font = Theme.Body, ForeColor = Theme.Muted, AutoSize = true, Location = new Point(0, 38) });
            p.Controls.Add(new Label { Text = title, Font = Theme.H1, ForeColor = Theme.Text, AutoSize = true, Location = new Point(-3, 0) });
            return p;
        }
    }

    public class GradientPanel : Panel
    {
        public Color Top { get; set; } = Color.FromArgb(26, 19, 42);
        public Color Bottom { get; set; } = Color.FromArgb(10, 10, 15);
        public GradientPanel() { SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true); }
        protected override void OnPaintBackground(PaintEventArgs e)
        {
            if (Width < 1 || Height < 1) return;
            using (var b = new LinearGradientBrush(ClientRectangle, Top, Bottom, LinearGradientMode.Vertical)) e.Graphics.FillRectangle(b, ClientRectangle);
            using (var line = new Pen(Color.FromArgb(45, Theme.Accent))) e.Graphics.DrawLine(line, Width - 1, 0, Width - 1, Height);
        }
    }

    /// <summary>Sidebar entry: icon, label, active accent bar, hover.</summary>
    public class NavButton : Control
    {
        static readonly Font IconFont = new Font("Segoe UI Symbol", 13f);
        static readonly Font LabelFont = new Font("Segoe UI Semibold", 10.5f);
        public string Icon { get; set; }
        public bool Active { get => active; set { active = value; Invalidate(); } }
        bool active, hover;
        public NavButton(string icon, string text)
        {
            Icon = icon; Text = text; Height = 46; Cursor = Cursors.Hand;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
        }
        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var r = new Rectangle(10, 3, Width - 20, Height - 6);
            if ((active || hover) && r.Width > 4)
            {
                using var path = Card.Round(r, 9);
                using var b = new LinearGradientBrush(r, Color.FromArgb(active ? 120 : 45, Theme.Accent), Color.FromArgb(active ? 20 : 8, Theme.Accent), LinearGradientMode.Horizontal);
                g.FillPath(b, path);
            }
            if (active) using (var bar = new SolidBrush(Theme.Accent)) g.FillRectangle(bar, 0, 10, 4, Height - 20);
            TextRenderer.DrawText(g, Icon, IconFont, new Rectangle(22, 0, 30, Height), active ? Color.White : Theme.Accent, TextFormatFlags.VerticalCenter | TextFormatFlags.HorizontalCenter);
            TextRenderer.DrawText(g, Text, LabelFont, new Rectangle(58, 0, Width - 60, Height), active ? Color.White : Theme.Muted, TextFormatFlags.VerticalCenter | TextFormatFlags.Left);
        }
    }

    /// <summary>Tiny area chart (CPU history).</summary>
    public class Sparkline : Control
    {
        public float[] Values = new float[0];
        public Color Color { get; set; } = Theme.Accent;
        public Sparkline() { SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.SupportsTransparentBackColor, true); BackColor = Color.Transparent; }
        protected override void OnPaint(PaintEventArgs e)
        {
            var v = Values;
            if (v == null || v.Length < 2 || Width < 4 || Height < 4) return;
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            float max = Math.Max(10f, v.Max());
            var pts = new PointF[v.Length];
            for (int i = 0; i < v.Length; i++) pts[i] = new PointF(i * (Width - 1f) / (v.Length - 1), Height - 2 - v[i] / max * (Height - 4));
            using (var path = new GraphicsPath())
            {
                path.AddLines(pts);
                path.AddLine(pts[pts.Length - 1], new PointF(Width - 1, Height));
                path.AddLine(new PointF(Width - 1, Height), new PointF(0, Height));
                path.CloseFigure();
                using var fill = new LinearGradientBrush(ClientRectangle, Color.FromArgb(90, Color), Color.FromArgb(0, Color), LinearGradientMode.Vertical);
                g.FillPath(fill, path);
            }
            using var pen = new Pen(Color, 1.6f);
            g.DrawLines(pen, pts);
        }
    }

    /// <summary>Big number with a caption, for the overview strip.</summary>
    public class StatTile : Card
    {
        readonly Label value = new Label { AutoSize = true, Font = new Font("Segoe UI Semibold", 20f), ForeColor = Theme.Text, BackColor = Color.Transparent, Location = new Point(18, 24) };
        readonly Label caption = new Label { AutoSize = true, Font = Theme.Small, ForeColor = Theme.Muted, BackColor = Color.Transparent, Location = new Point(20, 10) };
        readonly Label sub = new Label { AutoSize = true, Font = Theme.Small, ForeColor = Theme.Muted, BackColor = Color.Transparent, Location = new Point(20, 64) };
        public StatTile(string title, Color accent)
        {
            caption.Text = title.ToUpperInvariant();
            Accent = accent;
            FillTo = Color.FromArgb(24, 22, 33);
            Size = new Size(236, 90);
            Margin = new Padding(0, 0, 14, 0);
            Controls.AddRange(new Control[] { caption, value, sub });
        }
        public void Set(string v, string s) { if (value.Text != v) value.Text = v; if (sub.Text != s) sub.Text = s; }
    }
}
