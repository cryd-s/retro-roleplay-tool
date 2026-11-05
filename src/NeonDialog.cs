using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

// Meldung im Stil des Programms. Wird aufgerufen wie MessageBox.Show.
// Tastatur: Enter = erster Knopf, Esc = Abbrechen bzw. Nein, J/N/A/O = der jeweilige Knopf.
public class NeonDialog : Form
{
    readonly Dictionary<Keys, DialogResult> hotkeys = new Dictionary<Keys, DialogResult>();
    readonly Color accent;
    readonly string glyph, title;
    readonly Font titleFont = Theme.MakeFont("Segoe UI", 13f, FontStyle.Bold);
    readonly Font glyphFont = Theme.MakeFont("Segoe MDL2 Assets", 17f);
    readonly Panel scroller;
    DialogResult enterResult, escapeResult;

    public static DialogResult Show(IWin32Window owner, string text, string title, MessageBoxButtons buttons, MessageBoxIcon icon)
    {
        using (var dialog = new NeonDialog(text, title, buttons, icon))
        {
            dialog.StartPosition = owner != null ? FormStartPosition.CenterParent : FormStartPosition.CenterScreen;
            return dialog.ShowDialog(owner);
        }
    }

    public NeonDialog(string text, string title, MessageBoxButtons buttons, MessageBoxIcon icon)
    {
        this.title = title;
        Text = title;
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        MinimizeBox = false;
        MaximizeBox = false;
        KeyPreview = true;
        DoubleBuffered = true;
        BackColor = Theme.Bg;
        ForeColor = Theme.Text;
        Font = Theme.MakeFont("Segoe UI", 10.5f);

        bool alarm = icon == MessageBoxIcon.Warning || icon == MessageBoxIcon.Error;
        accent = alarm ? Theme.Pink : Theme.Orange;
        glyph = icon == MessageBoxIcon.Warning ? "" : icon == MessageBoxIcon.Error ? ""
              : icon == MessageBoxIcon.Question ? "" : "";

        int width = Theme.S(540), pad = Theme.S(24);
        int bodyTop = pad + Theme.S(44) + Theme.S(14);
        int buttonH = Theme.S(50), footer = Theme.S(16) + buttonH + Theme.S(14);
        int bodyW = width - 2 * pad;

        // Sehr lange Meldungen (z. B. viele Fehler) werden scrollbar, das Fenster bleibt auf dem Bildschirm.
        int maxBody = Math.Max(Theme.S(120), Screen.FromPoint(Cursor.Position).WorkingArea.Height - bodyTop - footer - Theme.S(40));
        int textH = DialogText.Measure(text, Font, bodyW);
        bool scrolls = textH > maxBody;
        if (scrolls) textH = DialogText.Measure(text, Font, bodyW - SystemInformation.VerticalScrollBarWidth);
        int bodyH = Math.Min(textH, maxBody);

        scroller = new Panel { BackColor = Theme.Bg, AutoScroll = scrolls, Bounds = new Rectangle(pad, bodyTop, bodyW, bodyH) };
        scroller.Controls.Add(new DialogText
        {
            Text = text,
            Font = Font,
            Bounds = new Rectangle(0, 0, scrolls ? bodyW - SystemInformation.VerticalScrollBarWidth : bodyW, textH),
        });
        Controls.Add(scroller);

        ClientSize = new Size(width, bodyTop + bodyH + footer);
        AddButtons(buttons, bodyTop + bodyH + Theme.S(16), buttonH, pad);
    }

    void AddButtons(MessageBoxButtons buttons, int y, int h, int pad)
    {
        var list = new List<KeyValuePair<string, DialogResult>>();
        if (buttons == MessageBoxButtons.OK)
        {
            list.Add(new KeyValuePair<string, DialogResult>("OK", DialogResult.OK));
            hotkeys[Keys.O] = DialogResult.OK;
            escapeResult = DialogResult.OK;
        }
        else
        {
            list.Add(new KeyValuePair<string, DialogResult>("JA", DialogResult.Yes));
            list.Add(new KeyValuePair<string, DialogResult>("NEIN", DialogResult.No));
            hotkeys[Keys.J] = DialogResult.Yes;
            hotkeys[Keys.N] = DialogResult.No;
            escapeResult = DialogResult.No;
            if (buttons == MessageBoxButtons.YesNoCancel)
            {
                list.Add(new KeyValuePair<string, DialogResult>("ABBRECHEN", DialogResult.Cancel));
                hotkeys[Keys.A] = DialogResult.Cancel;
                escapeResult = DialogResult.Cancel;
            }
        }
        enterResult = list[0].Value;

        int glow = Theme.S(6), w = Theme.S(142), gap = Theme.S(2);
        int x = ClientSize.Width - pad + glow - list.Count * w - (list.Count - 1) * gap;
        foreach (KeyValuePair<string, DialogResult> entry in list)
        {
            DialogResult result = entry.Value;
            var button = new NeonButton { Text = entry.Key, Primary = result == enterResult, Bounds = new Rectangle(x, y, w, h) };
            button.Click += delegate { DialogResult = result; };
            Controls.Add(button);
            x += w + gap;
        }
    }

    protected override CreateParams CreateParams
    {
        get
        {
            const int DropShadow = 0x00020000;
            CreateParams cp = base.CreateParams;
            cp.ClassStyle |= DropShadow;
            return cp;
        }
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        // Windows 11: runde Fensterecken. Windows 10 kennt den Wert nicht und bleibt eckig.
        const int CornerPreference = 33;
        int round = 2;
        try { DwmSetWindowAttribute(Handle, CornerPreference, ref round, sizeof(int)); }
        catch (Exception) { }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        DialogResult result;
        if (e.KeyCode == Keys.Enter) DialogResult = enterResult;
        else if (e.KeyCode == Keys.Escape) DialogResult = escapeResult;
        else if (hotkeys.TryGetValue(e.KeyCode, out result)) DialogResult = result;
        base.OnKeyDown(e);
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);
        if (!scroller.AutoScroll) return;
        Point p = scroller.AutoScrollPosition;
        scroller.AutoScrollPosition = new Point(-p.X, -p.Y - e.Delta / 2);
    }

    // Das Fenster hat keine Titelleiste: Verschieben geht über den freien Hintergrund.
    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button != MouseButtons.Left) return;
        const int NcLeftButtonDown = 0xA1;
        const int Caption = 2;
        ReleaseCapture();
        SendMessage(Handle, NcLeftButtonDown, (IntPtr)Caption, IntPtr.Zero);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        Graphics g = e.Graphics;
        Theme.Prepare(g);

        float border = Theme.S(2);
        var frame = new RectangleF(border / 2, border / 2, ClientSize.Width - border - 1, ClientSize.Height - border - 1);
        using (GraphicsPath p = Theme.Rounded(frame, Theme.S(8)))
        using (var pen = new Pen(accent, border))
            g.DrawPath(pen, p);

        int pad = Theme.S(24), d = Theme.S(44);
        var circle = new RectangleF(pad, pad, d, d);
        using (var fill = new LinearGradientBrush(circle, Theme.Pink, Theme.Orange, 45f))
            g.FillEllipse(fill, circle);
        TextRenderer.DrawText(g, glyph, glyphFont, Rectangle.Round(circle), Color.FromArgb(28, 10, 30),
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);

        int left = pad + d + Theme.S(14);
        TextRenderer.DrawText(g, title, titleFont, new Rectangle(left, pad, ClientSize.Width - left - pad, d), accent,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            titleFont.Dispose();
            glyphFont.Dispose();
        }
        base.Dispose(disposing);
    }

    [DllImport("dwmapi.dll")]
    static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);

    [DllImport("user32.dll")]
    static extern bool ReleaseCapture();

    [DllImport("user32.dll")]
    static extern IntPtr SendMessage(IntPtr window, int message, IntPtr wParam, IntPtr lParam);

    // Fließtext: bricht auch lange Pfade um und zeigt & als normales Zeichen.
    class DialogText : NeonControl
    {
        const TextFormatFlags Flags = TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.WordBreak |
                                      TextFormatFlags.TextBoxControl | TextFormatFlags.NoPrefix;

        public static int Measure(string text, Font font, int width)
        {
            return TextRenderer.MeasureText(text, font, new Size(width, 0), Flags).Height;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            TextRenderer.DrawText(e.Graphics, Text, Font, ClientRectangle, Theme.Text, Flags);
        }
    }
}
