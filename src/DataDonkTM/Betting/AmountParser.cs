using System.Globalization;
using System.Text.RegularExpressions;

namespace DataDonkTM.Betting;

/// <summary>Turns OCR / title text such as "Pot: $1,234.50" or "Call 2,5K" into numbers.</summary>
public static class AmountParser
{
    // A number possibly with grouping, decimals and a K/M suffix. Grouping spaces are only allowed
    // when followed by exactly three digits ("1 234"), so "Call 2 50" does not become 250.
    private static readonly Regex NumberRx = new(
        @"(?<num>\d+(?:(?:[.,']|\s(?=\d{3}(?!\d)))\d+)*)\s?(?<suf>[kKmM](?![a-zA-Z]))?",
        RegexOptions.Compiled);

    public static double? ParseFirst(string? text, string decimalSeparator = ".") =>
        ParseAll(text, decimalSeparator).Cast<double?>().FirstOrDefault();

    public static IEnumerable<double> ParseAll(string? text, string decimalSeparator = ".")
    {
        if (string.IsNullOrWhiteSpace(text)) yield break;
        text = FixOcrDigits(text);
        foreach (Match m in NumberRx.Matches(text))
        {
            var v = ParseNumber(m.Groups["num"].Value, decimalSeparator);
            if (v == null) continue;
            var suf = m.Groups["suf"].Value.ToUpperInvariant();
            yield return suf switch { "K" => v.Value * 1_000, "M" => v.Value * 1_000_000, _ => v.Value };
        }
    }

    /// <summary>Parses one number token using the site's decimal separator.</summary>
    public static double? ParseNumber(string token, string decimalSeparator = ".")
    {
        token = token.Replace(" ", "").Replace("'", "");
        char dec = string.IsNullOrEmpty(decimalSeparator) ? '.' : decimalSeparator[0];
        char group = dec == '.' ? ',' : '.';

        // OCR often confuses '.' and ','. If only the "group" char appears, and it is followed by
        // exactly 1-2 digits at the end, it is almost certainly a decimal point ("2,50" → 2.50).
        if (!token.Contains(dec) && Regex.IsMatch(token, $@"^\d+{Regex.Escape(group.ToString())}\d{{1,2}}$"))
            token = token.Replace(group, dec);

        token = token.Replace(group.ToString(), "");
        if (dec != '.') token = token.Replace(dec, '.');
        // If several decimal points remain, treat all but the last as grouping.
        int last = token.LastIndexOf('.');
        if (last >= 0) token = token[..last].Replace(".", "") + token[last..];
        return double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : null;
    }

    /// <summary>Fix common OCR letter/digit mix-ups, only when surrounded by digits.</summary>
    private static string FixOcrDigits(string s)
    {
        s = Regex.Replace(s, @"(?<=\d)[Oo](?=[\d.,]|$)|(?<=[\d.,])[Oo](?=\d)", "0");
        s = Regex.Replace(s, @"(?<=\d)[lI|](?=\d)", "1");
        return s;
    }

    /// <summary>Extracts the big blind using a regex with a named group "bb".</summary>
    public static double? ParseBigBlind(string? text, string regex, string decimalSeparator = ".")
    {
        if (string.IsNullOrWhiteSpace(text) || string.IsNullOrWhiteSpace(regex)) return null;
        try
        {
            var m = Regex.Match(text, regex, RegexOptions.IgnoreCase);
            if (!m.Success || !m.Groups["bb"].Success) return null;
            var bb = ParseNumber(m.Groups["bb"].Value.TrimEnd('.', ','), decimalSeparator);
            // Handle "1K/2K" style tournament blinds.
            int end = m.Groups["bb"].Index + m.Groups["bb"].Length;
            if (bb != null && end < text.Length && char.ToUpperInvariant(text[end]) == 'K') bb *= 1000;
            return bb > 0 ? bb : null;
        }
        catch (ArgumentException) { return null; } // invalid user regex
    }
}
