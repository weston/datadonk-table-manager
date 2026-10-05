using System.Drawing.Drawing2D;

namespace DataDonkTM.Overlays;

/// <summary>Small "Report bug" button on a table. Click saves a bug report; right-drag moves it.</summary>
internal sealed class ReportOverlay : OverlayForm
{
    private readonly System.Windows.Forms.Timer _reset = new() { Interval = 2500 };
    private string _text = Idle;
    private bool _hover;
    private const string Idle = "Report bug";

    public event Action? ReportRequested;
    public event Action? ResetPositionRequested;
    public event Action? OpenFolderRequested;

    public ReportOverlay(IntPtr tableHwnd) : base(tableHwnd)
    {
        Size = new Size(78, 20);
        Cursor = Cursors.Hand;
        _reset.Tick += (_, _) => { _reset.Stop(); _text = Idle; Invalidate(); };
    }

    /// <summary>Shown on the button until it resets, e.g. "Saving…" or "Saved ✔".</summary>
    public void SetStatus(string text, bool resetSoon)
    {
        _text = text;
        _reset.Stop();
        if (resetSoon) _reset.Start();
        Invalidate();
    }

    protected override void OnMouseClick(MouseEventArgs e)
    {
        base.OnMouseClick(e);
        if (e.Button == MouseButtons.Left && _text == Idle) ReportRequested?.Invoke();
    }

    protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); _hover = true; Invalidate(); }
    protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); _hover = false; Invalidate(); }

    protected override void OnRightClick(Point p)
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add("Open bug reports folder", null, (_, _) => OpenFolderRequested?.Invoke());
        menu.Items.Add("Reset position", null, (_, _) => ResetPositionRequested?.Invoke());
        menu.Closed += (_, _) => BeginInvoke(menu.Dispose);
        menu.Show(this, p);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
        g.Clear(Color.FromArgb(24, 26, 30));
        using (var bg = new SolidBrush(_hover && _text == Idle ? Color.FromArgb(120, 50, 50) : Color.FromArgb(80, 36, 40)))
            FillRounded(g, bg, new Rectangle(1, 1, Width - 2, Height - 2), 4);
        using var font = new Font("Segoe UI", 11, FontStyle.Bold, GraphicsUnit.Pixel);
        var fmt = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
        g.DrawString(_text, font, Brushes.White, new RectangleF(0, 0, Width, Height + 1), fmt);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _reset.Dispose();
        base.Dispose(disposing);
    }
}
