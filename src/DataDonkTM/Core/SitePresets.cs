namespace DataDonkTM.Core;

/// <summary>
/// Starting points for common sites. Only the detection rules are pre-filled; the screen regions
/// (pot, call button, bet box...) must be calibrated in Sites → Edit / Calibrate, because they
/// depend on the table theme and client version.
///
/// Process/class names are best guesses and are UNVERIFIED unless noted. Presets that could match
/// the wrong window start disabled - use "Fill rules from selected window" in the site editor to confirm them.
/// </summary>
public static class SitePresets
{
    private const string Unverified = "Detection rules are unverified guesses. Open a table, then in Edit / calibrate select it and use 'Fill rules from selected window' to confirm process/class/title, and calibrate the regions.";

    public static IEnumerable<SiteProfile> All()
    {
        yield return new SiteProfile
        {
            Name = "PokerStars",
            ProcessName = "PokerStars.exe",
            ClassRegex = "^PokerStarsTableFrameClass$",
            TitleRegex = "",
            Notes = "Table windows use the PokerStarsTableFrameClass window class. Calibrate the regions before using bet buttons.",
        };
        yield return Guess("GGPoker", "GGnet.exe");
        yield return Guess("888poker", "poker.exe");
        yield return Guess("partypoker", "PartyGaming.exe");
        yield return Guess("Winamax", "Winamax Poker.exe");
        yield return Guess("ACR / WPN", "AmericasCardroom.exe");
        yield return Guess("iPoker network", "");
        yield return Guess("CoinPoker", "CoinPoker.exe");
    }

    private static SiteProfile Guess(string name, string process) => new()
    {
        Name = name,
        ProcessName = process,
        // Most clients put the stakes in table titles; this keeps lobbies from being tiled.
        TitleRegex = @"\d\s*/\s*\D{0,3}\d",
        Enabled = false,
        Notes = Unverified,
    };
}
