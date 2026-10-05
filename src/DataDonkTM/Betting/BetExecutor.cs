using System.Collections.Concurrent;
using DataDonkTM.Core;
using DataDonkTM.Native;

namespace DataDonkTM.Betting;

/// <summary>What happened the last time a bet button was used on a table, kept for bug reports.</summary>
public sealed class BetRecord : IDisposable
{
    public DateTime Time { get; init; } = DateTime.Now;
    public string Button { get; init; } = "";
    public Bitmap? Picture { get; set; }
    /// <summary>The table just after typing, and what was read from the bet box then.</summary>
    public Bitmap? PictureAfter { get; set; }
    public string BetBoxTextAfter { get; set; } = "";
    public string Reading { get; set; } = "";
    public string Result { get; set; } = "";
    public void Dispose() { Picture?.Dispose(); PictureAfter?.Dispose(); }
}

public static class BetExecutor
{
    /// <summary>The last bet button use per table window.</summary>
    public static readonly ConcurrentDictionary<IntPtr, BetRecord> LastBet = new();

    /// <summary>Reads the table, computes the size for the button and types it into the bet box.</summary>
    public static async Task<(bool Ok, string Message)> ExecuteAsync(IntPtr hwnd, SiteProfile site, BetButton button, BetSettings settings)
    {
        var record = new BetRecord { Button = button.DisplayText };
        if (LastBet.TryGetValue(hwnd, out var old)) old.Dispose();
        LastBet[hwnd] = record;
        var (ok, message) = await RunAsync(hwnd, site, button, settings, record);
        record.Result = (ok ? "OK: " : "FAILED: ") + message;
        ActivityLog.Add($"Bet button {button.DisplayText} on \"{Win32.GetTitle(hwnd)}\": {record.Result}");
        return (ok, message);
    }

    private static async Task<(bool Ok, string Message)> RunAsync(IntPtr hwnd, SiteProfile site, BetButton button, BetSettings settings, BetRecord record)
    {
        var clock = System.Diagnostics.Stopwatch.StartNew();
        var timings = new List<string>();
        void Mark(string step) => timings.Add($"{step} {clock.ElapsedMilliseconds} ms");
        try
        {
            if (site.BetBoxPoint == null)
                return (false, $"'{site.Name}' is not calibrated: set the bet box position in Sites → Edit / Calibrate.");
            if (button.Unit == BetUnit.PotPercent && site.PotRegion == null)
                return (false, $"'{site.Name}' is not calibrated: set the pot region in Sites → Edit / Calibrate.");

            using var bmp = TableCapture.CaptureClient(hwnd, site.Capture);
            if (bmp == null) return (false, "Could not capture the table.");
            record.Picture = (Bitmap)bmp.Clone();
            Mark("captured");

            // While the table is read: look for the bet box (accessibility calls run off the UI thread: our own
            // overlays are in the table's tree, and answering them while the UI thread waits would deadlock), and
            // on a site known to need simulated typing, activate the table, click the box and select its contents.
            // None of that depends on the amount.
            var readTask = TableReader.ReadAsync(bmp, Win32.GetTitle(hwnd), site, button.Unit == BetUnit.PotPercent);
            var boxTask = Task.Run(() => FindAccessibleBetBox(hwnd, site));
            var textBox = InputSender.FindTextBox(hwnd, site);
            var prepared = site.TypingLearned && site.Typing is TypeMethod.VirtualKeys or TypeMethod.Unicode
                ? PrepareQuietlyAsync(hwnd, site) : Task.FromResult<string?>(null);

            var reading = await readTask;
            record.Reading = reading.Describe();
            Mark("read");
            var (box, boxNote) = await boxTask;
            record.Reading += boxNote;
            if (textBox != IntPtr.Zero) record.Reading += $"\nNative text box: 0x{textBox.ToInt64():X} ({Win32.GetClass(textBox)})";
            var prepNotes = await prepared;
            if (prepNotes != null) Mark("box ready");

            var amount = BetCalculator.RaiseTo(button, reading.ToContext(site), settings.Rounding);
            if (amount == null)
                return (false, button.Unit == BetUnit.BigBlinds
                    ? $"Couldn't find the big blind ({reading.BlindsSource}). If this site shows amounts in big blinds, " +
                      "turn on \"shows amounts in big blinds\" in the site's setup."
                    : $"Couldn't read the pot (OCR: \"{reading.PotText}\").");
            if (amount <= 0) return (false, "Computed amount is 0.");

            var text = BetCalculator.Format(amount.Value, site.DecimalSeparator);
            var wanted = AmountParser.ParseFirst(text, site.DecimalSeparator) ?? amount.Value;

            // Enter it, then check it really landed: some clients ignore some of the ways.
            string? problem = null;
            foreach (var method in MethodsToTry(site, box != null, textBox != IntPtr.Zero))
            {
                try
                {
                    if (method == TypeMethod.Accessibility)
                    {
                        await Task.Run(() => AccessibleTable.SetValue(box!, text));
                        record.Reading += $"\n\nEntering {text}: Accessibility: set the bet box value";
                    }
                    // The box was made ready while reading: just type, unless the player switched windows meanwhile.
                    else if (prepNotes != null && method == site.Typing && Win32.GetForegroundWindow() == hwnd)
                        record.Reading += $"\n\nEntering {text}: {method} (box made ready while reading: {prepNotes}); " +
                                          await InputSender.TypeAsync(site, text, method);
                    else record.Reading += $"\n\nEntering {text}: " + await InputSender.EnterAmountAsync(hwnd, site, text, method);
                    prepNotes = null;
                    Mark($"entered ({method})");
                    await Task.Delay(150);
                    problem = await CheckBetBoxAsync(hwnd, site, record, box, wanted);
                    Mark("checked");
                }
                catch (Exception ex) { problem = ex.Message; prepNotes = null; }
                record.Reading += $"\nBet box after: \"{record.BetBoxTextAfter}\"{(problem == null ? " - OK" : " - " + problem)}";
                if (problem != null) continue;
                if (method != site.Typing || !site.TypingLearned)
                {
                    ActivityLog.Add(site.TypingLearned
                        ? $"Entering bets with {site.Typing} stopped working on {site.Name}; {method} works, using that from now on"
                        : $"Entering bets on {site.Name} works with {method}, using that from now on");
                    site.Typing = method;
                    site.TypingLearned = true;
                    ConfigStore.Save();
                }
                break;
            }
            if (problem != null)
                return (false, $"Couldn't enter {text}: the bet box {problem}.");

            return (true, $"{button.DisplayText} → {text}   (bb {reading.BigBlind:0.##}, pot {reading.Pot:0.##}, call {reading.ToCall:0.##}, in front {reading.HeroCommitted:0.##})");
        }
        catch (Exception ex)
        {
            record.Reading += "\n\nException:\n" + ex;
            return (false, ex.Message);
        }
        finally
        {
            record.Reading += "\n\nTimings (since the click): " + string.Join(", ", timings);
        }
    }

    /// <summary>Makes the bet box ready for typing; null (with the reason in the activity log) if that failed.</summary>
    private static async Task<string?> PrepareQuietlyAsync(IntPtr hwnd, SiteProfile site)
    {
        try { return await InputSender.PrepareBoxAsync(hwnd, site); }
        catch (Exception ex)
        {
            ActivityLog.Add($"Getting the bet box ready on {site.Name} failed: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Preferred order for finding what works on a site: first the ways that need no simulated mouse or keyboard
    /// (accessibility, a native text box, then messages posted to the window, as SwC needs), then simulated typing.
    /// </summary>
    private static readonly TypeMethod[] Preference =
        { TypeMethod.Accessibility, TypeMethod.TextBox, TypeMethod.WindowMessages, TypeMethod.VirtualKeys, TypeMethod.Unicode };

    /// <summary>
    /// The method already known to work on the site goes first, alone unless it fails; until one is known,
    /// or when it stops working, the others are tried in order of preference.
    /// </summary>
    internal static List<TypeMethod> MethodsToTry(SiteProfile site, bool boxExposed, bool textBoxFound)
    {
        var methods = new List<TypeMethod>();
        if (site.TypingLearned) methods.Add(site.Typing);
        methods.AddRange(Preference);
        return methods.Distinct()
            .Where(m => (m != TypeMethod.Accessibility || boxExposed) && (m != TypeMethod.TextBox || textBoxFound))
            .ToList();
    }

    private static (System.Windows.Automation.AutomationElement? Box, string Note) FindAccessibleBetBox(IntPtr hwnd, SiteProfile site)
    {
        try
        {
            var box = AccessibleTable.FindBetBox(hwnd, site);
            return (box, box == null ? "\n\nAccessibility: no bet box exposed" : $"\n\nAccessibility: bet box found, value \"{AccessibleTable.GetValue(box)}\"");
        }
        catch (Exception ex)
        {
            return (null, "\n\nAccessibility: " + ex.Message);
        }
    }

    /// <summary>
    /// Checks the bet box shows the wanted amount after entering it. Returns null if it does, else what's wrong.
    /// Uses the real values when the client exposes them (accessibility), else reads the picture.
    /// </summary>
    private static async Task<string?> CheckBetBoxAsync(IntPtr hwnd, SiteProfile site, BetRecord record,
        System.Windows.Automation.AutomationElement? box, double wanted)
    {
        bool Same(double? v) => v is double x && Math.Abs(x - wanted) < 0.005;

        using var after = TableCapture.CaptureClient(hwnd, site.Capture);
        if (after != null)
        {
            record.PictureAfter?.Dispose();
            record.PictureAfter = (Bitmap)after.Clone();
        }

        if (box != null)
        {
            record.BetBoxTextAfter = await Task.Run(() => AccessibleTable.GetValue(box)) ?? "";
            if (!Same(AmountParser.ParseFirst(record.BetBoxTextAfter, site.DecimalSeparator)))
                return $"shows \"{record.BetBoxTextAfter}\"";
            // The box can show the amount without the client taking it: the Bet/Raise button tells.
            var action = await Task.Run(() => AccessibleTable.BetActionText(hwnd));
            record.BetBoxTextAfter += action == null ? "" : $"; button \"{action}\"";
            var onButton = AmountParser.ParseFirst(action, site.DecimalSeparator);
            return onButton == null || Same(onButton) ? null : $"shows the amount, but the button still says \"{action}\"";
        }

        if (after == null || site.BetBoxPoint is not { } p) return null;
        // About the size of a bet box, centred on the marked point.
        var area = new RelRect { X = Math.Max(0, p.X - 0.06), Y = Math.Max(0, p.Y - 0.025), W = 0.12, H = 0.05 };
        using var crop = TableCapture.Crop(after, area);
        record.BetBoxTextAfter = await OcrService.ReadTextAsync(crop);
        var shown = AmountParser.ParseFirst(record.BetBoxTextAfter, site.DecimalSeparator);
        if (shown != null) return Same(shown) ? null : $"shows \"{record.BetBoxTextAfter}\"";

        // Unreadable (e.g. highlighted): if the box looks exactly as before, nothing was entered,
        // unless it already showed this amount.
        if (record.Picture == null || record.Picture.Size != after.Size) return null;
        using var before = TableCapture.Crop(record.Picture, area);
        if (TableCapture.SignatureDiff(TableCapture.Signature(before), TableCapture.Signature(crop)) >= 1) return null;
        return Same(AmountParser.ParseFirst(await OcrService.ReadTextAsync(before), site.DecimalSeparator)) ? null : "didn't change";
    }
}
