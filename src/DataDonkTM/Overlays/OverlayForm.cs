using DataDonkTM.Core;
using DataDonkTM.Native;

namespace DataDonkTM.Overlays;

/// <summary>
/// Base for windows drawn on top of a poker table, kept directly above it in z-order (see <see cref="KeepAboveTable"/>).
/// The overlay never takes keyboard focus (WS_EX_NOACTIVATE). Right-button drag moves it.
/// The table is deliberately not made the overlay's owner: an owner in another process ties both programs'
/// input together, so tables froze (e.g. after another table closed) whenever that tangle went wrong.
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
        // Not setting ShowInTaskbar = false: WS_EX_TOOLWINDOW (below) already keeps overlays off the taskbar.
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
            return cp;
        }
    }

    /// <summary>Moves the overlay directly above its table in z-order, unless it already is above it.</summary>
    public void KeepAboveTable()
    {
        if (!Visible || !IsHandleCreated) return;
        // Already above the table? Walk up from the table (bounded: plenty of windows can sit in between).
        var w = TableHwnd;
        for (int i = 0; i < 200; i++)
        {
            w = Win32.GetWindow(w, Win32.GW_HWNDPREV);
            if (w == IntPtr.Zero) break;
            if (w == Handle) return;
        }
        // Insert just below whatever is above the table. If that is an always-on-top window (or nothing),
        // go to the top of the normal windows instead, so the overlay never becomes always-on-top itself.
        var above = Win32.GetWindow(TableHwnd, Win32.GW_HWNDPREV);
        if (above == IntPtr.Zero || (Win32.GetWindowLongPtr(above, Win32.GWL_EXSTYLE).ToInt64() & Win32.WS_EX_TOPMOST) != 0)
            above = Win32.HWND_TOP;
        Win32.SetWindowPos(Handle, above, 0, 0, 0, 0,
            Win32.SWP_NOMOVE | Win32.SWP_NOSIZE | Win32.SWP_NOACTIVATE | Win32.SWP_NOOWNERZORDER);
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
