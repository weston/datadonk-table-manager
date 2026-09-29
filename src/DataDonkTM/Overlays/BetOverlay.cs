using System.Drawing.Drawing2D;
using DataDonkTM.Betting;
using DataDonkTM.Core;

namespace DataDonkTM.Overlays;

/// <summary>
/// Up to 8 bet-size buttons (2 rows × 4). Shows the preflop or postflop set depending on whether the
/// board region shows cards. Right-click the panel to force a street.
/// </summary>
internal sealed class BetOverlay : OverlayForm
{
    private enum StreetOverride { Auto, Preflop, Postflop }

    private readonly SiteProfile _site;
    private readonly BetSettings _settings;
    private readonly System.Windows.Forms.Timer _poll = new() { Interval = 400 };
    private readonly ToolTip _tip = new();
    private bool? _detectedPostflop;
    private StreetOverride _override = StreetOverride.Auto;
    private int _hover = -1;
    private int _busy = -1;
    private const int Gap = 2;

    public event Action? ResetPositionRequested;

    public BetOverlay(IntPtr tableHwnd, SiteProfile site, BetSettings settings) : base(tableHwnd)
    {
        _site = site;
        _settings = settings;
        _poll.Tick += (_, _) => PollStreet();
        UpdateLayoutSize();
    }

    private bool IsPostflop => _override switch
    {
        StreetOverride.Preflop => false,
        StreetOverride.Postflop => true,
        _ => _detectedPostflop ?? false,
    };

    private List<BetButton> Buttons => (IsPostflop ? _settings.Postflop : _settings.Preflop).Take(BetSettings.MaxButtons).ToList();

    private void UpdateLayoutSize()
    {
        int n = Math.Max(1, Buttons.Count);
        int cols = Math.Min(4, n), rows = n > 4 ? 2 : 1;
        var size = new Size(
            cols * (_settings.ButtonWidth + Gap) + Gap,
            rows * (_settings.ButtonHeight + Gap) + Gap);
        if (Size != size) Size = size;
        Invalidate();
    }

    private Rectangle ButtonRect(int i)
    {
        int col = i % 4, row = i / 4;
        return new Rectangle(Gap + col * (_settings.ButtonWidth + Gap), Gap + row * (_settings.ButtonHeight + Gap),
            _settings.ButtonWidth, _settings.ButtonHeight);
    }

    private int HitTest(Point p)
    {
        var buttons = Buttons;
        for (int i = 0; i < buttons.Count; i++)
            if (ButtonRect(i).Contains(p)) return i;
        return -1;
    }

    protected override void OnVisibleChanged(EventArgs e)
    {
        base.OnVisibleChanged(e);
        if (Visible) { PollStreet(); _poll.Start(); } else _poll.Stop();
    }

    private void PollStreet()
    {
        bool? detected;
        try { detected = TableCapture.IsPostflopFast(TableHwnd, _site); }
        catch { return; }
        if (detected == _detectedPostflop) return;
        bool wasPostflop = IsPostflop;
        _detectedPostflop = detected;
        // A manual override lasts until the detector sees the street change (new street / new hand).
        if (detected != null) _override = StreetOverride.Auto;
        if (wasPostflop != IsPostflop) UpdateLayoutSize();
        Invalidate();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        int h = HitTest(e.Location);
        if (h != _hover) { _hover = h; Invalidate(); }
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        _hover = -1;
        Invalidate();
    }

    protected override async void OnMouseClick(MouseEventArgs e)
    {
        base.OnMouseClick(e);
        if (e.Button != MouseButtons.Left) return;
        int i = HitTest(e.Location);
        if (i < 0 || _busy >= 0) return;
        var button = Buttons[i];
        _busy = i;
        Invalidate();
        var (ok, message) = await BetExecutor.ExecuteAsync(TableHwnd, _site, button, _settings);
        _busy = -1;
        Invalidate();
        if (!ok) _tip.Show("⚠ " + message, this, 0, -40, 5000);
    }

    protected override void OnRightClick(Point p)
    {
        var menu = new ContextMenuStrip();
        void AddStreet(string text, StreetOverride o) =>
            menu.Items.Add(new ToolStripMenuItem(text, null, (_, _) => { _override = o; UpdateLayoutSize(); }) { Checked = _override == o });
        AddStreet("Street: auto-detect", StreetOverride.Auto);
        AddStreet("Force preflop buttons", StreetOverride.Preflop);
        AddStreet("Force postflop buttons", StreetOverride.Postflop);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Reset position", null, (_, _) => ResetPositionRequested?.Invoke());
        menu.Closed += (_, _) => BeginInvoke(menu.Dispose);
        menu.Show(this, p);
    }

    public void SettingsChanged() => UpdateLayoutSize();

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
        g.Clear(Color.FromArgb(24, 26, 30));
        var fmt = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };

        var buttons = Buttons;
        float fontPx = Math.Clamp(_settings.ButtonHeight * 0.5f, 8, 20);
        using var font = new Font("Segoe UI", fontPx, FontStyle.Bold, GraphicsUnit.Pixel);
        for (int i = 0; i < buttons.Count; i++)
        {
            var r = ButtonRect(i);
            var fill = i == _busy ? Color.FromArgb(70, 110, 190)
                : i == _hover ? Color.FromArgb(70, 76, 88)
                : Color.FromArgb(46, 50, 58);
            using var b = new SolidBrush(fill);
            FillRounded(g, b, r, 3);
            g.DrawString(buttons[i].DisplayText, font, Brushes.White, new RectangleF(r.X, r.Y, r.Width, r.Height + 1), fmt);
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) { _poll.Dispose(); _tip.Dispose(); }
        base.Dispose(disposing);
    }
}
