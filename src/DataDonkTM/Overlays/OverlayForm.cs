using DataDonkTM.Core;
using DataDonkTM.Native;

namespace DataDonkTM.Overlays;

/// <summary>
/// Base for windows drawn on top of a poker table. The table window is set as the Win32 owner, so the
/// overlay always stays directly above its table in z-order and hides when it is minimised. The overlay
/// never takes keyboard focus (WS_EX_NOACTIVATE). Right-button drag moves it.
/// </summary>
internal abstract class OverlayForm : Form
{
    protected readonly IntPtr TableHwnd;
    private Point? _rightDownAt;
    private Point _dragOffset;
    private bool _dragging;

    /// <summary>Raised after a right-drag, with the new top-left corner in screen coordinates.</summary>
    public event Action<Point>? DragFinished;

    protected OverlayForm(IntPtr tableHwnd)
    {
        TableHwnd = tableHwnd;
        FormBorderStyle = FormBorderStyle.None;
        // Deliberately not setting ShowInTaskbar = false: WinForms would then re-own the window to a hidden
        // helper window. WS_EX_TOOLWINDOW (below) already keeps overlays off the taskbar.
        StartPosition = FormStartPosition.Manual;
        AutoScaleMode = AutoScaleMode.None;
        DoubleBuffered = true;
        BackColor = Color.FromArgb(24, 26, 30);
    }

    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= Win32.WS_EX_NOACTIVATE | Win32.WS_EX_TOOLWINDOW;
            cp.Parent = TableHwnd; // top-level window with a parent handle = owned window
            return cp;
        }
    }

    protected override void SetVisibleCore(bool value)
    {
        base.SetVisibleCore(value);
        // WinForms resets the owner while showing a form that has no WinForms Owner, so re-apply it:
        // the table must be the owner to keep the overlay just above it in z-order.
        if (value && IsHandleCreated && Win32.GetWindow(Handle, Win32.GW_OWNER) != TableHwnd)
            Win32.SetWindowLongPtr(Handle, Win32.GWLP_HWNDPARENT, TableHwnd);
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == Win32.WM_MOUSEACTIVATE) { m.Result = (IntPtr)Win32.MA_NOACTIVATE; return; }
        if (m.Msg == Win32.WM_DPICHANGED) { m.Result = IntPtr.Zero; return; } // sizes are in physical pixels
        base.WndProc(ref m);
    }

    /// <summary>Places the overlay's top-left at a relative point of the table's client area, kept inside it.</summary>
    public void PlaceOn(Rectangle client, RelPoint pos)
    {
        if (_dragging) return;
        int x = client.X + (int)Math.Round(pos.X * client.Width);
        int y = client.Y + (int)Math.Round(pos.Y * client.Height);
        x = Math.Clamp(x, client.X, Math.Max(client.X, client.Right - Width));
        y = Math.Clamp(y, client.Y, Math.Max(client.Y, client.Bottom - Height));
        if (Location != new Point(x, y)) Location = new Point(x, y);
    }

    public static RelPoint ToRelative(Rectangle client, Point screenTopLeft) => new(
        client.Width > 0 ? Math.Clamp((double)(screenTopLeft.X - client.X) / client.Width, 0, 1) : 0,
        client.Height > 0 ? Math.Clamp((double)(screenTopLeft.Y - client.Y) / client.Height, 0, 1) : 0);

    protected virtual void OnRightClick(Point clientPoint) { }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button != MouseButtons.Right) return;
        _rightDownAt = Cursor.Position;
        _dragOffset = new Point(Cursor.Position.X - Left, Cursor.Position.Y - Top);
        Capture = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_rightDownAt is not Point start) return;
        var p = Cursor.Position;
        if (!_dragging && Math.Abs(p.X - start.X) + Math.Abs(p.Y - start.Y) < 4) return;
        _dragging = true;
        Location = new Point(p.X - _dragOffset.X, p.Y - _dragOffset.Y);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (e.Button != MouseButtons.Right || _rightDownAt == null) return;
        Capture = false;
        bool wasDrag = _dragging;
        _dragging = false;
        _rightDownAt = null;
        if (wasDrag) DragFinished?.Invoke(Location);
        else OnRightClick(e.Location);
    }

    protected static void FillRounded(Graphics g, Brush b, Rectangle r, int radius)
    {
        using var path = new System.Drawing.Drawing2D.GraphicsPath();
        int d = radius * 2;
        path.AddArc(r.X, r.Y, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        g.FillPath(b, path);
    }
}
