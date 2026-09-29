namespace DataDonkTM.Core;

/// <summary>Pure slot-selection rules (no Win32), so they can be unit tested.</summary>
public static class SlotLogic
{
    /// <summary>First free slot, taking the preferred slot type first and then the other type.</summary>
    public static int? PickSlotForNewTable(IReadOnlyList<Slot> slots, IReadOnlySet<int> occupied, bool preferPlaying)
    {
        var first = preferPlaying ? SlotType.Playing : SlotType.Observing;
        foreach (var type in new[] { first, first == SlotType.Playing ? SlotType.Observing : SlotType.Playing })
            for (int i = 0; i < slots.Count; i++)
                if (slots[i].Type == type && !occupied.Contains(i))
                    return i;
        return null;
    }

    /// <summary>Slot containing the point. If slots overlap, the one whose centre is closest wins.</summary>
    public static int? SlotAtPoint(IReadOnlyList<Slot> slots, Point p)
    {
        int? best = null;
        double bestDist = double.MaxValue;
        for (int i = 0; i < slots.Count; i++)
        {
            var b = slots[i].Bounds;
            if (!b.Contains(p)) continue;
            double dx = b.X + b.Width / 2.0 - p.X, dy = b.Y + b.Height / 2.0 - p.Y;
            double d = dx * dx + dy * dy;
            if (d < bestDist) { bestDist = d; best = i; }
        }
        return best;
    }

    /// <summary>Evenly spaced grid of slots inside an area, optionally keeping a width/height ratio.</summary>
    public static List<Slot> Grid(Rectangle area, int rows, int cols, int gap, double? aspectRatio, SlotType type)
    {
        var result = new List<Slot>();
        if (rows < 1 || cols < 1) return result;
        int cellW = (area.Width - gap * (cols - 1)) / cols;
        int cellH = (area.Height - gap * (rows - 1)) / rows;
        int w = cellW, h = cellH;
        if (aspectRatio is double ar && ar > 0)
        {
            if (cellW / ar <= cellH) h = (int)Math.Round(cellW / ar);
            else w = (int)Math.Round(cellH * ar);
        }
        // Centre the whole block inside the area.
        int totalW = w * cols + gap * (cols - 1), totalH = h * rows + gap * (rows - 1);
        int x0 = area.X + (area.Width - totalW) / 2, y0 = area.Y + (area.Height - totalH) / 2;
        for (int r = 0; r < rows; r++)
            for (int c = 0; c < cols; c++)
                result.Add(new Slot { Type = type, X = x0 + c * (w + gap), Y = y0 + r * (h + gap), Width = w, Height = h });
        return result;
    }
}
