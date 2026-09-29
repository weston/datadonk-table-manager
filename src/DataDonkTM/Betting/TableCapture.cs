using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using DataDonkTM.Core;
using DataDonkTM.Native;

namespace DataDonkTM.Betting;

/// <summary>Captures a table's client area into a bitmap (locally, nothing leaves the machine).</summary>
public static class TableCapture
{
    public static Bitmap? CaptureClient(IntPtr hwnd, CaptureMethod method)
    {
        var client = Win32.GetClientBounds(hwnd);
        if (client.Width <= 0 || client.Height <= 0) return null;

        if (method == CaptureMethod.PrintWindow)
        {
            var bmp = CaptureViaPrintWindow(hwnd, client);
            if (bmp != null && !IsMostlyBlack(bmp)) return bmp;
            bmp?.Dispose(); // Some GPU-rendered clients return black; fall back to the screen.
        }
        return CaptureScreen(client);
    }

    public static Bitmap CaptureScreen(Rectangle screenRect)
    {
        var bmp = new Bitmap(screenRect.Width, screenRect.Height, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bmp);
        g.CopyFromScreen(screenRect.Location, Point.Empty, screenRect.Size, CopyPixelOperation.SourceCopy);
        return bmp;
    }

    private static Bitmap? CaptureViaPrintWindow(IntPtr hwnd, Rectangle client)
    {
        var win = Win32.GetWindowBounds(hwnd);
        if (win.Width <= 0 || win.Height <= 0) return null;
        using var full = new Bitmap(win.Width, win.Height, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(full))
        {
            IntPtr hdc = g.GetHdc();
            bool ok;
            try { ok = Win32.PrintWindow(hwnd, hdc, Win32.PW_RENDERFULLCONTENT); }
            finally { g.ReleaseHdc(hdc); }
            if (!ok) return null;
        }
        var offset = new Rectangle(client.X - win.X, client.Y - win.Y, client.Width, client.Height);
        offset.Intersect(new Rectangle(Point.Empty, full.Size));
        if (offset.Width <= 0 || offset.Height <= 0) return null;
        return full.Clone(offset, PixelFormat.Format32bppArgb);
    }

    private static bool IsMostlyBlack(Bitmap bmp)
    {
        int dark = 0, total = 0;
        for (int y = 0; y < bmp.Height; y += Math.Max(1, bmp.Height / 12))
            for (int x = 0; x < bmp.Width; x += Math.Max(1, bmp.Width / 12))
            {
                var c = bmp.GetPixel(x, y);
                total++;
                if (c.R < 8 && c.G < 8 && c.B < 8) dark++;
            }
        return total > 0 && dark > total * 0.97;
    }

    public static Bitmap Crop(Bitmap src, RelRect rel)
    {
        var r = rel.ToPixels(src.Size);
        r.Intersect(new Rectangle(Point.Empty, src.Size));
        if (r.Width <= 0 || r.Height <= 0) r = new Rectangle(0, 0, 1, 1);
        return src.Clone(r, PixelFormat.Format32bppArgb);
    }

    // ---- Board (street) detection --------------------------------------------------

    private const int SigSize = 8;

    /// <summary>8×8 RGB thumbnail of a region, base64-encoded. Size-independent, so it survives resizing.</summary>
    public static string Signature(Bitmap region)
    {
        using var small = new Bitmap(SigSize, SigSize, PixelFormat.Format24bppRgb);
        using (var g = Graphics.FromImage(small))
        {
            g.InterpolationMode = InterpolationMode.HighQualityBilinear;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.DrawImage(region, new Rectangle(0, 0, SigSize, SigSize));
        }
        var bytes = new byte[SigSize * SigSize * 3];
        int i = 0;
        for (int y = 0; y < SigSize; y++)
            for (int x = 0; x < SigSize; x++)
            {
                var c = small.GetPixel(x, y);
                bytes[i++] = c.R; bytes[i++] = c.G; bytes[i++] = c.B;
            }
        return Convert.ToBase64String(bytes);
    }

    /// <summary>Mean absolute per-channel difference between two signatures (0-255).</summary>
    public static double SignatureDiff(string a, string b)
    {
        byte[] x, y;
        try { x = Convert.FromBase64String(a); y = Convert.FromBase64String(b); }
        catch (FormatException) { return 0; }
        if (x.Length != y.Length || x.Length == 0) return 0;
        long sum = 0;
        for (int i = 0; i < x.Length; i++) sum += Math.Abs(x[i] - y[i]);
        return (double)sum / x.Length;
    }

    /// <summary>
    /// True = postflop (board region differs from the calibrated empty board), false = preflop,
    /// null = site not calibrated for street detection.
    /// </summary>
    public static bool? IsPostflop(Bitmap client, SiteProfile site) =>
        IsPostflop(client, site, out _);

    public static bool? IsPostflop(Bitmap client, SiteProfile site, out double diff)
    {
        diff = 0;
        if (site.BoardRegion == null || string.IsNullOrEmpty(site.EmptyBoardSignature)) return null;
        using var crop = Crop(client, site.BoardRegion);
        diff = SignatureDiff(Signature(crop), site.EmptyBoardSignature);
        return diff > site.BoardDiffThreshold;
    }

    /// <summary>Cheap board check for polling: grabs only the board region from the screen.</summary>
    public static bool? IsPostflopFast(IntPtr hwnd, SiteProfile site)
    {
        if (site.BoardRegion == null || string.IsNullOrEmpty(site.EmptyBoardSignature)) return null;
        var client = Win32.GetClientBounds(hwnd);
        if (client.Width <= 0 || client.Height <= 0) return null;
        if (site.Capture == CaptureMethod.PrintWindow)
        {
            using var full = CaptureClient(hwnd, site.Capture);
            return full == null ? null : IsPostflop(full, site);
        }
        var r = site.BoardRegion.ToPixels(client.Size);
        r.Offset(client.Location);
        using var region = CaptureScreen(r);
        return SignatureDiff(Signature(region), site.EmptyBoardSignature) > site.BoardDiffThreshold;
    }
}
