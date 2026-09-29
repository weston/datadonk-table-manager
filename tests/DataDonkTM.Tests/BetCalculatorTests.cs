using DataDonkTM.Betting;
using DataDonkTM.Core;
using Xunit;

namespace DataDonkTM.Tests;

public class BetCalculatorTests
{
    private static double? Calc(BetUnit unit, double value, double bb, double pot, double toCall, double hero, bool includes = true,
        RoundingMode rounding = RoundingMode.Cents) =>
        BetCalculator.RaiseTo(new BetButton(unit, value), new BetContext(bb, pot, toCall, hero, includes), rounding);

    [Fact]
    public void BigBlindButton_IsMultipleOfBigBlind() =>
        Assert.Equal(0.25, Calc(BetUnit.BigBlinds, 2.5, 0.10, 0, 0, 0));

    [Fact]
    public void BigBlindButton_WithoutBlinds_ReturnsNull() =>
        Assert.Null(Calc(BetUnit.BigBlinds, 3, 0, 1, 0, 0));

    [Fact]
    public void PotBet_Unopened_IsFractionOfPot() =>
        // Flop, pot 10, nobody bet: 50% pot = 5.
        Assert.Equal(5, Calc(BetUnit.PotPercent, 50, 1, 10, 0, 0));

    [Fact]
    public void PotRaise_FromButton_Unopened() =>
        // 0.5/1, button open, pot-size: call 1, pot becomes 2.5, raise 2.5 more → to 3.5.
        Assert.Equal(3.5, Calc(BetUnit.PotPercent, 100, 1, 1.5, 1, 0));

    [Fact]
    public void PotRaise_FromSmallBlind_UsesHeroCommitted() =>
        // SB has 0.5 in: call 0.5 more, pot 2, raise 2 more → to 3.
        Assert.Equal(3, Calc(BetUnit.PotPercent, 100, 1, 1.5, 0.5, 0.5));

    [Fact]
    public void PotRaise_FacingPostflopBet() =>
        // Pot 10 before villain bets 5 → displayed total 15. Pot raise: call 5 → 20, raise 20 → to 25.
        Assert.Equal(25, Calc(BetUnit.PotPercent, 100, 1, 15, 5, 0));

    [Fact]
    public void HalfPotRaise_FacingPostflopBet() =>
        // Same spot, 50%: to 5 + 0.5 × 20 = 15.
        Assert.Equal(15, Calc(BetUnit.PotPercent, 50, 1, 15, 5, 0));

    [Fact]
    public void PotExcludingStreetBets_AddsFacingBetAndHeroChips() =>
        // Site shows 10 (previous streets). Villain bet 5, hero has 0 in: same spot as above → 25.
        Assert.Equal(25, Calc(BetUnit.PotPercent, 100, 1, 10, 5, 0, includes: false));

    [Fact]
    public void PotExcludingStreetBets_ReRaise() =>
        // Prev-street pot 10. Hero bet 5, villain raised to 15 → toCall 10, hero in 5.
        // Total pot = 10 + 5 + 15 = 30; after call 40; pot re-raise to 15 + 40 = 55.
        Assert.Equal(55, Calc(BetUnit.PotPercent, 100, 1, 10, 10, 5, includes: false));

    [Fact]
    public void SiteShowingBigBlinds_TypesBigBlindNumbers()
    {
        // SwC-style: title says 25/50, but the table and bet box use big blinds.
        var site = new SiteProfile { AmountsInBigBlinds = true };
        var reading = new TableReading { BigBlind = 50, Pot = 1.5, ToCall = 1 };
        var ctx = reading.ToContext(site);
        Assert.Equal(2.5, BetCalculator.RaiseTo(new BetButton(BetUnit.BigBlinds, 2.5), ctx, RoundingMode.Auto));
        Assert.Equal(3.5, BetCalculator.RaiseTo(new BetButton(BetUnit.PotPercent, 100), ctx, RoundingMode.Auto));
        Assert.Equal("2.50", BetCalculator.Format(2.5, "."));
    }

    [Theory]
    [InlineData(RoundingMode.Cents, 0.10, 0.333, 0.33)]
    [InlineData(RoundingMode.WholeChips, 100, 333.4, 333)]
    [InlineData(RoundingMode.HalfBB, 100, 333.4, 350)]
    [InlineData(RoundingMode.WholeBB, 1, 3.4, 3)]
    [InlineData(RoundingMode.Auto, 0.10, 0.333, 0.33)]
    [InlineData(RoundingMode.Auto, 200, 555.5, 556)]
    public void Rounding(RoundingMode mode, double bb, double amount, double expected) =>
        Assert.Equal(expected, BetCalculator.Round(amount, mode, bb), 6);

    [Theory]
    [InlineData(3.5, ".", "3.50")]
    [InlineData(3.5, ",", "3,50")]
    [InlineData(1200, ".", "1200")]
    public void Format(double amount, string sep, string expected) =>
        Assert.Equal(expected, BetCalculator.Format(amount, sep));
}
