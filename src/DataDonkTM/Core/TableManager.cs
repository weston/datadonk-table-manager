using System.Text.RegularExpressions;
using DataDonkTM.Native;
using DataDonkTM.Overlays;

namespace DataDonkTM.Core;

public sealed class TableInfo
{
    public IntPtr Hwnd { get; init; }
    public SiteProfile Site { get; set; } = null!;
    public string Title { get; set; } = "";
    public int? SlotIndex { get; set; }
    public DateTime FirstSeen { get; init; } = DateTime.Now;
    internal RngOverlay? Rng { get; set; }
    internal BetOverlay? Bet { get; set; }
}

/// <summary>
/// Finds poker tables (top-level windows matching a site profile), tiles them into the active
/// profile's slots, and keeps the RNG / bet overlays attached to tables in playing slots.
/// Must be created and used on the UI thread.
/// </summary>
public sealed class TableManager : IDisposable
{
    private readonly Dictionary<IntPtr, TableInfo> _tables = new();
    private readonly List<IntPtr> _hooks = new();
    private readonly Win32.WinEventDelegate _hookProc; // kept alive for the lifetime of the hooks
    private readonly System.Windows.Forms.Timer _scanTimer = new() { Interval = 1000 };
    private readonly System.Windows.Forms.Timer _debounce = new() { Interval = 150 };
    private readonly Dictionary<uint, string> _processNames = new();
    private readonly HashSet<IntPtr> _dragging = new();

    public event Action? TablesChanged;

    public IReadOnlyCollection<TableInfo> Tables => _tables.Values;
    private static AppConfig Cfg => ConfigStore.Current;

    public TableManager()
    {
        _hookProc = OnWinEvent;
        _scanTimer.Tick += (_, _) => Scan();
        _debounce.Tick += (_, _) => { _debounce.Stop(); Scan(); };
    }

    public void Start()
    {
        uint flags = Win32.WINEVENT_OUTOFCONTEXT | Win32.WINEVENT_SKIPOWNPROCESS;
        void Hook(uint min, uint max)
        {
            var h = Win32.SetWinEventHook(min, max, IntPtr.Zero, _hookProc, 0, 0, flags);
            if (h != IntPtr.Zero) _hooks.Add(h);
        }
        Hook(Win32.EVENT_SYSTEM_MOVESIZESTART, Win32.EVENT_SYSTEM_MOVESIZEEND);
        Hook(Win32.EVENT_SYSTEM_MINIMIZESTART, Win32.EVENT_SYSTEM_MINIMIZEEND);
        Hook(Win32.EVENT_OBJECT_DESTROY, Win32.EVENT_OBJECT_HIDE);
        Hook(Win32.EVENT_OBJECT_LOCATIONCHANGE, Win32.EVENT_OBJECT_NAMECHANGE);
        Scan();
        _scanTimer.Start();
    }

    // ---- Detection -------------------------------------------------------------------

    private void OnWinEvent(IntPtr hook, uint ev, IntPtr hwnd, int idObject, int idChild, uint thread, uint time)
    {
        if (idObject != Win32.OBJID_WINDOW || idChild != 0 || hwnd == IntPtr.Zero) return;
        bool known = _tables.TryGetValue(hwnd, out var t);
        switch (ev)
        {
            case Win32.EVENT_OBJECT_LOCATIONCHANGE:
                if (known) UpdateOverlays(t!);
                break;
            case Win32.EVENT_SYSTEM_MOVESIZESTART:
                if (known) _dragging.Add(hwnd);
                break;
            case Win32.EVENT_SYSTEM_MOVESIZEEND:
                if (known) { _dragging.Remove(hwnd); OnDragEnd(t!); }
                break;
            case Win32.EVENT_SYSTEM_MINIMIZESTART:
            case Win32.EVENT_SYSTEM_MINIMIZEEND:
                if (known) UpdateOverlays(t!);
                break;
            default: // show / hide / destroy / title change → rescan soon
                _debounce.Stop();
                _debounce.Start();
                break;
        }
    }

    public void Scan()
    {
        var found = new Dictionary<IntPtr, (SiteProfile Site, string Title)>();
        var sites = Cfg.Sites.Where(s => s.Enabled).ToList();
        if (sites.Count > 0)
        {
            Win32.EnumWindows((hwnd, _) =>
            {
                var site = MatchSite(hwnd, sites, out var title);
                if (site != null) found[hwnd] = (site, title);
                return true;
            }, IntPtr.Zero);
        }

        bool changed = false;
        foreach (var gone in _tables.Keys.Where(h => !found.ContainsKey(h)).ToList())
        {
            RemoveTable(gone);
            changed = true;
        }
        foreach (var (hwnd, (site, title)) in found)
        {
            if (_tables.TryGetValue(hwnd, out var t))
            {
                if (t.Title != title) { t.Title = title; changed = true; }
                if (t.Site != site) { t.Site = site; DisposeOverlays(t); UpdateOverlays(t); changed = true; }
                continue;
            }
            t = new TableInfo { Hwnd = hwnd, Site = site, Title = title };
            _tables[hwnd] = t;
            OnTableAdded(t);
            changed = true;
        }
        if (changed) TablesChanged?.Invoke();
    }

    /// <summary>Returns the first enabled site whose rules match this window, or null.</summary>
    public SiteProfile? MatchSite(IntPtr hwnd, IReadOnlyList<SiteProfile> sites, out string title)
    {
        title = "";
        if (!Win32.IsWindowVisible(hwnd) || Win32.IsCloaked(hwnd)) return null;
        if ((Win32.GetWindowLongPtr(hwnd, Win32.GWL_STYLE).ToInt64() & Win32.WS_CHILD) != 0) return null;
        title = Win32.GetTitle(hwnd);
        string cls = Win32.GetClass(hwnd);
        string proc = GetProcessName(hwnd);
        var bounds = Win32.GetWindowBounds(hwnd);
        foreach (var s in sites)
            if (Matches(s, proc, cls, title, bounds, Win32.IsIconic(hwnd))) return s;
        return null;
    }

    public static bool Matches(SiteProfile s, string processName, string className, string title, Rectangle bounds, bool minimized)
    {
        if (!string.IsNullOrWhiteSpace(s.ProcessName))
        {
            var want = s.ProcessName.Trim();
            if (!want.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) want += ".exe";
            if (!string.Equals(want, processName, StringComparison.OrdinalIgnoreCase)) return false;
        }
        if (!minimized && (bounds.Width < s.MinWidth || bounds.Height < s.MinHeight)) return false;
        if (!RegexOk(s.ClassRegex, className, true)) return false;
        if (!RegexOk(s.TitleRegex, title, true)) return false;
        if (!string.IsNullOrWhiteSpace(s.TitleExcludeRegex) && RegexOk(s.TitleExcludeRegex, title, false)) return false;
        // A site with no rules at all would match every window; refuse that.
        return !string.IsNullOrWhiteSpace(s.ProcessName) || !string.IsNullOrWhiteSpace(s.ClassRegex) || !string.IsNullOrWhiteSpace(s.TitleRegex);
    }

    private static bool RegexOk(string pattern, string input, bool emptyMeans)
    {
        if (string.IsNullOrWhiteSpace(pattern)) return emptyMeans;
        try { return Regex.IsMatch(input, pattern, RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(50)); }
        catch (ArgumentException) { return false; }
        catch (RegexMatchTimeoutException) { return false; }
    }

    public string GetProcessName(IntPtr hwnd)
    {
        Win32.GetWindowThreadProcessId(hwnd, out uint pid);
        if (_processNames.TryGetValue(pid, out var name)) return name;
        name = Path.GetFileName(Win32.GetProcessImagePath(pid) ?? "");
        if (_processNames.Count > 2000) _processNames.Clear();
        _processNames[pid] = name;
        return name;
    }

    // ---- Tiling ----------------------------------------------------------------------

    private void OnTableAdded(TableInfo t)
    {
        var profile = Cfg.ActiveProfile;
        if (Cfg.TilingEnabled && profile is { AutoPlaceNewTables: true })
        {
            var slot = SlotLogic.PickSlotForNewTable(profile.Slots, OccupiedSlots(), profile.NewTablesPreferPlaying);
            if (slot != null) MoveToSlot(t, slot.Value);
        }
        UpdateOverlays(t);
    }

    private HashSet<int> OccupiedSlots(TableInfo? except = null) =>
        _tables.Values.Where(x => x != except && x.SlotIndex != null).Select(x => x.SlotIndex!.Value).ToHashSet();

    private void OnDragEnd(TableInfo t)
    {
        var profile = Cfg.ActiveProfile;
        if (!Cfg.TilingEnabled || profile == null || !profile.SnapOnDrag || profile.Slots.Count == 0)
        {
            UpdateOverlays(t);
            return;
        }
        Win32.GetCursorPos(out var cur);
        int? target = SlotLogic.SlotAtPoint(profile.Slots, new Point(cur.X, cur.Y));
        int? from = t.SlotIndex;

        if (target == null)
        {
            if (from != null) MoveToSlot(t, from.Value); // dropped outside any slot → snap back
            else UpdateOverlays(t);
            return;
        }

        var other = _tables.Values.FirstOrDefault(x => x != t && x.SlotIndex == target);
        if (other != null)
        {
            // Swap: the table already in the target slot takes the dragged table's old slot.
            if (from != null) MoveToSlot(other, from.Value);
            else
            {
                other.SlotIndex = null;
                var free = SlotLogic.PickSlotForNewTable(profile.Slots, OccupiedSlots(other), profile.NewTablesPreferPlaying);
                if (free != null && free != target) MoveToSlot(other, free.Value);
                else UpdateOverlays(other);
            }
        }
        MoveToSlot(t, target.Value);
        TablesChanged?.Invoke();
    }

    private void MoveToSlot(TableInfo t, int slotIndex)
    {
        var profile = Cfg.ActiveProfile;
        if (profile == null || slotIndex < 0 || slotIndex >= profile.Slots.Count) return;
        t.SlotIndex = slotIndex;
        var r = profile.Slots[slotIndex].Bounds;
        if (Win32.IsIconic(t.Hwnd) || Win32.IsZoomed(t.Hwnd)) Win32.ShowWindow(t.Hwnd, Win32.SW_RESTORE);
        Win32.SetWindowPos(t.Hwnd, IntPtr.Zero, r.X, r.Y, r.Width, r.Height,
            Win32.SWP_NOZORDER | Win32.SWP_NOACTIVATE | Win32.SWP_NOOWNERZORDER);
        UpdateOverlays(t);
    }

    /// <summary>Re-places every open table into the active profile (used after activating or editing a profile).</summary>
    public void RetileAll()
    {
        var profile = Cfg.ActiveProfile;
        // Keep previous order: tables that had slots first (by slot), then the rest by age.
        var ordered = _tables.Values.OrderBy(t => t.SlotIndex ?? int.MaxValue).ThenBy(t => t.FirstSeen).ToList();
        foreach (var t in ordered) t.SlotIndex = null;
        if (Cfg.TilingEnabled && profile != null)
        {
            var occupied = new HashSet<int>();
            foreach (var t in ordered)
            {
                var slot = SlotLogic.PickSlotForNewTable(profile.Slots, occupied, profile.NewTablesPreferPlaying);
                if (slot == null) break;
                occupied.Add(slot.Value);
                MoveToSlot(t, slot.Value);
            }
        }
        foreach (var t in ordered) UpdateOverlays(t);
        TablesChanged?.Invoke();
    }

    public void ActivateProfile(string profileId)
    {
        if (Cfg.Profiles.All(p => p.Id != profileId)) return;
        Cfg.ActiveProfileId = profileId;
        ConfigStore.Save();
        RetileAll();
    }

    /// <summary>Current window rectangles of detected tables (for "capture layout" in the editor).</summary>
    public List<Rectangle> CurrentTableRects() => _tables.Values
        .Where(t => !Win32.IsIconic(t.Hwnd))
        .Select(t => Win32.GetWindowBounds(t.Hwnd))
        .Where(r => r.Width > 0)
        .ToList();

    public SlotType? SlotTypeOf(TableInfo t)
    {
        var profile = Cfg.ActiveProfile;
        if (t.SlotIndex is int i && profile != null && i < profile.Slots.Count) return profile.Slots[i].Type;
        return null;
    }

    // ---- Overlays --------------------------------------------------------------------

    /// <summary>Call after settings changed: rebuilds overlays with the new settings.</summary>
    public void RefreshOverlays()
    {
        foreach (var t in _tables.Values)
        {
            DisposeOverlays(t);
            UpdateOverlays(t);
        }
    }

    private void UpdateOverlays(TableInfo t)
    {
        bool show = SlotTypeOf(t) == SlotType.Playing
                    && Win32.IsWindow(t.Hwnd) && Win32.IsWindowVisible(t.Hwnd) && !Win32.IsIconic(t.Hwnd)
                    && !_dragging.Contains(t.Hwnd);
        var client = show ? Win32.GetClientBounds(t.Hwnd) : Rectangle.Empty;
        if (client.Width < 50 || client.Height < 50) show = false;

        // RNG
        if (show && Cfg.Rng.Enabled)
        {
            if (t.Rng == null)
            {
                t.Rng = new RngOverlay(t.Hwnd, Cfg.Rng.Size);
                t.Rng.DragFinished += p => SaveOverlayPos(t, p, (s, v) => s.RngPosition = v);
                t.Rng.ResizeRequested += d => ResizeRng(d);
                t.Rng.ResetPositionRequested += () => ResetPos(t, s => s.RngPosition = new RelPoint(0.02, 0.12));
            }
            t.Rng.PlaceOn(client, t.Site.RngPosition);
            if (!t.Rng.Visible) t.Rng.Show();
        }
        else t.Rng?.Hide();

        // Bet buttons (only for sites whose bet box has been set up)
        if (show && Cfg.Bet.Enabled && t.Site.BetBoxPoint != null)
        {
            if (t.Bet == null)
            {
                t.Bet = new BetOverlay(t.Hwnd, t.Site, Cfg.Bet);
                t.Bet.DragFinished += p => SaveOverlayPos(t, p, (s, v) => s.BetPanelPosition = v);
                t.Bet.ResetPositionRequested += () => ResetPos(t, s => s.BetPanelPosition = new RelPoint(0.55, 0.70));
            }
            t.Bet.PlaceOn(client, t.Site.BetPanelPosition);
            if (!t.Bet.Visible) t.Bet.Show();
        }
        else t.Bet?.Hide();
    }

    /// <summary>Overlay positions are stored per site, so every table of that site uses the new position.</summary>
    private void SaveOverlayPos(TableInfo t, Point screenTopLeft, Action<SiteProfile, RelPoint> set)
    {
        var client = Win32.GetClientBounds(t.Hwnd);
        set(t.Site, OverlayForm.ToRelative(client, screenTopLeft));
        ConfigStore.Save();
        foreach (var other in _tables.Values.Where(x => x.Site == t.Site)) UpdateOverlays(other);
    }

    private void ResetPos(TableInfo t, Action<SiteProfile> reset)
    {
        reset(t.Site);
        ConfigStore.Save();
        foreach (var other in _tables.Values.Where(x => x.Site == t.Site)) UpdateOverlays(other);
    }

    private void ResizeRng(int delta)
    {
        Cfg.Rng.Size = Math.Clamp(Cfg.Rng.Size + delta * 2, 16, 120);
        ConfigStore.Save();
        foreach (var t in _tables.Values)
        {
            t.Rng?.SetBoxSize(Cfg.Rng.Size);
            UpdateOverlays(t);
        }
    }

    private static void DisposeOverlays(TableInfo t)
    {
        t.Rng?.Dispose(); t.Rng = null;
        t.Bet?.Dispose(); t.Bet = null;
    }

    private void RemoveTable(IntPtr hwnd)
    {
        if (_tables.Remove(hwnd, out var t)) DisposeOverlays(t);
        _dragging.Remove(hwnd);
    }

    public void Dispose()
    {
        foreach (var h in _hooks) Win32.UnhookWinEvent(h);
        _hooks.Clear();
        _scanTimer.Dispose();
        _debounce.Dispose();
        foreach (var t in _tables.Values) DisposeOverlays(t);
        _tables.Clear();
    }
}
