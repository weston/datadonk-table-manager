using DataDonkTM.Core;
using DataDonkTM.Native;

namespace DataDonkTM.Betting;

public static class BetExecutor
{
    /// <summary>Reads the table, computes the size for the button and types it into the bet box.</summary>
    public static async Task<(bool Ok, string Message)> ExecuteAsync(IntPtr hwnd, SiteProfile site, BetButton button, BetSettings settings)
    {
        try
        {
            if (site.BetBoxPoint == null)
                return (false, $"'{site.Name}' is not calibrated: set the bet box position in Sites → Edit / Calibrate.");
            if (button.Unit == BetUnit.PotPercent && site.PotRegion == null)
                return (false, $"'{site.Name}' is not calibrated: set the pot region in Sites → Edit / Calibrate.");

            using var bmp = TableCapture.CaptureClient(hwnd, site.Capture);
            if (bmp == null) return (false, "Could not capture the table.");
            var reading = await TableReader.ReadAsync(bmp, Win32.GetTitle(hwnd), site, button.Unit == BetUnit.PotPercent);

            var amount = BetCalculator.RaiseTo(button, reading.ToContext(site), settings.Rounding);
            if (amount == null)
                return (false, button.Unit == BetUnit.BigBlinds
                    ? $"Couldn't find the big blind ({reading.BlindsSource})."
                    : $"Couldn't read the pot (OCR: \"{reading.PotText}\").");
            if (amount <= 0) return (false, "Computed amount is 0.");

            var text = BetCalculator.Format(amount.Value, site.DecimalSeparator);
            await InputSender.EnterAmountAsync(hwnd, site, text);

            return (true, $"{button.DisplayText} → {text}   (bb {reading.BigBlind:0.##}, pot {reading.Pot:0.##}, call {reading.ToCall:0.##}, in front {reading.HeroCommitted:0.##})");
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }
}
