using System.Text;
using System.Text.RegularExpressions;
using DataDonkTM.Betting;
using DataDonkTM.Core;
using DataDonkTM.Native;

namespace DataDonkTM.UI;

/// <summary>
/// Edits one site profile: how to detect its tables, and (via a screenshot) where the pot, call button,
/// bet box etc. are. "Test" runs the real OCR/math on the screenshot so you can see what it reads.
/// </summary>
internal sealed class SiteEditorForm : Form
{
    public SiteProfile Profile { get; }
    private readonly TableManager _manager;

    private enum Item { Pot, Call, HeroBet, Blinds, Board, BetBox }

    private static readonly (Item Item, string Label, bool IsPoint, Color Color, string Help)[] Items =
    {
        (Item.Pot, "Pot amount", false, Color.Gold, "Drag a box around the pot number (include the whole number, a little margin is fine). Required for % pot buttons."),
        (Item.Call, "Call button text", false, Color.OrangeRed, "Drag a box over the Call button's label (e.g. 'Call 2.50'). Reads 'Check' as 0. Needed for correct raise sizes."),
        (Item.HeroBet, "Hero's bet in front (optional)", false, Color.Violet, "Drag a box where YOUR chips/bet amount appear in front of you (e.g. your posted blind). Improves raise math from the blinds / when re-raising."),
        (Item.Blinds, "Blinds text (optional)", false, Color.LightSkyBlue, "Only if the stakes are NOT in the window title: box a place on the table that shows the blinds, e.g. 'NLH 0.05/0.10'."),
        (Item.Board, "First flop card", false, Color.LimeGreen, "Box where the FIRST flop card appears. Then, with no board dealt, click 'Board is empty now → save reference'."),
        (Item.BetBox, "Bet amount box (click)", true, Color.Cyan, "Click in the middle of the bet-size input box. The bet buttons click here and type the amount."),
    };

    private readonly ScreenshotCanvas _canvas = new();
    private readonly ComboBox _windows = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 520, FormattingEnabled = true };
    private readonly TextBox _output = new() { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Both, WordWrap = false, Font = new Font("Consolas", 9), Dock = DockStyle.Fill };
    private readonly Dictionary<Item, RadioButton> _radios = new();
    private readonly Label _itemHelp = Ui.Note("", 340);

    private readonly TextBox _name = new() { Width = 220 };
    private readonly CheckBox _enabled = Ui.Chk("Enabled", true);
    private readonly TextBox _process = new() { Width = 220 };
    private readonly TextBox _class = new() { Width = 220 };
    private readonly TextBox _title = new() { Width = 220 };
    private readonly TextBox _exclude = new() { Width = 220 };
    private readonly NumericUpDown _minW = Ui.Num(0, 5000, 300);
    private readonly NumericUpDown _minH = Ui.Num(0, 5000, 200);
    private readonly TextBox _blindsRx = new() { Width = 220 };
    private readonly ComboBox _decimal = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 60 };
    private readonly CheckBox _potIncludes = Ui.Chk("Pot number includes this street's bets", true);
    private readonly CheckBox _inBigBlinds = Ui.Chk("The site shows amounts in big blinds (e.g. \"Pot 12.5 BB\")", false);
    private readonly ComboBox _capture = Ui.Combo(CaptureMethod.PrintWindow);
    private readonly NumericUpDown _threshold = Ui.Num(1, 255, 35);
    private readonly Label _boardStatus = Ui.Lbl("");
    private string _lastCapturedTitle = "";

    public SiteEditorForm(SiteProfile site, TableManager manager)
    {
        Profile = site;
        _manager = manager;
        Text = $"Site: {site.Name}";
        Icon = Ui.AppIcon;
        ClientSize = new Size(1280, 820);
        StartPosition = FormStartPosition.CenterParent;
        MinimumSize = new Size(1000, 640);

        _decimal.Items.AddRange(new object[] { ".", "," });
        LoadFromSite();

        // Left: settings (scrollable). Right: screenshot + test output.
        var left = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true, Padding = new Padding(8) };
        left.Controls.Add(Section("Site"));
        left.Controls.Add(Field("Name", _name));
        left.Controls.Add(_enabled);
        if (!string.IsNullOrWhiteSpace(site.Notes)) left.Controls.Add(Ui.Note(site.Notes, 340));

        left.Controls.Add(Section("1. Detect table windows"));
        left.Controls.Add(Ui.Note("A window counts as a table when every filled-in rule matches. Select a table in the list on the right, then:", 340));
        left.Controls.Add(Ui.Btn("Fill rules from selected window", (_, _) => FillDetectionFromWindow(), 240));
        left.Controls.Add(Field("Process (.exe)", _process));
        left.Controls.Add(Field("Class regex", _class));
        left.Controls.Add(Field("Title regex", _title));
        left.Controls.Add(Field("Title exclude", _exclude));
        left.Controls.Add(Ui.Row(Ui.Lbl("Min size"), _minW, Ui.Lbl("×"), _minH));
        left.Controls.Add(Ui.Btn("Test detection", (_, _) => TestDetection(), 240));

        left.Controls.Add(Section("2. Mark regions on the screenshot"));
        foreach (var it in Items)
        {
            var rb = new RadioButton { AutoSize = true, Tag = it.Item, Margin = new Padding(3, 2, 3, 2) };
            rb.CheckedChanged += (_, _) =>
            {
                if (!rb.Checked) return;
                // Each radio sits in its own row panel, so enforce exclusivity by hand.
                foreach (var other in _radios.Values) if (other != rb) other.Checked = false;
                SelectItem(it.Item);
            };
            _radios[it.Item] = rb;
            var clear = new LinkLabel { Text = "clear", AutoSize = true, Margin = new Padding(6, 4, 3, 2) };
            clear.LinkClicked += (_, _) => { SetItem(it.Item, null, null); };
            left.Controls.Add(Ui.Row(rb, clear));
        }
        left.Controls.Add(_itemHelp);

        left.Controls.Add(Section("3. Street detection (preflop / postflop)"));
        left.Controls.Add(Ui.Btn("Board is empty now → save reference", (_, _) => SaveEmptyBoard(), 260));
        left.Controls.Add(Ui.Row(Ui.Lbl("Difference threshold"), _threshold));
        left.Controls.Add(_boardStatus);

        left.Controls.Add(Section("4. Reading numbers"));
        left.Controls.Add(Field("Blinds regex", _blindsRx));
        left.Controls.Add(Ui.Note("Applied to the window title (then to the blinds region). Needs a group named bb.", 340));
        left.Controls.Add(Ui.Row(Ui.Lbl("Decimal separator"), _decimal));
        left.Controls.Add(_inBigBlinds);
        left.Controls.Add(_potIncludes);
        left.Controls.Add(Ui.Note("Most sites show the total pot including current bets. If yours shows only the pot from previous streets (bets sit separately in front of players), untick this.", 340));
        left.Controls.Add(Field("Capture", _capture));
        left.Controls.Add(Ui.Note("PrintWindow reads the table even if something covers it. Use Screen if screenshots come out black or stale.", 340));


        var ok = new Button { Text = "Save", Width = 100, Height = 30, DialogResult = DialogResult.None };
        ok.Click += (_, _) => { if (ApplyToSite()) { DialogResult = DialogResult.OK; Close(); } };
        var cancel = new Button { Text = "Cancel", Width = 100, Height = 30, DialogResult = DialogResult.Cancel };
        CancelButton = cancel;
        left.Controls.Add(Section(""));
        left.Controls.Add(Ui.Row(ok, cancel));

        // Right side
        _windows.Format += (_, e) => e.Value = e.ListItem is WinEntry w ? w.ToString() : "";
        var zoom = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 70 };
        zoom.Items.AddRange(new object[] { "Fit", "100%", "150%", "200%", "300%" });
        zoom.SelectedIndex = 0;
        zoom.SelectedIndexChanged += (_, _) => _canvas.Zoom = zoom.SelectedIndex switch { 1 => 1f, 2 => 1.5f, 3 => 2f, 4 => 3f, _ => 0f };

        var toolbar = Ui.Row(
            Ui.Lbl("Window"), _windows,
            Ui.Btn("↻", (_, _) => RefreshWindows(), 32),
            Ui.Btn("Capture", (_, _) => CaptureSelected(), 80),
            Ui.Btn("Load image", (_, _) => LoadImage(), 95),
            Ui.Lbl("Zoom"), zoom,
            Ui.Btn("Test read", async (_, _) => await TestRead(), 90));

        var canvasHost = new Panel { Dock = DockStyle.Fill, AutoScroll = true, BackColor = _canvas.BackColor };
        canvasHost.Controls.Add(_canvas);
        _canvas.Markers = Markers;
        _canvas.RectDrawn += r => { if (CurrentItem is Item it) SetItem(it, r, null); };
        _canvas.PointPicked += p => { if (CurrentItem is Item it) SetItem(it, null, p); };

        var right = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 3, ColumnCount = 1 };
        right.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        right.RowStyles.Add(new RowStyle(SizeType.Percent, 75));
        right.RowStyles.Add(new RowStyle(SizeType.Percent, 25));
        right.Controls.Add(toolbar, 0, 0);
        right.Controls.Add(canvasHost, 0, 1);
        right.Controls.Add(_output, 0, 2);

        var split = new SplitContainer { Dock = DockStyle.Fill, FixedPanel = FixedPanel.Panel1 };
        split.Panel1.Controls.Add(left);
        split.Panel2.Controls.Add(right);
        Controls.Add(split);
        Load += (_, _) => { split.SplitterDistance = 390; };

        RefreshWindows();
        _radios[Item.Pot].Checked = true;
        UpdateRadioLabels();
    }

    // ---- Layout helpers -------------------------------------------------------------

    private Label Section(string text) => new()
    {
        Text = text, AutoSize = true, Font = new Font(Font, FontStyle.Bold), Margin = new Padding(3, 14, 3, 4),
    };

    private static FlowLayoutPanel Field(string label, Control c)
    {
        var l = Ui.Lbl(label, false);
        l.Width = 110;
        return Ui.Row(l, c);
    }

    // ---- Model <-> UI ---------------------------------------------------------------

    private void LoadFromSite()
    {
        var s = Profile;
        _name.Text = s.Name;
        _enabled.Checked = s.Enabled;
        _process.Text = s.ProcessName;
        _class.Text = s.ClassRegex;
        _title.Text = s.TitleRegex;
        _exclude.Text = s.TitleExcludeRegex;
        _minW.Value = Math.Clamp(s.MinWidth, 0, 5000);
        _minH.Value = Math.Clamp(s.MinHeight, 0, 5000);
        _blindsRx.Text = s.BlindsRegex;
        _decimal.SelectedItem = s.DecimalSeparator == "," ? "," : ".";
        _potIncludes.Checked = s.PotIncludesStreetBets;
        _inBigBlinds.Checked = s.AmountsInBigBlinds;
        _capture.SelectedItem = s.Capture;
        _threshold.Value = (decimal)Math.Clamp(s.BoardDiffThreshold, 1, 255);
        _boardStatus.Text = string.IsNullOrEmpty(s.EmptyBoardSignature) ? "Empty-board reference: not saved" : "Empty-board reference: saved";
    }

    private bool ApplyToSite()
    {
        foreach (var (box, what) in new[] { (_class, "Class regex"), (_title, "Title regex"), (_exclude, "Title exclude"), (_blindsRx, "Blinds regex") })
        {
            if (string.IsNullOrWhiteSpace(box.Text)) continue;
            try { _ = new Regex(box.Text); }
            catch (ArgumentException ex) { MessageBox.Show(this, $"{what} is not a valid regex: {ex.Message}"); return false; }
        }
        var s = Profile;
        s.Name = string.IsNullOrWhiteSpace(_name.Text) ? "Unnamed site" : _name.Text.Trim();
        s.Enabled = _enabled.Checked;
        s.ProcessName = _process.Text.Trim();
        s.ClassRegex = _class.Text.Trim();
        s.TitleRegex = _title.Text.Trim();
        s.TitleExcludeRegex = _exclude.Text.Trim();
        s.MinWidth = (int)_minW.Value;
        s.MinHeight = (int)_minH.Value;
        s.BlindsRegex = _blindsRx.Text.Trim();
        s.DecimalSeparator = (string)_decimal.SelectedItem!;
        s.PotIncludesStreetBets = _potIncludes.Checked;
        s.AmountsInBigBlinds = _inBigBlinds.Checked;
        s.Capture = (CaptureMethod)_capture.SelectedItem!;
        s.BoardDiffThreshold = (double)_threshold.Value;
        return true;
    }

    // ---- Regions --------------------------------------------------------------------

    private Item? CurrentItem
    {
        get
        {
            foreach (var (item, rb) in _radios) if (rb.Checked) return item;
            return null;
        }
    }

    private void SelectItem(Item item)
    {
        var info = Items.First(i => i.Item == item);
        _canvas.PickPoint = info.IsPoint;
        _itemHelp.Text = info.Help;
    }

    private (RelRect? Rect, RelPoint? Point) GetItem(Item item) => item switch
    {
        Item.Pot => (Profile.PotRegion, null),
        Item.Call => (Profile.CallRegion, null),
        Item.HeroBet => (Profile.HeroBetRegion, null),
        Item.Blinds => (Profile.BlindsRegion, null),
        Item.Board => (Profile.BoardRegion, null),
        Item.BetBox => (null, Profile.BetBoxPoint),
        _ => (null, null),
    };

    private void SetItem(Item item, RelRect? rect, RelPoint? point)
    {
        switch (item)
        {
            case Item.Pot: Profile.PotRegion = rect; break;
            case Item.Call: Profile.CallRegion = rect; break;
            case Item.HeroBet: Profile.HeroBetRegion = rect; break;
            case Item.Blinds: Profile.BlindsRegion = rect; break;
            case Item.Board:
                Profile.BoardRegion = rect;
                Profile.EmptyBoardSignature = null; // region changed → old reference is meaningless
                _boardStatus.Text = "Empty-board reference: not saved (region changed)";
                break;
            case Item.BetBox: Profile.BetBoxPoint = point; break;
        }
        UpdateRadioLabels();
        _canvas.Invalidate();
    }

    private void UpdateRadioLabels()
    {
        foreach (var it in Items)
        {
            var (r, p) = GetItem(it.Item);
            _radios[it.Item].Text = $"{((r != null || p != null) ? "✔" : "○")}  {it.Label}";
        }
    }

    private IEnumerable<CanvasMarker> Markers()
    {
        foreach (var it in Items)
        {
            var (r, p) = GetItem(it.Item);
            if (r != null || p != null) yield return new CanvasMarker(it.Label.Split(" (")[0], it.Color, r, p);
        }
    }

    // ---- Windows / capture ----------------------------------------------------------

    private sealed record WinEntry(IntPtr Hwnd, string Process, string Class, string Title, bool IsMatch)
    {
        public override string ToString() => $"{(IsMatch ? "★ " : "")}{Process} — {Title}  [{Class}]";
    }

    private void RefreshWindows()
    {
        ApplyToSite();
        var list = new List<WinEntry>();
        Win32.EnumWindows((hwnd, _) =>
        {
            if (!Win32.IsWindowVisible(hwnd) || Win32.IsCloaked(hwnd)) return true;
            var title = Win32.GetTitle(hwnd);
            var b = Win32.GetWindowBounds(hwnd);
            if (string.IsNullOrWhiteSpace(title) || b.Width < 150 || b.Height < 100) return true;
            if (hwnd == Handle) return true;
            var proc = _manager.GetProcessName(hwnd);
            var cls = Win32.GetClass(hwnd);
            bool match = TableManager.Matches(Profile, proc, cls, title, b, Win32.IsIconic(hwnd));
            list.Add(new WinEntry(hwnd, proc, cls, title, match));
            return true;
        }, IntPtr.Zero);
        var prev = _windows.SelectedItem as WinEntry;
        _windows.Items.Clear();
        foreach (var w in list.OrderByDescending(w => w.IsMatch).ThenBy(w => w.Process)) _windows.Items.Add(w);
        var again = list.FirstOrDefault(w => w.Hwnd == prev?.Hwnd) ?? list.OrderByDescending(w => w.IsMatch).FirstOrDefault();
        if (again != null) _windows.SelectedItem = _windows.Items.Cast<WinEntry>().First(w => w.Hwnd == again.Hwnd);
    }

    private WinEntry? SelectedWindow => _windows.SelectedItem as WinEntry;

    private void FillDetectionFromWindow()
    {
        if (SelectedWindow is not { } w) { MessageBox.Show(this, "Select a table window in the list on the right first."); return; }
        _process.Text = w.Process;
        _class.Text = "^" + Regex.Escape(w.Class) + "$";
        // Stakes in the title (e.g. "0.05/0.10") distinguish tables from lobbies on most sites.
        _title.Text = Regex.IsMatch(w.Title, @"\d\s*/\s*\D{0,3}\d") ? @"\d\s*/\s*\D{0,3}\d" : "";
        var b = Win32.GetWindowBounds(w.Hwnd);
        _minW.Value = Math.Clamp(Math.Min(300, b.Width / 2), 0, 5000);
        _minH.Value = Math.Clamp(Math.Min(200, b.Height / 2), 0, 5000);
        MessageBox.Show(this,
            "Filled in process, window class and (if the title contains stakes) a title rule.\n\n" +
            "Important: open the site's LOBBY too and click 'Test detection' - the lobby must NOT match. " +
            "If it does, add a Title exclude (e.g. Lobby) or adjust the class/title rules.", "Detection");
    }

    private void TestDetection()
    {
        RefreshWindows();
        var matches = _windows.Items.Cast<WinEntry>().Where(w => w.IsMatch).ToList();
        var sb = new StringBuilder();
        sb.AppendLine($"{matches.Count} window(s) currently match these rules:");
        foreach (var m in matches) sb.AppendLine("  ★ " + m);
        if (matches.Count == 0) sb.AppendLine("  (none - is a table open? are the rules too strict?)");
        _output.Text = sb.ToString();
    }

    private void CaptureSelected()
    {
        if (SelectedWindow is not { } w) { MessageBox.Show(this, "Select a window first."); return; }
        ApplyToSite();
        var bmp = TableCapture.CaptureClient(w.Hwnd, Profile.Capture);
        if (bmp == null) { MessageBox.Show(this, "Capture failed (is the window minimised?)."); return; }
        _canvas.Image = bmp;
        _lastCapturedTitle = w.Title;
        _output.Text = $"Captured {bmp.Width}×{bmp.Height} client area of: {w.Title}\r\nNow pick an item on the left and drag/click on the screenshot.";
        UpdateBoardStatus();
    }

    private void LoadImage()
    {
        using var dlg = new OpenFileDialog { Filter = "Images|*.png;*.jpg;*.jpeg;*.bmp" };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        using var tmp = new Bitmap(dlg.FileName);
        _canvas.Image = new Bitmap(tmp);
        _lastCapturedTitle = "";
        _output.Text = "Loaded image. It must show exactly the table's client area (no window border) for positions to be correct.";
    }

    private void SaveEmptyBoard()
    {
        if (_canvas.Image == null) { MessageBox.Show(this, "Capture a table first (with no flop dealt)."); return; }
        if (Profile.BoardRegion == null) { MessageBox.Show(this, "Mark the 'First flop card' region first."); return; }
        using var crop = TableCapture.Crop(_canvas.Image, Profile.BoardRegion);
        Profile.EmptyBoardSignature = TableCapture.Signature(crop);
        UpdateBoardStatus();
    }

    private void UpdateBoardStatus()
    {
        if (string.IsNullOrEmpty(Profile.EmptyBoardSignature) || Profile.BoardRegion == null) return;
        if (_canvas.Image == null) { _boardStatus.Text = "Empty-board reference: saved"; return; }
        Profile.BoardDiffThreshold = (double)_threshold.Value;
        var post = TableCapture.IsPostflop(_canvas.Image, Profile, out var diff);
        _boardStatus.Text = $"Reference saved. This screenshot: diff {diff:0.0} → {(post == true ? "POSTFLOP" : "PREFLOP")}";
    }

    // ---- Tests ----------------------------------------------------------------------

    private async Task TestRead()
    {
        if (_canvas.Image == null) { MessageBox.Show(this, "Capture a table first."); return; }
        if (!ApplyToSite()) return;
        _output.Text = "Reading…";
        try
        {
            var reading = await TableReader.ReadAsync(_canvas.Image, _lastCapturedTitle, Profile);
            var sb = new StringBuilder(reading.Describe());
            var ctx = reading.ToContext(Profile);
            var list = reading.Postflop == true ? ConfigStore.Current.Bet.Postflop : ConfigStore.Current.Bet.Preflop;
            sb.AppendLine($"Total pot used for % buttons: {BetCalculator.TotalPot(ctx):0.##}");
            sb.Append(reading.Postflop == true ? "Postflop" : "Preflop").Append(" buttons would enter: ");
            sb.AppendLine(string.Join("   ", list.Select(b =>
                $"{b.DisplayText}→{(BetCalculator.RaiseTo(b, ctx, ConfigStore.Current.Bet.Rounding) is double v ? BetCalculator.Format(v, Profile.DecimalSeparator) : "?")}")));
            _output.Text = sb.ToString().Replace("\n", "\r\n").Replace("\r\r", "\r");
            UpdateBoardStatus();
        }
        catch (Exception ex) { _output.Text = "OCR failed: " + ex.Message; }
    }
}
