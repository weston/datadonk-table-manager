using System.Drawing;
using DataDonkTM.Core;
using Xunit;

namespace DataDonkTM.Tests;

public class SlotLogicTests
{
    private static List<Slot> Slots(params SlotType[] types) =>
        types.Select((t, i) => new Slot { Type = t, X = i * 100, Y = 0, Width = 100, Height = 80 }).ToList();

    [Fact]
    public void NewTable_TakesFirstFreePlayingSlot()
    {
        var slots = Slots(SlotType.Observing, SlotType.Playing, SlotType.Playing);
        Assert.Equal(2, SlotLogic.PickSlotForNewTable(slots, new HashSet<int> { 1 }, preferPlaying: true));
    }

    [Fact]
    public void NewTable_FallsBackToObservingWhenPlayingFull()
    {
        var slots = Slots(SlotType.Playing, SlotType.Observing);
        Assert.Equal(1, SlotLogic.PickSlotForNewTable(slots, new HashSet<int> { 0 }, preferPlaying: true));
    }

    [Fact]
    public void NewTable_PreferObserving()
    {
        var slots = Slots(SlotType.Playing, SlotType.Observing);
        Assert.Equal(1, SlotLogic.PickSlotForNewTable(slots, new HashSet<int>(), preferPlaying: false));
    }

    [Fact]
    public void NewTable_NoFreeSlot() =>
        Assert.Null(SlotLogic.PickSlotForNewTable(Slots(SlotType.Playing), new HashSet<int> { 0 }, true));

    [Fact]
    public void SlotAtPoint_PicksContainingSlot()
    {
        var slots = Slots(SlotType.Playing, SlotType.Playing);
        Assert.Equal(1, SlotLogic.SlotAtPoint(slots, new Point(150, 40)));
        Assert.Null(SlotLogic.SlotAtPoint(slots, new Point(150, 400)));
    }

    [Fact]
    public void SlotAtPoint_OverlapPrefersNearestCentre()
    {
        var slots = new List<Slot>
        {
            new() { X = 0, Y = 0, Width = 200, Height = 200 },
            new() { X = 100, Y = 100, Width = 200, Height = 200 },
        };
        Assert.Equal(1, SlotLogic.SlotAtPoint(slots, new Point(180, 180)));
        Assert.Equal(0, SlotLogic.SlotAtPoint(slots, new Point(110, 110)));
    }

    [Fact]
    public void Grid_FillsAreaKeepingRatio()
    {
        var grid = SlotLogic.Grid(new Rectangle(0, 0, 1920, 1040), 2, 2, 0, 1.4, SlotType.Playing);
        Assert.Equal(4, grid.Count);
        Assert.All(grid, s => Assert.Equal(1.4, (double)s.Width / s.Height, 1));
        Assert.All(grid, s => Assert.True(s.X >= 0 && s.Y >= 0 && s.X + s.Width <= 1920 && s.Y + s.Height <= 1040));
        // Row-major order: slot 2 is directly below slot 0.
        Assert.Equal(grid[0].X, grid[2].X);
        Assert.True(grid[2].Y > grid[0].Y);
    }

    [Fact]
    public void Matches_RequiresAtLeastOneRule()
    {
        var empty = new SiteProfile { MinWidth = 0, MinHeight = 0 };
        Assert.False(TableManager.Matches(empty, "x.exe", "cls", "title", new Rectangle(0, 0, 800, 600), false));
    }

    [Fact]
    public void Matches_ProcessClassTitle()
    {
        var s = new SiteProfile { ProcessName = "PokerStars", ClassRegex = "^PokerStarsTableFrameClass$", TitleExcludeRegex = "Lobby" };
        var r = new Rectangle(0, 0, 800, 600);
        Assert.True(TableManager.Matches(s, "PokerStars.exe", "PokerStarsTableFrameClass", "Halley - $0.01/$0.02", r, false));
        Assert.False(TableManager.Matches(s, "PokerStars.exe", "#32770", "Halley", r, false));
        Assert.False(TableManager.Matches(s, "PokerStars.exe", "PokerStarsTableFrameClass", "PokerStars Lobby", r, false));
        Assert.False(TableManager.Matches(s, "other.exe", "PokerStarsTableFrameClass", "Halley", r, false));
        Assert.False(TableManager.Matches(s, "PokerStars.exe", "PokerStarsTableFrameClass", "Halley", new Rectangle(0, 0, 100, 100), false));
    }
}
