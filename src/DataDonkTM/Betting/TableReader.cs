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
    /// <summary>The pot/call/hero text showed a "BB" unit, so the table is in big blinds even if the site isn't set that way.</summary>
    public bool ShowsBigBlinds { get; set; }

    /// <summary>When the site shows amounts in big blinds, one big blind is simply 1 on screen.</summary>
    public BetContext ToContext(SiteProfile site) =>
        new(site.AmountsInBigBlinds || ShowsBigBlinds ? 1 : BigBlind ?? 0, Pot ?? 0, ToCall, HeroCommitted, site.PotIncludesStreetBets);

    public string Describe()
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Big blind: {(BigBlind?.ToString("0.##") ?? "NOT FOUND")}   ({BlindsSource})");
        sb.AppendLine($"Pot:       {(Pot?.ToString("0.##") ?? "NOT READ")}   OCR: \"{PotText}\"");
        sb.AppendLine($"To call:   {ToCall:0.##}   OCR: \"{CallText}\"");
        sb.AppendLine($"Hero bet:  {HeroCommitted:0.##}   OCR: \"{HeroText}\"");
        if (ShowsBigBlinds) sb.AppendLine("Amounts:   shown in big blinds (\"BB\" on the table)");
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

        // Amounts already in big blinds: the big blind isn't needed (bb buttons type the number as-is).
        if (site.AmountsInBigBlinds) r.BlindsSource = "not needed (site shows amounts in big blinds)";
        else
        {
            r.BigBlind = AmountParser.ParseBigBlind(title, site.BlindsRegex, site.DecimalSeparator);
            r.BlindsSource = r.BigBlind != null ? $"title \"{title}\"" : $"not in title \"{title}\"";
        }
        if (r.BigBlind == null && !site.AmountsInBigBlinds && site.BlindsRegion != null)
        {
            var text = await OcrRegion(client, site.BlindsRegion);
            r.BigBlind = AmountParser.ParseBigBlind(text, site.BlindsRegex, site.DecimalSeparator);
            r.BlindsSource += $"; blinds region OCR \"{text}\"";
        }

        r.Postflop = TableCapture.IsPostflop(client, site, out var diff);
        r.BoardDiff = diff;

        // No big blind found: the table may show amounts in big blinds ("Pot 12.5 BB"), which the
        // pot/call text below reveals. Then a 3bb button just types "3".
        if (r.BigBlind == null && !site.AmountsInBigBlinds) readPotInfo = true;
        if (!readPotInfo) return r;

        // The regions are read at the same time.
        var pot = OcrRegionInBackground(client, site.PotRegion);
        var call = OcrRegionInBackground(client, site.CallRegion);
        var hero = OcrRegionInBackground(client, site.HeroBetRegion);
        await Task.WhenAll(pot, call, hero);
        if (site.PotRegion != null)
        {
            r.PotText = pot.Result;
            r.Pot = AmountParser.ParseFirst(r.PotText, site.DecimalSeparator);
        }
        if (site.CallRegion != null)
        {
            r.CallText = call.Result;
            // "Check" or an empty/hidden button means nothing to call.
            r.ToCall = r.CallText.Contains("check", StringComparison.OrdinalIgnoreCase)
                ? 0 : AmountParser.ParseFirst(r.CallText, site.DecimalSeparator) ?? 0;
        }
        if (site.HeroBetRegion != null)
        {
            r.HeroText = hero.Result;
            r.HeroCommitted = AmountParser.ParseFirst(r.HeroText, site.DecimalSeparator) ?? 0;
        }
        r.ShowsBigBlinds = AmountParser.MentionsBigBlinds(r.PotText) || AmountParser.MentionsBigBlinds(r.CallText)
                           || AmountParser.MentionsBigBlinds(r.HeroText);
        return r;
    }

    /// <summary>
    /// Crops here (a bitmap must not be used by two threads at once), then reads the crop on another thread.
    /// "" if the region isn't set.
    /// </summary>
    private static Task<string> OcrRegionInBackground(Bitmap client, RelRect? rect)
    {
        if (rect == null) return Task.FromResult("");
        var crop = TableCapture.Crop(client, rect);
        return Task.Run(async () =>
        {
            using (crop) return await OcrService.ReadTextAsync(crop);
        });
    }

    private static async Task<string> OcrRegion(Bitmap client, RelRect rect)
    {
        using var crop = TableCapture.Crop(client, rect);
        return await OcrService.ReadTextAsync(crop);
    }
}
