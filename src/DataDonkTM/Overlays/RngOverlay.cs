using System.Drawing.Drawing2D;
using System.Security.Cryptography;

namespace DataDonkTM.Overlays;

/// <summary>Click → uniform random number 1-100 (cryptographic RNG). Right-drag to move, mouse wheel to resize.</summary>
internal sealed class RngOverlay : OverlayForm
{
    private int? _value;
    private readonly System.Windows.Forms.Timer _flash = new() { Interval = 180 };
    private bool _flashing;

    /// <summary>+1 / -1 size steps requested by the wheel or context menu.</summary>
    public event Action<int>? ResizeRequested;
    public event Action? ResetPositionRequested;

    public RngOverlay(IntPtr tableHwnd, int size) : base(tableHwnd)
    {
        SetBoxSize(size);
        Cursor = Cursors.Hand;
        _flash.Tick += (_, _) => { _flash.Stop(); _flashing = false; Invalidate(); };
    }

    public void SetBoxSize(int height)
    {
        height = Math.Clamp(height, 16, 120);
        Size = new Size((int)(height * 1.5), height);
        Invalidate();
    }

    protected override void OnMouseClick(MouseEventArgs e)
    {
        base.OnMouseClick(e);
        if (e.Button != MouseButtons.Left) return;
        _value = RandomNumberGenerator.GetInt32(1, 101);
        _flashing = true;
        _flash.Stop(); _flash.Start();
        Invalidate();
    }

    protected override void OnMouseDoubleClick(MouseEventArgs e) => OnMouseClick(e); // fast re-rolls

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);
        ResizeRequested?.Invoke(e.Delta > 0 ? 1 : -1);
    }

    protected override void OnRightClick(Point p)
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add("Larger", null, (_, _) => ResizeRequested?.Invoke(1));
        menu.Items.Add("Smaller", null, (_, _) => ResizeRequested?.Invoke(-1));
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
        var rect = new Rectangle(1, 1, Width - 2, Height - 2);
        using (var bg = new SolidBrush(_flashing ? Color.FromArgb(70, 110, 190) : Color.FromArgb(40, 44, 52)))
            FillRounded(g, bg, rect, 4);

        var text = _value?.ToString() ?? "RNG";
        float px = _value == null ? Height * 0.36f : Height * 0.58f;
        using var font = new Font("Segoe UI", px, FontStyle.Bold, GraphicsUnit.Pixel);
        using var fg = new SolidBrush(_value == null ? Color.FromArgb(150, 155, 165) : Color.White);
        var fmt = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
        g.DrawString(text, font, fg, new RectangleF(0, 0, Width, Height + 1), fmt);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _flash.Dispose();
        base.Dispose(disposing);
    }
}
