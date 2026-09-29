using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Globalization;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;

namespace DataDonkTM.Betting;

/// <summary>
/// Text recognition using the OCR engine built into Windows 10/11 (Windows.Media.Ocr).
/// Runs fully offline; no third-party OCR library or service is involved.
/// </summary>
public static class OcrService
{
    private static OcrEngine? _engine;

    private static OcrEngine Engine =>
        _engine ??= OcrEngine.TryCreateFromUserProfileLanguages()
                    ?? OcrEngine.TryCreateFromLanguage(new Language("en-US"))
                    ?? throw new InvalidOperationException(
                        "Windows OCR is not available. Install a language pack with OCR support (Settings → Time & language → Language).");

    public static async Task<string> ReadTextAsync(Bitmap region)
    {
        using var prepared = Prepare(region);
        using var sb = ToSoftwareBitmap(prepared);
        var result = await Engine.RecognizeAsync(sb);
        return string.Join(" ", result.Lines.Select(l => l.Text)).Trim();
    }

    /// <summary>Upscales small text, converts to grayscale and makes the text dark-on-light, which Windows OCR reads best.</summary>
    private static Bitmap Prepare(Bitmap src)
    {
        double scale = Math.Clamp(80.0 / Math.Max(1, src.Height), 1, 5);
        int max = (int)OcrEngine.MaxImageDimension - 40;
        scale = Math.Min(scale, Math.Min((double)max / src.Width, (double)max / src.Height));
        int w = Math.Max(1, (int)(src.Width * scale)), h = Math.Max(1, (int)(src.Height * scale));
        const int pad = 16;

        var dst = new Bitmap(w + pad * 2, h + pad * 2, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(dst))
        {
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.Clear(Color.White);
            g.DrawImage(src, new Rectangle(pad, pad, w, h));
        }

        var data = dst.LockBits(new Rectangle(0, 0, dst.Width, dst.Height), ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);
        try
        {
            var px = new byte[data.Stride * data.Height];
            System.Runtime.InteropServices.Marshal.Copy(data.Scan0, px, 0, px.Length);
            long sum = 0; int n = 0;
            for (int y = pad; y < pad + h; y++)
                for (int x = pad; x < pad + w; x++)
                {
                    int i = y * data.Stride + x * 4;
                    sum += (px[i] * 114 + px[i + 1] * 587 + px[i + 2] * 299) / 1000;
                    n++;
                }
            bool invert = n > 0 && sum / n < 128; // mostly dark → light text on dark background
            for (int i = 0; i < px.Length; i += 4)
            {
                int l = (px[i] * 114 + px[i + 1] * 587 + px[i + 2] * 299) / 1000;
                if (invert) l = 255 - l;
                px[i] = px[i + 1] = px[i + 2] = (byte)l;
                px[i + 3] = 255;
            }
            // The white padding was inverted too; restore it.
            if (invert)
                for (int y = 0; y < dst.Height; y++)
                    for (int x = 0; x < dst.Width; x++)
                        if (y < pad || y >= pad + h || x < pad || x >= pad + w)
                        {
                            int i = y * data.Stride + x * 4;
                            px[i] = px[i + 1] = px[i + 2] = 255;
                        }
            System.Runtime.InteropServices.Marshal.Copy(px, 0, data.Scan0, px.Length);
        }
        finally { dst.UnlockBits(data); }
        return dst;
    }

    private static SoftwareBitmap ToSoftwareBitmap(Bitmap bmp)
    {
        var data = bmp.LockBits(new Rectangle(0, 0, bmp.Width, bmp.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            var bytes = new byte[bmp.Width * bmp.Height * 4];
            for (int y = 0; y < bmp.Height; y++)
                System.Runtime.InteropServices.Marshal.Copy(data.Scan0 + y * data.Stride, bytes, y * bmp.Width * 4, bmp.Width * 4);
            return SoftwareBitmap.CreateCopyFromBuffer(bytes.AsBuffer(), BitmapPixelFormat.Bgra8, bmp.Width, bmp.Height, BitmapAlphaMode.Premultiplied);
        }
        finally { bmp.UnlockBits(data); }
    }
}
