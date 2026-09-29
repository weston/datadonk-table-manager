using DataDonkTM.Betting;
using DataDonkTM.Core;
using Xunit;

namespace DataDonkTM.Tests;

public class AmountParserTests
{
    [Theory]
    [InlineData("Pot: $12.50", ".", 12.5)]
    [InlineData("Total pot : 1,234.56", ".", 1234.56)]
    [InlineData("Pot 1.234,56 €", ",", 1234.56)]
    [InlineData("Call 2,50", ".", 2.5)]            // OCR read ',' instead of '.'
    [InlineData("Call 2 50", ".", 2)]              // stray space is not a thousands group
    [InlineData("Pot 1 234", ".", 1234)]           // space thousands group
    [InlineData("Raise to 12.5K", ".", 12500)]
    [InlineData("Pot: 1O5", ".", 105)]             // O read instead of 0
    [InlineData("Call 1,500", ".", 1500)]
    public void ParseFirst(string text, string sep, double expected) =>
        Assert.Equal(expected, AmountParser.ParseFirst(text, sep)!.Value, 6);

    [Theory]
    [InlineData("Check")]
    [InlineData("")]
    [InlineData(null)]
    public void ParseFirst_NoNumber(string? text) => Assert.Null(AmountParser.ParseFirst(text));

    [Theory]
    [InlineData("Halley - $0.01/$0.02 USD - No Limit Hold'em - Logged In as hero", 0.02)]
    [InlineData("NLH 0.05/0.10 - Table 12", 0.10)]
    [InlineData("Tournament 123 Table 4 - Blinds 100/200 Ante 25", 200)]
    [InlineData("MTT - 1K/2K", 2000)]
    [InlineData("€0,25/€0,50 NL", 0.5)]
    public void BigBlindFromTitle(string title, double expected)
    {
        var sep = title.Contains("0,5") ? "," : ".";
        Assert.Equal(expected, AmountParser.ParseBigBlind(title, SiteProfile.DefaultBlindsRegex, sep)!.Value, 6);
    }

    [Fact]
    public void BigBlind_NotFound() => Assert.Null(AmountParser.ParseBigBlind("PokerStars Lobby", SiteProfile.DefaultBlindsRegex));
}
