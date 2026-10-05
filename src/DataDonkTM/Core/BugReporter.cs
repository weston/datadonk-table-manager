using System.Drawing.Imaging;
using System.Text;
using System.Text.Json;
using DataDonkTM.Betting;
using DataDonkTM.Native;

namespace DataDonkTM.Core;

/// <summary>
/// Saves everything needed to look into a problem later into one folder:
/// pictures of the screen and table, what the app reads from the table, the last bet button use,
/// window/tile positions, settings and recent activity. Nothing is sent anywhere.
/// </summary>
public static class BugReporter
{
    public static string ReportsFolder =>
        Path.Combine(Path.GetDirectoryName(ConfigStore.ConfigPath)!, "bug-reports");

    /// <summary>Saves a report about one table (or, with no table, about everything) and returns its folder.</summary>
    public static async Task<string> SaveAsync(TableManager manager, TableInfo? table)
    {
        var name = $"{DateTime.Now:yyyy-MM-dd_HH-mm-ss}_{Safe(table?.Site.Name ?? "general")}";
        var dir = Path.Combine(ReportsFolder, name);
        Directory.CreateDirectory(dir);
        var errors = new StringBuilder();

        void Try(string what, Action action)
        {
            try { action(); }
            catch (Exception ex) { errors.AppendLine($"{what}: {ex}"); }
        }

        // The whole screen first, before anything changes.
        Try("screen.png", () =>
        {
            using var screen = TableCapture.CaptureScreen(SystemInformation.VirtualScreen);
            screen.Save(Path.Combine(dir, "screen.png"), ImageFormat.Png);
        });

        // One table: its files go in the report folder. General report: a sub-folder per open table.
        var tables = table != null ? new[] { (table, dir) }
            : manager.Tables.Select((t, i) => (t, Path.Combine(dir, $"table-{i + 1}_{Safe(t.Site.Name)}"))).ToArray();
        foreach (var (t, tableDir) in tables)
        {
            try
            {
                Directory.CreateDirectory(tableDir);
                await SaveTableAsync(t, tableDir, errors);
            }
            catch (Exception ex) { errors.AppendLine($"table {t}: {ex}"); }
        }

        Try("info.json", () => File.WriteAllText(Path.Combine(dir, "info.json"), JsonSerializer.Serialize(Info(manager, table), ConfigStore.JsonOptions)));
        Try("config.json", () => File.Copy(ConfigStore.ConfigPath, Path.Combine(dir, "config.json")));
        Try("activity.txt", () => File.WriteAllLines(Path.Combine(dir, "activity.txt"), ActivityLog.Snapshot()));
        if (errors.Length > 0) File.WriteAllText(Path.Combine(dir, "errors.txt"), errors.ToString());

        ActivityLog.Add($"Bug report saved: {dir}");
        return dir;
    }

    private static async Task SaveTableAsync(TableInfo t, string dir, StringBuilder errors)
    {
        var site = t.Site;
        using var bmp = TableCapture.CaptureClient(t.Hwnd, site.Capture);
        if (bmp == null) { errors.AppendLine("table: could not capture the table (minimised or closed?)"); return; }
        bmp.Save(Path.Combine(dir, "table.png"), ImageFormat.Png);

        using (var marked = DrawMarks(bmp, site)) marked.Save(Path.Combine(dir, "table-marked.png"), ImageFormat.Png);

        var regions = new (string Name, RelRect? Rect)[]
        {
            ("pot", site.PotRegion), ("call", site.CallRegion), ("your-bet", site.HeroBetRegion),
            ("blinds", site.BlindsRegion), ("flop-card", site.BoardRegion),
        };
        foreach (var (regionName, rect) in regions)
        {
            if (rect == null) continue;
            using var crop = TableCapture.Crop(bmp, rect);
            crop.Save(Path.Combine(dir, $"region-{regionName}.png"), ImageFormat.Png);
        }

        // What the app reads right now, and what each bet button would type.
        var sb = new StringBuilder();
        sb.AppendLine($"Read at {DateTime.Now:HH:mm:ss} from table.png");
        sb.AppendLine();
        var reading = await TableReader.ReadAsync(bmp, Win32.GetTitle(t.Hwnd), site, readPotInfo: true);
        sb.AppendLine(reading.Describe());
        var context = reading.ToContext(site);
        var bet = ConfigStore.Current.Bet;
        foreach (var (street, buttons) in new[] { ("Preflop", bet.Preflop), ("Postflop", bet.Postflop) })
        {
            sb.Append($"{street} buttons would type: ");
            sb.AppendLine(string.Join("   ", buttons.Select(b =>
                $"{b.DisplayText}={(BetCalculator.RaiseTo(b, context, bet.Rounding) is double v ? BetCalculator.Format(v, site.DecimalSeparator) : "?")}")));
        }
        File.WriteAllText(Path.Combine(dir, "reading-now.txt"), sb.ToString());

        // The last time a bet button was used on this table: what it saw and did.
        if (BetExecutor.LastBet.TryGetValue(t.Hwnd, out var last))
        {
            File.WriteAllText(Path.Combine(dir, "last-bet.txt"),
                $"Bet button {last.Button} at {last.Time:HH:mm:ss.fff} ({(DateTime.Now - last.Time).TotalSeconds:0} seconds before this report)\n" +
                $"{last.Result}\nBet box read after typing: \"{last.BetBoxTextAfter}\"\n\n{last.Reading}");
            if (last.Picture is { } pic)
            {
                pic.Save(Path.Combine(dir, "last-bet-table.png"), ImageFormat.Png);
                using var markedLast = DrawMarks(pic, site);
                markedLast.Save(Path.Combine(dir, "last-bet-table-marked.png"), ImageFormat.Png);
            }
            last.PictureAfter?.Save(Path.Combine(dir, "last-bet-after-typing.png"), ImageFormat.Png);
        }
    }

    /// <summary>The table picture with every marked box and the bet box point drawn on it.</summary>
    private static Bitmap DrawMarks(Bitmap src, SiteProfile site)
    {
        var bmp = new Bitmap(src);
        using var g = Graphics.FromImage(bmp);
        using var font = new Font("Segoe UI", 11, FontStyle.Bold, GraphicsUnit.Pixel);
        void Box(RelRect? r, Color c, string label)
        {
            if (r == null) return;
            var px = r.ToPixels(bmp.Size);
            using var pen = new Pen(c, 2);
            g.DrawRectangle(pen, px);
            using var brush = new SolidBrush(c);
            g.DrawString(label, font, brush, px.X, Math.Max(0, px.Y - 15));
        }
        Box(site.PotRegion, Color.Gold, "pot");
        Box(site.CallRegion, Color.OrangeRed, "call");
        Box(site.HeroBetRegion, Color.Violet, "your bet");
        Box(site.BlindsRegion, Color.LightSkyBlue, "blinds");
        Box(site.BoardRegion, Color.LimeGreen, "flop card");
        if (site.BetBoxPoint is { } p)
        {
            int x = (int)Math.Round(p.X * bmp.Width), y = (int)Math.Round(p.Y * bmp.Height);
            using var pen = new Pen(Color.Cyan, 2);
            g.DrawLine(pen, x - 8, y, x + 8, y);
            g.DrawLine(pen, x, y - 8, x, y + 8);
            g.DrawString("bet box", font, Brushes.Cyan, x + 6, y + 4);
        }
        return bmp;
    }

    private static object Info(TableManager manager, TableInfo? reported)
    {
        var cfg = ConfigStore.Current;
        var layout = cfg.ActiveProfile;
        return new
        {
            Time = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
            Version = UpdateChecker.CurrentVersion.ToString(),
            Commit = UpdateChecker.SourceCommit,
            Windows = Environment.OSVersion.VersionString,
            ReportedTable = reported == null ? null : Table(manager, reported),
            Screens = Screen.AllScreens.Select(s => new { s.DeviceName, s.Primary, Bounds = Rect(s.Bounds), WorkingArea = Rect(s.WorkingArea) }),
            VirtualScreen = Rect(SystemInformation.VirtualScreen),
            cfg.TilingEnabled,
            ActiveLayout = layout == null ? null : new
            {
                layout.Name, layout.AutoPlaceNewTables, layout.SnapOnDrag, layout.NewTablesPreferPlaying,
                Slots = layout.Slots.Select((s, i) => new { Index = i, Type = s.Type.ToString(), Bounds = Rect(s.Bounds) }),
            },
            AllTables = manager.Tables.Select(t => Table(manager, t)),
        };
    }

    private static object Table(TableManager manager, TableInfo t) => new
    {
        Hwnd = $"0x{t.Hwnd.ToInt64():X}",
        t.Title,
        Site = t.Site.Name,
        Process = manager.GetProcessName(t.Hwnd),
        Class = Win32.GetClass(t.Hwnd),
        Slot = t.SlotIndex,
        SlotType = manager.SlotTypeOf(t)?.ToString() ?? "not in a slot",
        Minimized = Win32.IsIconic(t.Hwnd),
        Window = Rect(Win32.GetWindowBounds(t.Hwnd)),
        VisibleWindow = Rect(Win32.GetVisibleBounds(t.Hwnd)),
        Client = Rect(Win32.GetClientBounds(t.Hwnd)),
        Overlays = new
        {
            Rng = Overlay(t.Rng),
            BetButtons = Overlay(t.Bet),
            ReportButton = Overlay(t.Report),
        },
    };

    private static object? Overlay(Form? f) => f == null || f.IsDisposed ? null : new { f.Visible, Bounds = Rect(f.Bounds) };

    private static object Rect(Rectangle r) => new { r.X, r.Y, r.Width, r.Height };

    private static string Safe(string s)
    {
        foreach (var c in Path.GetInvalidFileNameChars()) s = s.Replace(c, '_');
        return s.Length > 40 ? s[..40] : s;
    }
}
