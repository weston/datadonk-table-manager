using System.Drawing.Drawing2D;
using DataDonkTM.Core;
using DataDonkTM.Native;

namespace DataDonkTM.UI;

/// <summary>
/// Full-screen, semi-transparent editor covering every monitor. Slots are drawn where tables will go.
/// Drag to move, drag the corner to resize (keeps aspect ratio; hold Shift for free resize), edges snap
/// to monitors and other slots (hold Alt to disable). Right-click for options.
/// </summary>
internal sealed class LayoutEditorForm : Form
{
    private readonly List<Slot> _slots;
    private readonly TableManager _manager;
    private readonly Rectangle _virtual = SystemInformation.VirtualScreen;
    private readonly Form _toolbar;
    private List<Rectangle> _openTables;
    private int _selected = -1;
    private enum DragMode { None, Move, Resize }
    private DragMode _mode;
    private Point _dragStart;
    private Rectangle _startBounds;
    private Size _lastSize = new(640, 470);
    private const int HandleSize = 16;
    private const int SnapDistance = 10;

    public List<Slot> ResultSlots => _slots;

    public LayoutEditorForm(TilingProfile profile, TableManager manager)
    {
        _manager = manager;
        _slots = profile.Slots.Select(s => s.Clone()).ToList();
        _openTables = manager.CurrentTableRects();
        if (_slots.Count > 0) _lastSize = _slots[^1].Bounds.Size;
        else if (_openTables.Count > 0) _lastSize = _openTables[0].Size;

        Text = $"Layout editor - {profile.Name}";
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        AutoScaleMode = AutoScaleMode.None;
        Bounds = _virtual;
        TopMost = true;
        ShowInTaskbar = true;
        KeyPreview = true;
        DoubleBuffered = true;
        BackColor = Color.FromArgb(12, 14, 18);
        Opacity = 0.84;

        _toolbar = BuildToolbar(profile.Name);
    }

    protected override void WndProc(ref Message m)
    {
        // Stay in physical pixels when the window spans monitors with different scaling.
        if (m.Msg == Win32.WM_DPICHANGED) { m.Result = IntPtr.Zero; return; }
        base.WndProc(ref m);
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        Bounds = _virtual;
        var primary = Screen.PrimaryScreen!.WorkingArea;
        _toolbar.Location = new Point(primary.X + (primary.Width - _toolbar.Width) / 2, primary.Y + 20);
        _toolbar.Show(this);
    }

    private Form BuildToolbar(string profileName)
    {
        var f = new Form
        {
            Text = $"Layout: {profileName}", FormBorderStyle = FormBorderStyle.FixedToolWindow, TopMost = true,
            ShowInTaskbar = false, StartPosition = FormStartPosition.Manual, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ControlBox = false,
        };
        var row1 = Ui.Row(
            Ui.Btn("Save", (_, _) => Finish(true), 80),
            Ui.Btn("Cancel", (_, _) => Finish(false), 80),
            Ui.Btn("+ Playing slot", (_, _) => AddSlot(SlotType.Playing), 110),
            Ui.Btn("+ Observing slot", (_, _) => AddSlot(SlotType.Observing), 120),
            Ui.Btn("Grid on monitor", (_, _) => AddGrid(), 130),
            Ui.Btn("Capture open tables", (_, _) => CaptureTables(), 140),
            Ui.Btn("Clear all", (_, _) => { if (Confirm("Remove all slots?")) { _slots.Clear(); _selected = -1; Invalidate(); } }, 80));
        var help = Ui.Note("Drag = move · drag corner = resize (Shift = free ratio) · Alt = no snapping · double-click / P / O = playing↔observing · " +
                           "Del = delete · arrows = nudge · right-click = menu · slot numbers = fill order · Enter = save · Esc = cancel", 900);
        help.ForeColor = SystemColors.ControlText;
        var col = Ui.Column(row1, help);
        col.Padding = new Padding(6);
        f.Controls.Add(col);
        f.KeyPreview = true;
        f.KeyDown += (_, e) => OnKeyDown(e);
        return f;
    }

    private bool Confirm(string text) =>
        MessageBox.Show(_toolbar, text, "Layout editor", MessageBoxButtons.YesNo) == DialogResult.Yes;

    private void Finish(bool save)
    {
        DialogResult = save ? DialogResult.OK : DialogResult.Cancel;
        Close();
    }

    // ---- Coordinates ----------------------------------------------------------------

    private Rectangle ToClient(Rectangle screen) { screen.Offset(-_virtual.X, -_virtual.Y); return screen; }
    private Point ToScreen(Point client) => new(client.X + _virtual.X, client.Y + _virtual.Y);

    private int HitTest(Point screen, out bool onHandle)
    {
        onHandle = false;
        for (int i = _slots.Count - 1; i >= 0; i--)
        {
            var b = _slots[i].Bounds;
            if (!b.Contains(screen)) continue;
            onHandle = screen.X >= b.Right - HandleSize && screen.Y >= b.Bottom - HandleSize;
            return i;
        }
        return -1;
    }

    // ---- Editing actions ------------------------------------------------------------

    private void AddSlot(SlotType type, Point? screenCenter = null)
    {
        var center = screenCenter ?? Cursor.Position;
        var screen = Screen.FromPoint(center).WorkingArea;
        if (!screen.Contains(center)) center = new Point(screen.X + screen.Width / 2, screen.Y + screen.Height / 2);
        var r = new Rectangle(center.X - _lastSize.Width / 2, center.Y - _lastSize.Height / 2, _lastSize.Width, _lastSize.Height);
        // If toolbar-triggered and something is already there, cascade.
        while (_slots.Any(s => s.X == r.X && s.Y == r.Y)) r.Offset(30, 30);
        r = KeepOnScreen(r);
        _slots.Add(new Slot { Type = type, Bounds = r });
        _selected = _slots.Count - 1;
        Invalidate();
    }

    private static Rectangle KeepOnScreen(Rectangle r)
    {
        var wa = Screen.FromRectangle(r).WorkingArea;
        r.X = Math.Clamp(r.X, wa.X, Math.Max(wa.X, wa.Right - r.Width));
        r.Y = Math.Clamp(r.Y, wa.Y, Math.Max(wa.Y, wa.Bottom - r.Height));
        return r;
    }

    private void CaptureTables()
    {
        _openTables = _manager.CurrentTableRects();
        if (_openTables.Count == 0)
        {
            MessageBox.Show(_toolbar, "No poker tables detected. Make sure the site is enabled in the Sites tab and tables are open.", "Capture");
            return;
        }
        int added = 0;
        foreach (var r in _openTables.OrderBy(r => r.Y / 50).ThenBy(r => r.X))
        {
            if (_slots.Any(s => Math.Abs(s.X - r.X) < 8 && Math.Abs(s.Y - r.Y) < 8)) continue;
            _slots.Add(new Slot { Type = SlotType.Playing, Bounds = r });
            added++;
        }
        _lastSize = _openTables[0].Size;
        MessageBox.Show(_toolbar, $"Added {added} slot(s) at the current table positions.", "Capture");
        Invalidate();
    }

    private void AddGrid()
    {
        double ratio = _openTables.Count > 0 ? (double)_openTables[0].Width / _openTables[0].Height
            : _lastSize.Height > 0 ? (double)_lastSize.Width / _lastSize.Height : 1.38;
        using var dlg = new GridDialog(ratio);
        if (dlg.ShowDialog(_toolbar) != DialogResult.OK) return;
        var screen = Screen.AllScreens[dlg.MonitorIndex];
        var area = dlg.UseWorkingArea ? screen.WorkingArea : screen.Bounds;
        if (dlg.ReplaceExisting) _slots.RemoveAll(s => area.Contains(new Point(s.X + s.Width / 2, s.Y + s.Height / 2)));
        var grid = SlotLogic.Grid(area, dlg.Rows, dlg.Cols, dlg.Gap, dlg.AspectRatio, dlg.Type);
        _slots.AddRange(grid);
        if (grid.Count > 0) _lastSize = grid[0].Bounds.Size;
        _selected = -1;
        Invalidate();
    }

    private void ToggleType(int i)
    {
        if (i < 0 || i >= _slots.Count) return;
        _slots[i].Type = _slots[i].Type == SlotType.Playing ? SlotType.Observing : SlotType.Playing;
        Invalidate();
    }

    private void MoveOrder(int i, int delta)
    {
        int j = i + delta;
        if (i < 0 || j < 0 || j >= _slots.Count) return;
        (_slots[i], _slots[j]) = (_slots[j], _slots[i]);
        _selected = j;
        Invalidate();
    }

    private void SetExactSize(int i)
    {
        var s = _slots[i];
        var text = Ui.Prompt(_toolbar, "Slot size", "Width x Height (pixels), e.g. 800x580:", $"{s.Width}x{s.Height}");
        if (text == null) return;
        var parts = text.ToLowerInvariant().Split('x', '×', ',', ' ').Where(p => p.Length > 0).ToArray();
        if (parts.Length == 2 && int.TryParse(parts[0], out int w) && int.TryParse(parts[1], out int h) && w >= 100 && h >= 80)
        {
            s.Width = w; s.Height = h;
            _lastSize = new Size(w, h);
            Invalidate();
        }
    }

    // ---- Snapping -------------------------------------------------------------------

    private (List<int> xs, List<int> ys) SnapLines(int except)
    {
        var xs = new List<int>();
        var ys = new List<int>();
        foreach (var s in Screen.AllScreens)
        {
            foreach (var r in new[] { s.Bounds, s.WorkingArea })
            {
                xs.Add(r.Left); xs.Add(r.Right);
                ys.Add(r.Top); ys.Add(r.Bottom);
            }
        }
        for (int i = 0; i < _slots.Count; i++)
        {
            if (i == except) continue;
            var b = _slots[i].Bounds;
            xs.Add(b.Left); xs.Add(b.Right);
            ys.Add(b.Top); ys.Add(b.Bottom);
        }
        return (xs, ys);
    }

    private static int BestSnap(IEnumerable<int> edges, List<int> lines)
    {
        int best = 0, bestAbs = SnapDistance + 1;
        foreach (var e in edges)
            foreach (var l in lines)
            {
                int d = l - e;
                if (Math.Abs(d) < bestAbs) { bestAbs = Math.Abs(d); best = d; }
            }
        return bestAbs <= SnapDistance ? best : 0;
    }

    // ---- Mouse ----------------------------------------------------------------------

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        var screen = ToScreen(e.Location);
        int hit = HitTest(screen, out bool onHandle);
        if (e.Button == MouseButtons.Left)
        {
            _selected = hit;
            if (hit >= 0)
            {
                _mode = onHandle ? DragMode.Resize : DragMode.Move;
                _dragStart = screen;
                _startBounds = _slots[hit].Bounds;
                Capture = true;
            }
            Invalidate();
        }
        else if (e.Button == MouseButtons.Right)
        {
            _selected = hit;
            Invalidate();
            ShowContextMenu(e.Location, screen, hit);
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var screen = ToScreen(e.Location);
        if (_mode == DragMode.None)
        {
            HitTest(screen, out bool onHandle);
            Cursor = onHandle ? Cursors.SizeNWSE : Cursors.Default;
            return;
        }
        if (_selected < 0) return;

        int dx = screen.X - _dragStart.X, dy = screen.Y - _dragStart.Y;
        bool snap = (ModifierKeys & Keys.Alt) == 0;
        var r = _startBounds;
        if (_mode == DragMode.Move)
        {
            r.Offset(dx, dy);
            if (snap)
            {
                var (xs, ys) = SnapLines(_selected);
                r.Offset(BestSnap(new[] { r.Left, r.Right }, xs), BestSnap(new[] { r.Top, r.Bottom }, ys));
            }
        }
        else
        {
            bool keepRatio = (ModifierKeys & Keys.Shift) == 0;
            double ratio = (double)_startBounds.Width / Math.Max(1, _startBounds.Height);
            int w = Math.Max(120, _startBounds.Width + dx);
            if (snap)
            {
                var (xs, _) = SnapLines(_selected);
                w += BestSnap(new[] { r.X + w }, xs);
            }
            int h = keepRatio ? (int)Math.Round(w / ratio) : Math.Max(90, _startBounds.Height + dy);
            if (snap && !keepRatio)
            {
                var (_, ys) = SnapLines(_selected);
                h += BestSnap(new[] { r.Y + h }, ys);
            }
            r.Width = w; r.Height = h;
        }
        _slots[_selected].Bounds = r;
        Invalidate();
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (_mode != DragMode.None && _selected >= 0) _lastSize = _slots[_selected].Bounds.Size;
        _mode = DragMode.None;
        Capture = false;
    }

    protected override void OnMouseDoubleClick(MouseEventArgs e)
    {
        base.OnMouseDoubleClick(e);
        int hit = HitTest(ToScreen(e.Location), out _);
        if (hit >= 0) ToggleType(hit);
        else if (e.Button == MouseButtons.Left) AddSlot(SlotType.Playing, ToScreen(e.Location));
    }

    private void ShowContextMenu(Point clientPoint, Point screen, int hit)
    {
        var menu = new ContextMenuStrip();
        if (hit >= 0)
        {
            var s = _slots[hit];
            menu.Items.Add(s.Type == SlotType.Playing ? "Make observing slot" : "Make playing slot", null, (_, _) => ToggleType(hit));
            menu.Items.Add("Duplicate", null, (_, _) =>
            {
                var copy = s.Clone();
                copy.X += 30; copy.Y += 30;
                _slots.Add(copy);
                _selected = _slots.Count - 1;
                Invalidate();
            });
            menu.Items.Add("Set exact size", null, (_, _) => SetExactSize(hit));
            menu.Items.Add("Apply this size to all slots", null, (_, _) =>
            {
                foreach (var o in _slots) { o.Width = s.Width; o.Height = s.Height; }
                Invalidate();
            });
            menu.Items.Add("Fill earlier (lower number)", null, (_, _) => MoveOrder(hit, -1));
            menu.Items.Add("Fill later (higher number)", null, (_, _) => MoveOrder(hit, 1));
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Delete", null, (_, _) => { _slots.RemoveAt(hit); _selected = -1; Invalidate(); });
        }
        else
        {
            menu.Items.Add("Add playing slot here", null, (_, _) => AddSlot(SlotType.Playing, screen));
            menu.Items.Add("Add observing slot here", null, (_, _) => AddSlot(SlotType.Observing, screen));
            menu.Items.Add("Grid on monitor", null, (_, _) => AddGrid());
        }
        menu.Closed += (_, _) => BeginInvoke(menu.Dispose);
        menu.Show(this, clientPoint);
    }

    // ---- Keyboard -------------------------------------------------------------------

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        switch (e.KeyCode)
        {
            case Keys.Escape: Finish(false); return;
            case Keys.Enter: Finish(true); return;
            case Keys.S when e.Control: Finish(true); return;
        }
        if (_selected < 0 || _selected >= _slots.Count) return;
        var s = _slots[_selected];
        int step = e.Shift ? 10 : 1;
        switch (e.KeyCode)
        {
            case Keys.Delete: case Keys.Back: _slots.RemoveAt(_selected); _selected = -1; break;
            case Keys.Left: s.X -= step; break;
            case Keys.Right: s.X += step; break;
            case Keys.Up: s.Y -= step; break;
            case Keys.Down: s.Y += step; break;
            case Keys.P: s.Type = SlotType.Playing; break;
            case Keys.O: s.Type = SlotType.Observing; break;
            default: return;
        }
        e.Handled = true;
        Invalidate();
    }

    // ---- Drawing --------------------------------------------------------------------

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;

        using var monitorPen = new Pen(Color.FromArgb(120, 125, 135), 2);
        using var workPen = new Pen(Color.FromArgb(80, 85, 95), 1) { DashStyle = DashStyle.Dash };
        using var small = new Font("Segoe UI", 14, GraphicsUnit.Pixel);
        using var big = new Font("Segoe UI", 34, FontStyle.Bold, GraphicsUnit.Pixel);
        using var mid = new Font("Segoe UI", 16, FontStyle.Bold, GraphicsUnit.Pixel);
        var screens = Screen.AllScreens;
        for (int i = 0; i < screens.Length; i++)
        {
            var b = ToClient(screens[i].Bounds);
            b.Inflate(-1, -1);
            g.DrawRectangle(monitorPen, b);
            g.DrawRectangle(workPen, ToClient(screens[i].WorkingArea));
            g.DrawString($"Monitor {i + 1}  {screens[i].Bounds.Width}×{screens[i].Bounds.Height}{(screens[i].Primary ? "  (primary)" : "")}",
                mid, Brushes.Gainsboro, b.X + 10, b.Bottom - 60);
        }

        // Where tables currently are, for reference.
        using (var tablePen = new Pen(Color.FromArgb(200, 190, 60), 1) { DashStyle = DashStyle.Dot })
            foreach (var r in _openTables) g.DrawRectangle(tablePen, ToClient(r));

        var center = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
        for (int i = 0; i < _slots.Count; i++)
        {
            var s = _slots[i];
            var r = ToClient(s.Bounds);
            bool sel = i == _selected;
            var baseColor = s.Type == SlotType.Playing ? Color.FromArgb(34, 139, 84) : Color.FromArgb(52, 101, 180);
            using (var fill = new SolidBrush(sel ? ControlPaint.Light(baseColor, 0.3f) : baseColor))
                g.FillRectangle(fill, r);
            using (var border = new Pen(sel ? Color.White : ControlPaint.Dark(baseColor, 0.2f), sel ? 3 : 1))
                g.DrawRectangle(border, r.X, r.Y, r.Width - 1, r.Height - 1);

            g.DrawString($"#{i + 1}", big, Brushes.White, new RectangleF(r.X, r.Y - 18, r.Width, r.Height), center);
            g.DrawString(s.Type == SlotType.Playing ? "PLAYING" : "OBSERVING", mid, Brushes.White,
                new RectangleF(r.X, r.Y + 26, r.Width, r.Height), center);
            g.DrawString($"{s.Width}×{s.Height}  @ {s.X},{s.Y}", small, Brushes.WhiteSmoke, r.X + 6, r.Y + 4);

            using var handleBrush = new SolidBrush(Color.FromArgb(230, 255, 255, 255));
            g.FillPolygon(handleBrush, new[] { new Point(r.Right - 1, r.Bottom - HandleSize), new Point(r.Right - 1, r.Bottom - 1), new Point(r.Right - HandleSize, r.Bottom - 1) });
        }

        if (_slots.Count == 0)
        {
            var p = ToClient(Screen.PrimaryScreen!.Bounds);
            g.DrawString("No slots yet.\nUse 'Grid on monitor', 'Capture open tables', or double-click anywhere to add a slot.",
                mid, Brushes.White, p, center);
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _toolbar.Dispose();
        base.Dispose(disposing);
    }
}

/// <summary>Generates a rows × cols grid of slots on one monitor.</summary>
internal sealed class GridDialog : Form
{
    private readonly ComboBox _monitor = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 260 };
    private readonly NumericUpDown _rows = Ui.Num(1, 10, 2);
    private readonly NumericUpDown _cols = Ui.Num(1, 10, 2);
    private readonly NumericUpDown _gap = Ui.Num(0, 200, 0);
    private readonly NumericUpDown _ratio;
    private readonly CheckBox _keepRatio = Ui.Chk("Keep table aspect ratio (width ÷ height)", true);
    private readonly CheckBox _workArea = Ui.Chk("Avoid the taskbar (use working area)", true);
    private readonly CheckBox _replace = Ui.Chk("Replace existing slots on this monitor", true);
    private readonly ComboBox _type = Ui.Combo(SlotType.Playing);

    public int MonitorIndex => _monitor.SelectedIndex;
    public int Rows => (int)_rows.Value;
    public int Cols => (int)_cols.Value;
    public int Gap => (int)_gap.Value;
    public double? AspectRatio => _keepRatio.Checked ? (double)_ratio.Value : null;
    public bool UseWorkingArea => _workArea.Checked;
    public bool ReplaceExisting => _replace.Checked;
    public SlotType Type => (SlotType)_type.SelectedItem!;

    public GridDialog(double ratio)
    {
        Text = "Grid on monitor";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = MaximizeBox = false;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        TopMost = true;
        _ratio = Ui.Num(0.5m, 3m, (decimal)Math.Round(ratio, 3), 3, 0.01m);

        var screens = Screen.AllScreens;
        for (int i = 0; i < screens.Length; i++)
            _monitor.Items.Add($"Monitor {i + 1}: {screens[i].Bounds.Width}×{screens[i].Bounds.Height}{(screens[i].Primary ? " (primary)" : "")}");
        int cur = Array.FindIndex(screens, s => s.Bounds.Contains(Cursor.Position));
        _monitor.SelectedIndex = Math.Max(0, cur);

        var ok = new Button { Text = "Add grid", DialogResult = DialogResult.OK, Width = 90 };
        var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Width = 90 };
        AcceptButton = ok; CancelButton = cancel;

        var col = Ui.Column(
            Ui.Row(Ui.Lbl("Monitor"), _monitor),
            Ui.Row(Ui.Lbl("Rows"), _rows, Ui.Lbl("Columns"), _cols, Ui.Lbl("Gap px"), _gap),
            Ui.Row(Ui.Lbl("Slot type"), _type),
            _keepRatio, Ui.Row(Ui.Lbl("Aspect ratio"), _ratio),
            _workArea, _replace,
            Ui.Row(ok, cancel));
        col.Padding = new Padding(10);
        Controls.Add(col);
    }
}
