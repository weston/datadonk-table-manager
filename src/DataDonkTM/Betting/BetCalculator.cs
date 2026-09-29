using DataDonkTM.Core;

namespace DataDonkTM.Betting;

/// <summary>What was read from the table at the moment a bet button is clicked.</summary>
/// <param name="BigBlind">Big blind in table currency/chips (0 if unknown).</param>
/// <param name="Pot">The pot number as displayed by the site.</param>
/// <param name="ToCall">Amount hero must add to call (0 if not facing a bet).</param>
/// <param name="HeroCommitted">Chips hero already has in front of them on this street (e.g. a posted blind).</param>
/// <param name="PotIncludesStreetBets">Whether the displayed pot already contains this street's bets.</param>
public readonly record struct BetContext(double BigBlind, double Pot, double ToCall, double HeroCommitted, bool PotIncludesStreetBets);

public static class BetCalculator
{
    /// <summary>
    /// Total pot including every chip committed so far on all streets. If the site's pot display
    /// excludes current-street bets, we add hero's chips plus the bet hero is facing. (With several
    /// callers in front of hero that under-counts, so prefer a site/setting that shows the total pot.)
    /// </summary>
    public static double TotalPot(in BetContext c) => c.PotIncludesStreetBets
        ? c.Pot
        : c.Pot + c.HeroCommitted + (c.ToCall > 0 ? c.HeroCommitted + c.ToCall : 0);

    /// <summary>
    /// The "raise to" / "bet" amount (a street total, which is what bet boxes expect).
    ///   BB:    value × big blind.
    ///   Pot %: hero first calls, then raises by pct × (pot after the call):
    ///          raiseTo = heroCommitted + toCall + pct × (totalPot + toCall).
    ///          e.g. 100% pot open from the button at 0.5/1: 0 + 1 + 1.0 × (1.5 + 1) = 3.5
    /// Returns null if the needed inputs are missing.
    /// </summary>
    public static double? RaiseTo(BetButton b, in BetContext c, RoundingMode rounding)
    {
        double amount;
        if (b.Unit == BetUnit.BigBlinds)
        {
            if (c.BigBlind <= 0) return null;
            amount = b.Value * c.BigBlind;
        }
        else
        {
            if (c.Pot <= 0) return null;
            double potAfterCall = TotalPot(c) + c.ToCall;
            amount = c.HeroCommitted + c.ToCall + b.Value / 100.0 * potAfterCall;
        }
        return Round(amount, rounding, c.BigBlind);
    }

    public static double Round(double amount, RoundingMode mode, double bigBlind)
    {
        if (mode == RoundingMode.Auto)
            // Tournament chips (and very high cash stakes) → whole numbers; otherwise cents.
            mode = bigBlind >= 10 ? RoundingMode.WholeChips : RoundingMode.Cents;

        double step = mode switch
        {
            RoundingMode.WholeChips => 1,
            RoundingMode.TenthBB when bigBlind > 0 => bigBlind * 0.1,
            RoundingMode.HalfBB when bigBlind > 0 => bigBlind * 0.5,
            RoundingMode.WholeBB when bigBlind > 0 => bigBlind,
            _ => 0.01,
        };
        double r = Math.Round(amount / step, MidpointRounding.AwayFromZero) * step;
        return Math.Round(r, 2, MidpointRounding.AwayFromZero);
    }

    /// <summary>Formats for typing into a bet box: no grouping, minimal decimals, site's separator.</summary>
    public static string Format(double amount, string decimalSeparator)
    {
        var s = amount.ToString(amount == Math.Floor(amount) ? "0" : "0.00", System.Globalization.CultureInfo.InvariantCulture);
        return decimalSeparator == "." ? s : s.Replace(".", decimalSeparator);
    }
}
