using System.Text.Json.Serialization;

namespace DataDonkTM.Core;

public enum SlotType { Playing, Observing }

/// <summary>A tiling location, in physical screen pixels (same coordinates as GetWindowRect).</summary>
public sealed class Slot
{
    public SlotType Type { get; set; } = SlotType.Playing;
    public int X { get; set; }
    public int Y { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }

    [JsonIgnore]
    public Rectangle Bounds
    {
        get => new(X, Y, Width, Height);
        set { X = value.X; Y = value.Y; Width = value.Width; Height = value.Height; }
    }

    public Slot Clone() => (Slot)MemberwiseClone();
}

public sealed class TilingProfile
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "New profile";
    /// <summary>Order matters: new tables fill slots in this order.</summary>
    public List<Slot> Slots { get; set; } = new();
    public bool AutoPlaceNewTables { get; set; } = true;
    public bool SnapOnDrag { get; set; } = true;
    /// <summary>New tables go to playing slots first (true) or observing slots first (false).</summary>
    public bool NewTablesPreferPlaying { get; set; } = true;

    public TilingProfile Clone()
    {
        var p = (TilingProfile)MemberwiseClone();
        p.Slots = Slots.Select(s => s.Clone()).ToList();
        return p;
    }
}

/// <summary>A rectangle as fractions (0..1) of a table's client area, so it survives table resizing.</summary>
public sealed class RelRect
{
    public double X { get; set; }
    public double Y { get; set; }
    public double W { get; set; }
    public double H { get; set; }

    public Rectangle ToPixels(Size client) => new(
        (int)Math.Round(X * client.Width), (int)Math.Round(Y * client.Height),
        Math.Max(1, (int)Math.Round(W * client.Width)), Math.Max(1, (int)Math.Round(H * client.Height)));

    public static RelRect FromPixels(Rectangle r, Size client) => new()
    {
        X = (double)r.X / client.Width, Y = (double)r.Y / client.Height,
        W = (double)r.Width / client.Width, H = (double)r.Height / client.Height,
    };
}

/// <summary>A point as fractions (0..1) of a table's client area.</summary>
public sealed class RelPoint
{
    public double X { get; set; }
    public double Y { get; set; }

    public RelPoint() { }
    public RelPoint(double x, double y) { X = x; Y = y; }

    public Point ToPixels(Size client) =>
        new((int)Math.Round(X * client.Width), (int)Math.Round(Y * client.Height));
}

public enum SelectAllMethod { CtrlA, DoubleClick, TripleClick, Backspaces }
public enum TypeMethod { VirtualKeys, Unicode }
public enum CaptureMethod { PrintWindow, Screen }

/// <summary>Everything needed to recognise and read one poker site's tables.</summary>
public sealed class SiteProfile
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "New site";
    public bool Enabled { get; set; } = true;
    public string Notes { get; set; } = "";

    // --- Detection: a top-level window is a table if ALL non-empty rules match. ---
    /// <summary>Executable file name, e.g. "PokerStars.exe". Case-insensitive; ".exe" optional.</summary>
    public string ProcessName { get; set; } = "";
    public string ClassRegex { get; set; } = "";
    public string TitleRegex { get; set; } = "";
    public string TitleExcludeRegex { get; set; } = "";
    public int MinWidth { get; set; } = 300;
    public int MinHeight { get; set; } = 200;

    // --- Reading the table ---
    /// <summary>Applied to the window title (then to the OCR'd blinds region). Needs a named group "bb".</summary>
    public string BlindsRegex { get; set; } = DefaultBlindsRegex;
    public const string DefaultBlindsRegex = @"(?<sb>\d[\d.,]*[kK]?)\s*/\s*[^\d\s]{0,3}\s*(?<bb>\d[\d.,]*)";
    public string DecimalSeparator { get; set; } = ".";
    /// <summary>True if the pot number shown on the table already includes bets made on the current street.</summary>
    public bool PotIncludesStreetBets { get; set; } = true;
    /// <summary>
    /// True if the client shows every amount in big blinds (e.g. "Pot 12.5 BB") and the bet box expects big blinds.
    /// Then 2.5bb is typed as "2.5", and pot % is computed from the big-blind numbers on screen.
    /// </summary>
    public bool AmountsInBigBlinds { get; set; }
    public CaptureMethod Capture { get; set; } = CaptureMethod.PrintWindow;

    public RelRect? PotRegion { get; set; }
    /// <summary>Region of the Call/Check button text, used to read the amount to call.</summary>
    public RelRect? CallRegion { get; set; }
    /// <summary>Region showing hero's chips already in front of them on this street (optional).</summary>
    public RelRect? HeroBetRegion { get; set; }
    /// <summary>Optional region showing blinds, if they are not in the window title.</summary>
    public RelRect? BlindsRegion { get; set; }
    /// <summary>Region of the first flop card; compared to an empty-board reference to detect preflop/postflop.</summary>
    public RelRect? BoardRegion { get; set; }
    public string? EmptyBoardSignature { get; set; }
    public double BoardDiffThreshold { get; set; } = 35;

    // --- Entering bets ---
    public RelPoint? BetBoxPoint { get; set; }
    public SelectAllMethod SelectAll { get; set; } = SelectAllMethod.CtrlA;
    public TypeMethod Typing { get; set; } = TypeMethod.VirtualKeys;
    public int InputDelayMs { get; set; } = 30;

    // --- Overlay placement (top-left corner, fraction of client area) ---
    public RelPoint RngPosition { get; set; } = new(0.02, 0.12);
    public RelPoint BetPanelPosition { get; set; } = new(0.55, 0.70);

    public SiteProfile Clone()
    {
        var json = System.Text.Json.JsonSerializer.Serialize(this, ConfigStore.JsonOptions);
        return System.Text.Json.JsonSerializer.Deserialize<SiteProfile>(json, ConfigStore.JsonOptions)!;
    }
}

public enum BetUnit { BigBlinds, PotPercent }

public sealed class BetButton
{
    public BetUnit Unit { get; set; } = BetUnit.PotPercent;
    public double Value { get; set; }

    public BetButton() { }
    public BetButton(BetUnit unit, double value) { Unit = unit; Value = value; }

    [JsonIgnore]
    public string DisplayText => Unit == BetUnit.BigBlinds ? $"{Value:0.##}bb" : $"{Value:0.#}%";
}

public enum RoundingMode { Auto, Cents, WholeChips, TenthBB, HalfBB, WholeBB }

public sealed class BetSettings
{
    public const int MaxButtons = 8;

    public bool Enabled { get; set; } = true;
    public List<BetButton> Preflop { get; set; } = DefaultPreflop();
    public List<BetButton> Postflop { get; set; } = DefaultPostflop();
    public int ButtonWidth { get; set; } = 44;
    public int ButtonHeight { get; set; } = 22;
    public RoundingMode Rounding { get; set; } = RoundingMode.Auto;

    public static List<BetButton> DefaultPreflop() =>
        new double[] { 3, 3.5, 7.5, 8, 10, 12, 22, 25 }.Select(v => new BetButton(BetUnit.BigBlinds, v)).ToList();

    public static List<BetButton> DefaultPostflop() =>
        new double[] { 10, 25, 40, 50, 76, 100, 150, 200 }.Select(v => new BetButton(BetUnit.PotPercent, v)).ToList();
}

public sealed class RngSettings
{
    public bool Enabled { get; set; } = true;
    /// <summary>Height of the box in pixels; width is 1.5x.</summary>
    public int Size { get; set; } = 30;
}

public sealed class AppConfig
{
    public int Version { get; set; } = 1;
    public bool TilingEnabled { get; set; } = true;
    /// <summary>Ask GitHub for a newer version at startup and twice a day (see UpdateChecker.cs).</summary>
    public bool CheckForUpdates { get; set; } = true;
    public List<TilingProfile> Profiles { get; set; } = new();
    public string? DefaultProfileId { get; set; }
    public string? ActiveProfileId { get; set; }
    public List<SiteProfile> Sites { get; set; } = new();
    public BetSettings Bet { get; set; } = new();
    public RngSettings Rng { get; set; } = new();

    [JsonIgnore]
    public TilingProfile? ActiveProfile => Profiles.FirstOrDefault(p => p.Id == ActiveProfileId);
}
