using DataDonkTM.Native;

namespace DataDonkTM.UI;

/// <summary>
/// A thick colored frame with a label ("LOBBY", "TABLE") drawn around another program's window, so it's
/// clear which window a list entry is. Click-through, never takes focus, and follows the window as it moves.
/// </summary>
internal sealed class WindowHighlight : Form
{
    private const int Thickness = 6;
    private static readonly Color Key = Color.Magenta;
    private readonly System.Windows.Forms.Timer _follow = new() { Interval = 150 };
    private readonly Font _labelFont = new("Segoe UI", 12, FontStyle.Bold);
    private readonly Color _color;
    private readonly string _label;
    private IntPtr _target;

    public WindowHighlight(Color color, string label)
    {
        _color = color;
        _label = label;
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        AutoScaleMode = AutoScaleMode.None;
        ShowInTaskbar = false;
        TopMost = true;
        DoubleBuffered = true;
        BackColor = Key;
        TransparencyKey = Key;
        _follow.Tick += (_, _) => Follow();
    }

    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= Win32.WS_EX_NOACTIVATE | Win32.WS_EX_TOOLWINDOW | Win32.WS_EX_TOPMOST
                          | Win32.WS_EX_LAYERED | Win32.WS_EX_TRANSPARENT;
            return cp;
        }
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == Win32.WM_DPICHANGED) { m.Result = IntPtr.Zero; return; } // sizes are in physical pixels
        base.WndProc(ref m);
    }

    /// <summary>Frames this window, or hides the frame for IntPtr.Zero.</summary>
    public void Target(IntPtr hwnd)
    {
        _target = hwnd;
        Follow();
        _follow.Enabled = hwnd != IntPtr.Zero;
    }

    private void Follow()
    {
        if (_target == IntPtr.Zero || !Win32.IsWindow(_target) || Win32.IsIconic(_target))
        {
            if (Visible) Hide();
            return;
        }
        var b = Win32.GetVisibleBounds(_target);
        b.Inflate(Thickness, Thickness);
        if (Bounds != b) { Bounds = b; Invalidate(); }
        if (!Visible) Show();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        using var pen = new Pen(_color, Thickness) { Alignment = System.Drawing.Drawing2D.PenAlignment.Inset };
        g.DrawRectangle(pen, 0, 0, Width, Height);
        // Label tab in the top-left corner, inside the frame.
        var size = TextRenderer.MeasureText(_label, _labelFont);
        var tab = new Rectangle(Thickness, Thickness, size.Width + 12, size.Height + 6);
        using var brush = new SolidBrush(_color);
        g.FillRectangle(brush, tab);
        TextRenderer.DrawText(g, _label, _labelFont, tab, Color.White,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) { _follow.Dispose(); _labelFont.Dispose(); }
        base.Dispose(disposing);
    }
}
