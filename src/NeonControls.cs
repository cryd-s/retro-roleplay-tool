using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

// Farben und Skalierung im Stil des Banners (dunkel, Orange, Neon-Pink).
public static partial class Theme
{
    public static readonly Color Bg = Color.FromArgb(15, 7, 23);
    public static readonly Color Line = Color.FromArgb(52, 28, 70);
    public static readonly Color Track = Color.FromArgb(38, 20, 52);
    public static readonly Color Orange = Color.FromArgb(255, 138, 31);
    public static readonly Color OrangeDark = Color.FromArgb(226, 84, 10);
    public static readonly Color Pink = Color.FromArgb(255, 63, 180);
    public static readonly Color Text = Color.FromArgb(248, 244, 252);
    public static readonly Color Muted = Color.FromArgb(160, 140, 178);
    public static readonly Color Good = Color.FromArgb(110, 231, 168);

    public static float Scale = 1f;
    // Schriften wachsen mit der Windows-Skalierung von selbst; muss das Fenster kleiner werden
    // als die Windows-Skalierung, schrumpfen sie hiermit im gleichen Verhältnis mit.
    public static float FontFactor = 1f;
    public static int S(float v) { return (int)Math.Round(v * Scale); }

    public static Font MakeFont(string family, float size, FontStyle style = FontStyle.Regular)
    {
        return new Font(family, size * FontFactor, style);
    }

    public static GraphicsPath Rounded(RectangleF r, float radius)
    {
        float d = radius * 2;
        var p = new GraphicsPath();
        p.AddArc(r.X, r.Y, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }

    public static void Prepare(Graphics g)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
    }
}

public abstract class NeonControl : Control
{
    protected NeonControl()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        BackColor = Theme.Bg;
        ForeColor = Theme.Text;
    }
}

// Button: Primary = orange gefüllt mit Glow, sonst pinker Neon-Rahmen.
public class NeonButton : NeonControl
{
    public bool Primary;
    bool hover, down;

    public NeonButton()
    {
        Cursor = Cursors.Hand;
        Font = Theme.MakeFont("Segoe UI", 10.5f, FontStyle.Bold);
    }

    protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { hover = false; down = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnMouseDown(MouseEventArgs e) { down = true; Invalidate(); base.OnMouseDown(e); }
    protected override void OnMouseUp(MouseEventArgs e) { down = false; Invalidate(); base.OnMouseUp(e); }
    protected override void OnEnabledChanged(EventArgs e) { Cursor = Enabled ? Cursors.Hand : Cursors.Default; Invalidate(); base.OnEnabledChanged(e); }
    protected override void OnTextChanged(EventArgs e) { Invalidate(); base.OnTextChanged(e); }

    // Für Screenreader / Windows-Automatisierung als normaler Button sichtbar und auslösbar.
    protected override AccessibleObject CreateAccessibilityInstance() { return new ButtonAccessible(this); }

    public void PerformClick() { if (Enabled && Visible) OnClick(EventArgs.Empty); }

    class ButtonAccessible : ControlAccessibleObject
    {
        readonly NeonButton owner;
        public ButtonAccessible(NeonButton owner) : base(owner) { this.owner = owner; }
        public override AccessibleRole Role { get { return AccessibleRole.PushButton; } }
        public override string DefaultAction { get { return "Drücken"; } }
        public override void DoDefaultAction() { owner.PerformClick(); }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics g = e.Graphics;
        Theme.Prepare(g);
        float glow = Theme.S(6);
        var body = new RectangleF(glow, glow, Width - 2 * glow - 1, Height - 2 * glow - 1);
        float radius = Theme.S(9);
        Color accent = Primary ? Theme.Orange : Theme.Pink;

        if (!Enabled)
        {
            using (GraphicsPath p = Theme.Rounded(body, radius))
            using (var pen = new Pen(Theme.Line, Theme.S(2)))
            using (var fill = new SolidBrush(Theme.Track))
            {
                g.FillPath(fill, p);
                g.DrawPath(pen, p);
            }
            DrawText(g, body, Theme.Muted);
            return;
        }

        // Glow-Ringe
        int rings = hover ? 6 : 4;
        for (int i = rings; i >= 1; i--)
        {
            var r = RectangleF.Inflate(body, i * glow / rings, i * glow / rings);
            using (GraphicsPath p = Theme.Rounded(r, radius + i))
            using (var pen = new Pen(Color.FromArgb(hover ? 26 : 16, accent), 2f))
                g.DrawPath(pen, p);
        }

        using (GraphicsPath p = Theme.Rounded(body, radius))
        {
            if (Primary)
            {
                Color a = down ? Theme.OrangeDark : Theme.Orange;
                Color b = down ? Color.FromArgb(190, 60, 5) : Theme.OrangeDark;
                using (var fill = new LinearGradientBrush(body, a, b, 90f)) g.FillPath(fill, p);
                if (hover)
                    using (var shine = new SolidBrush(Color.FromArgb(30, Color.White))) g.FillPath(shine, p);
                DrawText(g, body, Color.FromArgb(28, 10, 30));
            }
            else
            {
                using (var fill = new SolidBrush(Color.FromArgb(down ? 70 : hover ? 40 : 14, Theme.Pink))) g.FillPath(fill, p);
                using (var pen = new Pen(Theme.Pink, Theme.S(2))) g.DrawPath(pen, p);
                DrawText(g, body, Theme.Text);
            }
        }
    }

    void DrawText(Graphics g, RectangleF r, Color c)
    {
        TextRenderer.DrawText(g, Text, Font, Rectangle.Round(r), c,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
    }
}

// Regler in ganzen Schritten: mit der Maus ziehen oder mit den Pfeiltasten bedienen.
public class NeonSlider : NeonControl
{
    readonly int minimum, maximum;
    int value;
    bool hover, dragging;

    public event EventHandler ValueChanged;

    public NeonSlider(int minimum, int maximum, int value)
    {
        this.minimum = minimum;
        this.maximum = maximum;
        this.value = Math.Max(minimum, Math.Min(maximum, value));
        Cursor = Cursors.Hand;
        TabStop = true;
        SetStyle(ControlStyles.Selectable, true);
    }

    public int Value
    {
        get { return value; }
        set
        {
            int v = Math.Max(minimum, Math.Min(maximum, value));
            if (v == this.value) return;
            this.value = v;
            Invalidate();
            if (ValueChanged != null) ValueChanged(this, EventArgs.Empty);
        }
    }

    int TrackLeft { get { return Theme.S(10); } }
    int TrackRight { get { return Width - Theme.S(10); } }

    float XOf(int v)
    {
        return TrackLeft + (TrackRight - TrackLeft) * (v - minimum) / (float)(maximum - minimum);
    }

    int ValueAt(int x)
    {
        return minimum + (int)Math.Round((x - TrackLeft) * (maximum - minimum) / (double)(TrackRight - TrackLeft));
    }

    protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
    protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }
    protected override void OnEnabledChanged(EventArgs e) { Cursor = Enabled ? Cursors.Hand : Cursors.Default; Invalidate(); base.OnEnabledChanged(e); }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button != MouseButtons.Left) return;
        Focus();
        dragging = true;
        Capture = true;
        Value = ValueAt(e.X);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (dragging) Value = ValueAt(e.X);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        dragging = false;
        Capture = false;
    }

    protected override bool IsInputKey(Keys key)
    {
        switch (key)
        {
            case Keys.Left: case Keys.Right: case Keys.Up: case Keys.Down: case Keys.Home: case Keys.End: return true;
        }
        return base.IsInputKey(key);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        switch (e.KeyCode)
        {
            case Keys.Left: case Keys.Down: Value = value - 1; break;
            case Keys.Right: case Keys.Up: Value = value + 1; break;
            case Keys.Home: Value = minimum; break;
            case Keys.End: Value = maximum; break;
            default: return;
        }
        e.Handled = true;
    }

    // Für Screenreader: als Regler sichtbar, der Wert kommt aus Text.
    protected override AccessibleObject CreateAccessibilityInstance() { return new SliderAccessible(this); }

    class SliderAccessible : ControlAccessibleObject
    {
        readonly NeonSlider owner;
        public SliderAccessible(NeonSlider owner) : base(owner) { this.owner = owner; }
        public override AccessibleRole Role { get { return AccessibleRole.Slider; } }
        public override string Value { get { return owner.Text; } }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics g = e.Graphics;
        Theme.Prepare(g);
        float cy = Height / 2f, trackH = Theme.S(4), thumb = Theme.S(8), x = XOf(value);
        Color accent = Enabled ? Theme.Orange : Theme.Muted;

        var track = new RectangleF(TrackLeft, cy - trackH / 2, TrackRight - TrackLeft, trackH);
        using (GraphicsPath p = Theme.Rounded(track, trackH / 2))
        using (var fill = new SolidBrush(Theme.Track))
            g.FillPath(fill, p);
        var filled = new RectangleF(TrackLeft, cy - trackH / 2, Math.Max(trackH, x - TrackLeft), trackH);
        using (GraphicsPath p = Theme.Rounded(filled, trackH / 2))
        using (var fill = new SolidBrush(accent))
            g.FillPath(fill, p);

        float dot = Theme.S(1.5f);
        using (var passed = new SolidBrush(Color.FromArgb(28, 10, 30)))
        using (var ahead = new SolidBrush(Theme.Line))
            for (int v = minimum; v <= maximum; v++)
                g.FillEllipse(v <= value ? passed : ahead, XOf(v) - dot, cy - dot, 2 * dot, 2 * dot);

        if (Enabled)
        {
            int rings = hover || dragging ? 4 : 2;
            for (int i = rings; i >= 1; i--)
            {
                float r = thumb + i * Theme.S(1);
                using (var fill = new SolidBrush(Color.FromArgb(30, Theme.Orange)))
                    g.FillEllipse(fill, x - r, cy - r, 2 * r, 2 * r);
            }
        }
        using (var fill = new SolidBrush(accent))
            g.FillEllipse(fill, x - thumb, cy - thumb, 2 * thumb, 2 * thumb);
        if (Focused)
            using (var pen = new Pen(Theme.Pink, Theme.S(2)))
                g.DrawEllipse(pen, x - thumb, cy - thumb, 2 * thumb, 2 * thumb);
    }
}

// Eine Zeile in der Liste: Häkchen, Name, Pfad, Wert rechts.
public class TargetRow : NeonControl
{
    bool isChecked = true;
    string title = "", subtitle = "", value = "";
    Color valueColor = Theme.Orange;
    readonly Font titleFont = Theme.MakeFont("Segoe UI Semibold", 11f);
    readonly Font subFont = Theme.MakeFont("Segoe UI", 8.25f);
    readonly Font valueFont = Theme.MakeFont("Segoe UI", 11f, FontStyle.Bold);

    public event EventHandler CheckedChanged;

    public TargetRow() { Cursor = Cursors.Hand; }

    public bool Checked
    {
        get { return isChecked; }
        set { if (isChecked == value) return; isChecked = value; Invalidate(); if (CheckedChanged != null) CheckedChanged(this, EventArgs.Empty); }
    }
    public string Title { get { return title; } set { title = value; Invalidate(); } }
    public string Subtitle { get { return subtitle; } set { subtitle = value; Invalidate(); } }
    public string Value { get { return value; } set { this.value = value; Invalidate(); } }
    public Color ValueColor { get { return valueColor; } set { valueColor = value; Invalidate(); } }

    protected override void OnClick(EventArgs e)
    {
        if (Enabled) Checked = !Checked;
        base.OnClick(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics g = e.Graphics;
        Theme.Prepare(g);
        int box = Theme.S(22);
        int left = box + Theme.S(16);

        var r = new RectangleF(Theme.S(2), (Height - box) / 2f, box, box);
        using (GraphicsPath p = Theme.Rounded(r, Theme.S(5)))
        {
            if (isChecked)
            {
                using (var fill = new LinearGradientBrush(r, Theme.Orange, Theme.OrangeDark, 90f)) g.FillPath(fill, p);
                using (var pen = new Pen(Color.FromArgb(28, 10, 30), Theme.S(2.6f)))
                {
                    pen.StartCap = pen.EndCap = LineCap.Round;
                    g.DrawLines(pen, new[] {
                        new PointF(r.X + r.Width * 0.24f, r.Y + r.Height * 0.52f),
                        new PointF(r.X + r.Width * 0.43f, r.Y + r.Height * 0.70f),
                        new PointF(r.X + r.Width * 0.77f, r.Y + r.Height * 0.32f) });
                }
            }
            else
            {
                using (var pen = new Pen(Enabled ? Theme.Pink : Theme.Line, Theme.S(2))) g.DrawPath(pen, p);
            }
        }

        Color titleColor = Enabled ? Theme.Text : Theme.Muted;
        int valueWidth = Theme.S(190);
        var titleRect = new Rectangle(left, Theme.S(4), Width - left - valueWidth, Height / 2);
        var subRect = new Rectangle(left, Height / 2 + Theme.S(4), Width - left - valueWidth, Height / 2 - Theme.S(4));
        TextRenderer.DrawText(g, title, titleFont, titleRect, titleColor, TextFormatFlags.Left | TextFormatFlags.Bottom | TextFormatFlags.EndEllipsis);
        TextRenderer.DrawText(g, subtitle, subFont, subRect, Theme.Muted, TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.PathEllipsis);
        TextRenderer.DrawText(g, value, valueFont, new Rectangle(Width - valueWidth, 0, valueWidth - Theme.S(2), Height),
            Enabled ? valueColor : Theme.Muted, TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);

        using (var pen = new Pen(Theme.Line)) g.DrawLine(pen, 0, Height - 1, Width, Height - 1);
    }
}

// Große Kachel auf der Übersichtsseite: Symbol, Titel, Beschreibung, Pfeil.
public class NavTile : NeonControl
{
    bool hover, down;
    string detail = "", glyph = "";
    readonly Font titleFont = Theme.MakeFont("Segoe UI", 15f, FontStyle.Bold);
    readonly Font detailFont = Theme.MakeFont("Segoe UI", 9.5f);
    readonly Font glyphFont = Theme.MakeFont("Segoe MDL2 Assets", 22f);
    readonly Font arrowFont = Theme.MakeFont("Segoe UI", 22f, FontStyle.Bold);

    public NavTile() { Cursor = Cursors.Hand; }

    public string Detail { get { return detail; } set { detail = value; Invalidate(); } }
    public string Glyph { get { return glyph; } set { glyph = value; Invalidate(); } }

    protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { hover = false; down = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnMouseDown(MouseEventArgs e) { down = true; Invalidate(); base.OnMouseDown(e); }
    protected override void OnMouseUp(MouseEventArgs e) { down = false; Invalidate(); base.OnMouseUp(e); }
    protected override void OnEnabledChanged(EventArgs e) { Cursor = Enabled ? Cursors.Hand : Cursors.Default; Invalidate(); base.OnEnabledChanged(e); }
    protected override void OnTextChanged(EventArgs e) { Invalidate(); base.OnTextChanged(e); }

    public void PerformClick() { if (Enabled && Visible) OnClick(EventArgs.Empty); }

    protected override AccessibleObject CreateAccessibilityInstance() { return new TileAccessible(this); }

    class TileAccessible : ControlAccessibleObject
    {
        readonly NavTile owner;
        public TileAccessible(NavTile owner) : base(owner) { this.owner = owner; }
        public override AccessibleRole Role { get { return AccessibleRole.PushButton; } }
        public override string DefaultAction { get { return "Öffnen"; } }
        public override void DoDefaultAction() { owner.PerformClick(); }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics g = e.Graphics;
        Theme.Prepare(g);
        float glow = Theme.S(6);
        var body = new RectangleF(glow, glow, Width - 2 * glow - 1, Height - 2 * glow - 1);
        float radius = Theme.S(14);
        bool active = Enabled && (hover || down);
        Color accent = active ? Theme.Orange : Theme.Pink;

        if (Enabled)
        {
            int rings = active ? 6 : 3;
            for (int i = rings; i >= 1; i--)
            {
                var r = RectangleF.Inflate(body, i * glow / rings, i * glow / rings);
                using (GraphicsPath p = Theme.Rounded(r, radius + i))
                using (var pen = new Pen(Color.FromArgb(active ? 26 : 14, accent), 2f))
                    g.DrawPath(pen, p);
            }
        }

        using (GraphicsPath p = Theme.Rounded(body, radius))
        {
            Color fill = !Enabled ? Theme.Track : down ? Color.FromArgb(60, Theme.Orange) : active ? Color.FromArgb(34, Theme.Orange) : Color.FromArgb(24, 12, 34);
            using (var b = new SolidBrush(fill)) g.FillPath(b, p);
            using (var pen = new Pen(Enabled ? accent : Theme.Line, Theme.S(2))) g.DrawPath(pen, p);
        }

        // Symbol im Kreis
        float d = Theme.S(58);
        var circle = new RectangleF(body.X + Theme.S(22), body.Y + (body.Height - d) / 2, d, d);
        using (var b = new LinearGradientBrush(circle, Enabled ? Theme.Pink : Theme.Line, Enabled ? Theme.Orange : Theme.Line, 45f))
            g.FillEllipse(b, circle);
        TextRenderer.DrawText(g, glyph, glyphFont, Rectangle.Round(circle), Color.FromArgb(28, 10, 30),
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);

        int left = (int)(circle.Right + Theme.S(22));
        int right = (int)(body.Right - Theme.S(56));
        int mid = (int)(body.Y + body.Height / 2);
        TextRenderer.DrawText(g, Text, titleFont, new Rectangle(left, mid - Theme.S(34), right - left, Theme.S(34)),
            Enabled ? (active ? Theme.Orange : Theme.Text) : Theme.Muted,
            TextFormatFlags.Left | TextFormatFlags.Bottom | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
        TextRenderer.DrawText(g, detail, detailFont, new Rectangle(left, mid + Theme.S(2), right - left, Theme.S(24)), Theme.Muted,
            TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
        TextRenderer.DrawText(g, "›", arrowFont, new Rectangle(right, (int)body.Y, (int)body.Right - right - Theme.S(14), (int)body.Height),
            Enabled ? accent : Theme.Line, TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
    }
}

// Auswahlkarte (wie ein Radiobutton): Titel, Größe, Beschreibung, optionaler Warnhinweis.
public class ModeCard : NeonControl
{
    bool selected, hover;
    string title = "", detail = "", warning = "", value = "";
    readonly Font titleFont = Theme.MakeFont("Segoe UI Semibold", 12f);
    readonly Font valueFont = Theme.MakeFont("Segoe UI", 11f, FontStyle.Bold);
    readonly Font detailFont = Theme.MakeFont("Segoe UI", 8.5f);
    readonly Font warningFont = Theme.MakeFont("Segoe UI Semibold", 8.5f);

    public event EventHandler Picked;

    public ModeCard() { Cursor = Cursors.Hand; }

    public bool Selected { get { return selected; } set { selected = value; Invalidate(); } }
    public string Title { get { return title; } set { title = value; Text = value; Invalidate(); } }
    public string Detail { get { return detail; } set { detail = value; Invalidate(); } }
    public string Warning { get { return warning; } set { warning = value; Invalidate(); } }
    public string Value { get { return value; } set { this.value = value; Invalidate(); } }

    protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }

    protected override void OnClick(EventArgs e)
    {
        if (Enabled && Picked != null) Picked(this, EventArgs.Empty);
        base.OnClick(e);
    }

    protected override AccessibleObject CreateAccessibilityInstance() { return new CardAccessible(this); }

    class CardAccessible : ControlAccessibleObject
    {
        readonly ModeCard owner;
        public CardAccessible(ModeCard owner) : base(owner) { this.owner = owner; }
        public override AccessibleRole Role { get { return AccessibleRole.RadioButton; } }
        public override string DefaultAction { get { return "Auswählen"; } }
        public override void DoDefaultAction() { owner.OnClick(EventArgs.Empty); }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics g = e.Graphics;
        Theme.Prepare(g);
        float glow = Theme.S(5);
        var body = new RectangleF(glow, glow, Width - 2 * glow - 1, Height - 2 * glow - 1);
        float radius = Theme.S(12);
        bool active = Enabled && selected;

        if (active)
        {
            for (int i = 4; i >= 1; i--)
            {
                var r = RectangleF.Inflate(body, i * glow / 4, i * glow / 4);
                using (GraphicsPath p = Theme.Rounded(r, radius + i))
                using (var pen = new Pen(Color.FromArgb(18, Theme.Orange), 2f))
                    g.DrawPath(pen, p);
            }
        }

        using (GraphicsPath p = Theme.Rounded(body, radius))
        {
            Color fill = active ? Color.FromArgb(34, Theme.Orange) : hover && Enabled ? Color.FromArgb(18, Theme.Pink) : Color.FromArgb(24, 12, 34);
            using (var b = new SolidBrush(fill)) g.FillPath(b, p);
            Color border = active ? Theme.Orange : hover && Enabled ? Theme.Pink : Theme.Line;
            using (var pen = new Pen(border, active ? Theme.S(2) : Theme.S(1.5f))) g.DrawPath(pen, p);
        }

        // Radiopunkt
        float d = Theme.S(20);
        var dot = new RectangleF(body.X + Theme.S(14), body.Y + Theme.S(14), d, d);
        using (var pen = new Pen(active ? Theme.Orange : Theme.Muted, Theme.S(2))) g.DrawEllipse(pen, dot);
        if (active)
            using (var b = new SolidBrush(Theme.Orange)) g.FillEllipse(b, RectangleF.Inflate(dot, -Theme.S(5), -Theme.S(5)));

        int left = (int)(dot.Right + Theme.S(10));
        int right = (int)(body.Right - Theme.S(14));
        int top = (int)body.Y + Theme.S(11);
        Color text = Enabled ? Theme.Text : Theme.Muted;
        int valueWidth = value.Length == 0 ? 0 : Theme.S(110);
        TextRenderer.DrawText(g, title, titleFont, new Rectangle(left, top, right - left - valueWidth, Theme.S(26)), text,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
        TextRenderer.DrawText(g, value, valueFont, new Rectangle(right - valueWidth, top, valueWidth, Theme.S(26)),
            Enabled ? Theme.Orange : Theme.Muted, TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);

        int x = (int)body.X + Theme.S(16);
        int y = top + Theme.S(34);
        TextRenderer.DrawText(g, detail, detailFont, new Rectangle(x, y, right - x, Theme.S(36)), Theme.Muted,
            TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.WordBreak);
        if (warning.Length > 0)
            TextRenderer.DrawText(g, warning, warningFont, new Rectangle(x, (int)body.Bottom - Theme.S(28), right - x, Theme.S(20)),
                Enabled ? Theme.Pink : Theme.Muted, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
    }
}

// Fortschrittsbalken mit Pink→Orange-Verlauf. Value 0..1000.
public class NeonProgressBar : NeonControl
{
    int val;
    public int Value { get { return val; } set { val = Math.Max(0, Math.Min(1000, value)); Invalidate(); } }

    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics g = e.Graphics;
        Theme.Prepare(g);
        var track = new RectangleF(0, 0, Width - 1, Height - 1);
        float radius = (Height - 1) / 2f;
        using (GraphicsPath p = Theme.Rounded(track, radius))
        using (var b = new SolidBrush(Theme.Track))
            g.FillPath(b, p);

        float w = track.Width * val / 1000f;
        if (w < Height) { if (val == 0) return; w = Height; }
        var fillRect = new RectangleF(0, 0, w, Height - 1);
        using (GraphicsPath p = Theme.Rounded(fillRect, radius))
        using (var b = new LinearGradientBrush(new RectangleF(0, 0, Width, Height), Theme.Pink, Theme.Orange, 0f))
            g.FillPath(b, p);
    }
}

// Banner, einmalig hochwertig auf Fensterbreite skaliert, mit Neon-Kante unten.
public class BannerView : NeonControl
{
    Image source;
    Bitmap scaled;

    public BannerView(Image source) { this.source = source; }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        if (scaled != null) { scaled.Dispose(); scaled = null; }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        if (source == null || Width <= 0 || Height <= 0) return;
        if (scaled == null)
        {
            scaled = new Bitmap(Width, Height);
            using (Graphics sg = Graphics.FromImage(scaled))
            {
                sg.InterpolationMode = InterpolationMode.HighQualityBicubic;
                sg.PixelOffsetMode = PixelOffsetMode.HighQuality;
                sg.DrawImage(source, new Rectangle(0, 0, Width, Height));
            }
        }
        e.Graphics.DrawImageUnscaled(scaled, 0, 0);
        int h = Math.Max(2, Theme.S(3));
        using (var b = new LinearGradientBrush(new Rectangle(0, Height - h, Width, h), Theme.Pink, Theme.Orange, 0f))
            e.Graphics.FillRectangle(b, 0, Height - h, Width, h);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && scaled != null) scaled.Dispose();
        base.Dispose(disposing);
    }
}
