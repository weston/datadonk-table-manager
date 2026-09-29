using System.Drawing;
using System.Drawing.Text;
using DataDonkTM.Betting;
using Xunit;

namespace DataDonkTM.Tests;

/// <summary>Runs the real Windows OCR engine on rendered text (needs an OCR-capable language pack, present on standard installs).</summary>
public class OcrTests
{
    private static Bitmap Render(string text, Color fg, Color bg, float px)
    {
        var bmp = new Bitmap(10 + (int)(text.Length * px * 0.62), (int)(px * 1.6));
        using var g = Graphics.FromImage(bmp);
        g.Clear(bg);
        g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
        using var font = new Font("Segoe UI", px, FontStyle.Bold, GraphicsUnit.Pixel);
        using var brush = new SolidBrush(fg);
        g.DrawString(text, font, brush, 4, 2);
        return bmp;
    }

    [Theory]
    [InlineData("Pot: 12.50", 12.5)]
    [InlineData("Call 1,250", 1250)]
    [InlineData("Total pot: $3.75", 3.75)]
    public async Task ReadsSmallLightTextOnDarkBackground(string text, double expected)
    {
        using var bmp = Render(text, Color.WhiteSmoke, Color.FromArgb(20, 60, 40), 13);
        var read = await OcrService.ReadTextAsync(bmp);
        Assert.Equal(expected, AmountParser.ParseFirst(read)!.Value, 6);
    }

    [Fact]
    public async Task ReadsDarkTextOnLightButton()
    {
        using var bmp = Render("Call 0.40", Color.Black, Color.FromArgb(230, 200, 60), 14);
        var read = await OcrService.ReadTextAsync(bmp);
        Assert.Equal(0.4, AmountParser.ParseFirst(read)!.Value, 6);
    }
}
