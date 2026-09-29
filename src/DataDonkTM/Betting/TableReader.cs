using System.Text;
using DataDonkTM.Core;

namespace DataDonkTM.Betting;

public sealed class TableReading
{
    public double? BigBlind { get; set; }
    public string BlindsSource { get; set; } = "";
    public double? Pot { get; set; }
    public string PotText { get; set; } = "";
    public double ToCall { get; set; }
    public string CallText { get; set; } = "";
    public double HeroCommitted { get; set; }
    public string HeroText { get; set; } = "";
    public bool? Postflop { get; set; }
    public double BoardDiff { get; set; }

    /// <summary>When the site shows amounts in big blinds, one big blind is simply 1 on screen.</summary>
    public BetContext ToContext(SiteProfile site) =>
        new(site.AmountsInBigBlinds ? 1 : BigBlind ?? 0, Pot ?? 0, ToCall, HeroCommitted, site.PotIncludesStreetBets);

    public string Describe()
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Big blind: {(BigBlind?.ToString("0.##") ?? "NOT FOUND")}   ({BlindsSource})");
        sb.AppendLine($"Pot:       {(Pot?.ToString("0.##") ?? "NOT READ")}   OCR: \"{PotText}\"");
        sb.AppendLine($"To call:   {ToCall:0.##}   OCR: \"{CallText}\"");
        sb.AppendLine($"Hero bet:  {HeroCommitted:0.##}   OCR: \"{HeroText}\"");
        sb.AppendLine($"Street:    {(Postflop == null ? "not calibrated" : Postflop.Value ? "POSTFLOP" : "PREFLOP")}   (board diff {BoardDiff:0.0})");
        return sb.ToString();
    }
}

/// <summary>Reads blinds, pot, amount to call and hero's bet from a captured table image.</summary>
public static class TableReader
{
    public static async Task<TableReading> ReadAsync(Bitmap client, string title, SiteProfile site, bool readPotInfo = true)
    {
        var r = new TableReading();

        r.BigBlind = AmountParser.ParseBigBlind(title, site.BlindsRegex, site.DecimalSeparator);
        r.BlindsSource = r.BigBlind != null ? $"title \"{title}\"" : $"not in title \"{title}\"";
        if (r.BigBlind == null && site.BlindsRegion != null)
        {
            var text = await OcrRegion(client, site.BlindsRegion);
            r.BigBlind = AmountParser.ParseBigBlind(text, site.BlindsRegex, site.DecimalSeparator);
            r.BlindsSource += $"; blinds region OCR \"{text}\"";
        }

        r.Postflop = TableCapture.IsPostflop(client, site, out var diff);
        r.BoardDiff = diff;

        if (!readPotInfo) return r;

        if (site.PotRegion != null)
        {
            r.PotText = await OcrRegion(client, site.PotRegion);
            r.Pot = AmountParser.ParseFirst(r.PotText, site.DecimalSeparator);
        }
        if (site.CallRegion != null)
        {
            r.CallText = await OcrRegion(client, site.CallRegion);
            // "Check" or an empty/hidden button means nothing to call.
            r.ToCall = r.CallText.Contains("check", StringComparison.OrdinalIgnoreCase)
                ? 0 : AmountParser.ParseFirst(r.CallText, site.DecimalSeparator) ?? 0;
        }
        if (site.HeroBetRegion != null)
        {
            r.HeroText = await OcrRegion(client, site.HeroBetRegion);
            r.HeroCommitted = AmountParser.ParseFirst(r.HeroText, site.DecimalSeparator) ?? 0;
        }
        return r;
    }

    private static async Task<string> OcrRegion(Bitmap client, RelRect rect)
    {
        using var crop = TableCapture.Crop(client, rect);
        return await OcrService.ReadTextAsync(crop);
    }
}
