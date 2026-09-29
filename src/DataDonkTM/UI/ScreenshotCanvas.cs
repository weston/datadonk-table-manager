using System.Drawing.Drawing2D;
using DataDonkTM.Core;

namespace DataDonkTM.UI;

/// <summary>A marker drawn on the screenshot: either a rectangle or a point, in relative coordinates.</summary>
internal sealed record CanvasMarker(string Label, Color Color, RelRect? Rect, RelPoint? Point);

/// <summary>Shows a table screenshot and lets the user drag a rectangle or click a point on it.</summary>
internal sealed class ScreenshotCanvas : Control
{
    private Bitmap? _image;
    private float _zoom; // 0 = fit
    private Point? _dragStart;
    private Rectangle _rubber;

    public Func<IEnumerable<CanvasMarker>>? Markers { get; set; }
    /// <summary>True when the active item is a point (click) rather than a rectangle (drag).</summary>
    public bool PickPoint { get; set; }
    public event Action<RelRect>? RectDrawn;
    public event Action<RelPoint>? PointPicked;

    public ScreenshotCanvas()
    {
        DoubleBuffered = true;
        BackColor = Color.FromArgb(40, 40, 44);
        Cursor = Cursors.Cross;
    }

    public Bitmap? Image
    {
        get => _image;
        set { _image?.Dispose(); _image = value; UpdateSize(); Invalidate(); }
    }

    /// <summary>0 = fit to the panel, otherwise a scale factor.</summary>
    public float Zoom
    {
        get => _zoom;
        set { _zoom = value; UpdateSize(); Invalidate(); }
    }

    private void UpdateSize()
    {
        if (_zoom <= 0 || _image == null) { Dock = DockStyle.Fill; return; }
        Dock = DockStyle.None;
        Size = new Size((int)(_image.Width * _zoom), (int)(_image.Height * _zoom));
    }

    /// <summary>Where the image is drawn inside the control.</summary>
    private RectangleF ImageRect()
    {
        if (_image == null) return RectangleF.Empty;
        float scale = _zoom > 0 ? _zoom : Math.Min((float)Width / _image.Width, (float)Height / _image.Height);
        float w = _image.Width * scale, h = _image.Height * scale;
        return _zoom > 0 ? new RectangleF(0, 0, w, h) : new RectangleF((Width - w) / 2, (Height - h) / 2, w, h);
    }

    private PointF ToRel(Point p)
    {
        var r = ImageRect();
        return new PointF(Math.Clamp((p.X - r.X) / r.Width, 0, 1), Math.Clamp((p.Y - r.Y) / r.Height, 0, 1));
    }

    private RectangleF FromRel(RelRect rel)
    {
        var r = ImageRect();
        return new RectangleF(r.X + (float)rel.X * r.Width, r.Y + (float)rel.Y * r.Height, (float)rel.W * r.Width, (float)rel.H * r.Height);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (_image == null || e.Button != MouseButtons.Left) return;
        if (PickPoint)
        {
            var p = ToRel(e.Location);
            PointPicked?.Invoke(new RelPoint(p.X, p.Y));
            Invalidate();
            return;
        }
        _dragStart = e.Location;
        _rubber = new Rectangle(e.Location, Size.Empty);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_dragStart is not Point s) return;
        _rubber = Rectangle.FromLTRB(Math.Min(s.X, e.X), Math.Min(s.Y, e.Y), Math.Max(s.X, e.X), Math.Max(s.Y, e.Y));
        Invalidate();
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (_dragStart == null) return;
        _dragStart = null;
        if (_rubber.Width >= 3 && _rubber.Height >= 3)
        {
            var a = ToRel(_rubber.Location);
            var b = ToRel(new Point(_rubber.Right, _rubber.Bottom));
            RectDrawn?.Invoke(new RelRect { X = a.X, Y = a.Y, W = b.X - a.X, H = b.Y - a.Y });
        }
        _rubber = Rectangle.Empty;
        Invalidate();
    }

    protected override void OnResize(EventArgs e) { base.OnResize(e); Invalidate(); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        if (_image == null)
        {
            TextRenderer.DrawText(g, "Pick a table window above, then click 'Capture'.\nBest: a hand in progress where you are facing a bet.",
                Font, ClientRectangle, Color.Gainsboro, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak);
            return;
        }
        g.InterpolationMode = _zoom >= 1 ? InterpolationMode.NearestNeighbor : InterpolationMode.HighQualityBilinear;
        g.DrawImage(_image, ImageRect());
        g.SmoothingMode = SmoothingMode.AntiAlias;

        using var font = new Font("Segoe UI", 11, FontStyle.Bold, GraphicsUnit.Pixel);
        foreach (var m in Markers?.Invoke() ?? Enumerable.Empty<CanvasMarker>())
        {
            using var pen = new Pen(m.Color, 2);
            using var bg = new SolidBrush(Color.FromArgb(200, m.Color));
            PointF labelAt;
            if (m.Rect != null)
            {
                var r = FromRel(m.Rect);
                g.DrawRectangle(pen, r.X, r.Y, r.Width, r.Height);
                labelAt = new PointF(r.X, r.Y - 15);
            }
            else if (m.Point != null)
            {
                var ir = ImageRect();
                var p = new PointF(ir.X + (float)m.Point.X * ir.Width, ir.Y + (float)m.Point.Y * ir.Height);
                g.DrawEllipse(pen, p.X - 7, p.Y - 7, 14, 14);
                g.DrawLine(pen, p.X - 11, p.Y, p.X + 11, p.Y);
                g.DrawLine(pen, p.X, p.Y - 11, p.X, p.Y + 11);
                labelAt = new PointF(p.X + 10, p.Y - 18);
            }
            else continue;
            var size = g.MeasureString(m.Label, font);
            g.FillRectangle(bg, labelAt.X, labelAt.Y, size.Width, size.Height - 1);
            g.DrawString(m.Label, font, Brushes.White, labelAt);
        }

        if (_rubber.Width > 0)
        {
            using var pen = new Pen(Color.White, 1) { DashStyle = DashStyle.Dash };
            g.DrawRectangle(pen, _rubber);
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _image?.Dispose();
        base.Dispose(disposing);
    }
}
