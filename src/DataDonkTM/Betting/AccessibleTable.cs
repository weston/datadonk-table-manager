using System.Windows.Automation;
using DataDonkTM.Core;
using DataDonkTM.Native;

namespace DataDonkTM.Betting;

/// <summary>
/// Reads and sets a table's bet box through Windows accessibility (UI Automation), for clients that
/// expose their controls (e.g. SwC Poker, built with Qt). This is what screen readers use; it needs no
/// mouse, keyboard or focus, and reads the real values instead of a picture.
/// </summary>
public static class AccessibleTable
{
    /// <summary>The editable text box at (or nearest to, within 80 px) the marked bet box point.</summary>
    public static AutomationElement? FindBetBox(IntPtr hwnd, SiteProfile site)
    {
        if (site.BetBoxPoint == null) return null;
        var client = Win32.GetClientBounds(hwnd);
        var p = site.BetBoxPoint.ToPixels(client.Size);
        p.Offset(client.Location);

        var root = AutomationElement.FromHandle(hwnd);
        var edits = root.FindAll(TreeScope.Descendants,
            new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Edit));
        AutomationElement? best = null;
        double bestDist = 80;
        foreach (AutomationElement e in edits)
        {
            if (!e.TryGetCurrentPattern(ValuePattern.Pattern, out var vp) || ((ValuePattern)vp).Current.IsReadOnly) continue;
            var r = e.Current.BoundingRectangle;
            if (r.IsEmpty) continue;
            double dx = Math.Max(0, Math.Max(r.Left - p.X, p.X - r.Right));
            double dy = Math.Max(0, Math.Max(r.Top - p.Y, p.Y - r.Bottom));
            double d = Math.Sqrt(dx * dx + dy * dy); // 0 when the point is inside the box
            if (d <= bestDist) { bestDist = d; best = e; }
        }
        return best;
    }

    public static string? GetValue(AutomationElement box) =>
        box.TryGetCurrentPattern(ValuePattern.Pattern, out var vp) ? ((ValuePattern)vp).Current.Value : null;

    public static void SetValue(AutomationElement box, string text) =>
        ((ValuePattern)box.GetCurrentPattern(ValuePattern.Pattern)).SetValue(text);

    /// <summary>The text of the Bet / Raise button (e.g. "Raise 7.96 BB"), if the client exposes it.</summary>
    public static string? BetActionText(IntPtr hwnd)
    {
        var root = AutomationElement.FromHandle(hwnd);
        var buttons = root.FindAll(TreeScope.Descendants,
            new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button));
        foreach (AutomationElement b in buttons)
        {
            var name = (b.Current.Name ?? "").Replace('\n', ' ').Trim();
            if (name.StartsWith("Raise", StringComparison.OrdinalIgnoreCase) || name.StartsWith("Bet", StringComparison.OrdinalIgnoreCase))
                return name;
        }
        return null;
    }
}
