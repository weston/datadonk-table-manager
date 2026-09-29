using System.Text;
using System.Text.RegularExpressions;
using DataDonkTM.Betting;
using DataDonkTM.Core;
using DataDonkTM.Native;

namespace DataDonkTM.UI;

/// <summary>
/// Guided site setup: one instruction per screen, with instant feedback (e.g. "I read the pot as 1.50 ✓").
/// Covers what most people need; everything else is in the Advanced editor.
/// </summary>
internal sealed class SiteWizardForm : Form
{
    public SiteProfile Profile { get; }
    private readonly TableManager _manager;

    private enum Step { PickLobby, PickTable, Pot, Call, BetBox, Board, EmptyBoard, Done }
    private Step _step = Step.PickLobby;

    private static readonly string[] StepNames = { "Pick the lobby", "Pick a table", "Pot", "Call button", "Bet box", "Flop card", "Empty board", "Finish" };
    private readonly FlowLayoutPanel _progress = new() { AutoSize = true, WrapContents = false, Margin = new Padding(0) };
    private readonly Label _stepTitle = new() { AutoSize = true, Font = new Font("Segoe UI", 16, FontStyle.Bold), Margin = new Padding(0, 0, 0, 6) };
    private readonly Label _instruction = new() { AutoSize = true, Font = new Font("Segoe UI", 11), MaximumSize = new Size(1000, 0) };
    private readonly Label _feedback = new() { AutoSize = true, Font = new Font("Segoe UI", 11, FontStyle.Bold), MaximumSize = new Size(1000, 0), Margin = new Padding(0, 6, 0, 0) };
    private readonly Panel _content = new() { Dock = DockStyle.Fill };
    private readonly ScreenshotCanvas _canvas = new();
    private readonly Panel _canvasHost = new() { Dock = DockStyle.Fill, AutoScroll = true };
    private readonly ListBox _windows = new() { Dock = DockStyle.Fill, Font = new Font("Segoe UI", 11), IntegralHeight = false, FormattingEnabled = true };
    private readonly ListBox _lobbies = new() { Dock = DockStyle.Fill, Font = new Font("Segoe UI", 11), IntegralHeight = false, FormattingEnabled = true };
    private readonly Button _refresh = new() { Text = "Refresh list", Width = 110, Height = 34 };
    private readonly Button _back = new() { Text = "◀ Back", Width = 100, Height = 34 };
    private readonly Button _next = new() { Text = "Next ▶", Width = 120, Height = 34 };
    private readonly Button _skip = new() { Text = "Skip", Width = 90, Height = 34 };
    private readonly Button _skipBets = new() { Text = "Skip bet buttons", Width = 140, Height = 34 };
    private readonly Button _retake = new() { Text = "Retake picture", Width = 130, Height = 34 };
    private readonly Button _emptyNow = new() { Text = "The board is empty right now", Width = 240, Height = 34 };
    private readonly TextBox _name = new() { Width = 260, Font = new Font("Segoe UI", 11) };
    private readonly CheckBox _inBigBlinds = new()
    {
        Text = "This site shows amounts in big blinds (e.g. \"Pot 12.5 BB\"), so bets are typed in big blinds",
        AutoSize = true, Font = new Font("Segoe UI", 10.5f), Margin = new Padding(3, 8, 3, 8),
    };
    private readonly TextBox _summary = new() { Multiline = true, ReadOnly = true, Dock = DockStyle.Fill, Font = new Font("Consolas", 10), ScrollBars = ScrollBars.Vertical };

    private IntPtr _table;
    private string _tableTitle = "";
    private int _ocrToken;

    public SiteWizardForm(SiteProfile profile, TableManager manager)
    {
        Profile = profile;
        _manager = manager;
        Text = "Set up a poker site";
        Icon = Ui.AppIcon;
        ClientSize = new Size(1100, 800);
        MinimumSize = new Size(800, 600);
        StartPosition = FormStartPosition.CenterParent;
        Font = new Font("Segoe UI", 9.5f);

        _canvasHost.BackColor = _canvas.BackColor;
        _canvasHost.Controls.Add(_canvas);
        _canvas.Markers = Markers;
        _canvas.RectDrawn += OnRectDrawn;
        _canvas.PointPicked += OnPointPicked;
        _windows.Format += (_, e) => e.Value = e.ListItem?.ToString();
        _windows.SelectedIndexChanged += (_, _) => UpdateButtons();
        _windows.DoubleClick += (_, _) => { if (_next.Enabled) GoNext(); };
        _lobbies.Format += (_, e) => e.Value = e.ListItem?.ToString();
        _lobbies.SelectedIndexChanged += (_, _) => UpdateButtons();
        _lobbies.DoubleClick += (_, _) => { if (_next.Enabled) GoNext(); };
        _refresh.Click += (_, _) => { if (_step == Step.PickTable) FillWindowList(); else FillLobbyList(); UpdateButtons(); };

        _back.Click += (_, _) => Go(_step - 1);
        _next.Click += (_, _) => GoNext();
        _skip.Click += (_, _) => { if (_step == Step.PickLobby) _lobby = null; Go(_editing ? Step.Done : _step + 1); };
        _retake.Click += (_, _) => TakePicture();
        _emptyNow.Click += (_, _) => SaveEmptyBoardNow();

        var header = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Padding = new Padding(16, 10, 16, 8) };
        header.Controls.AddRange(new Control[] { _progress, _stepTitle, _instruction, _feedback });
        for (int i = 0; i < StepNames.Length; i++)
        {
            var target = (Step)i;
            var label = new Label { AutoSize = true, Font = new Font("Segoe UI", 10), Padding = new Padding(8, 4, 8, 4), Margin = new Padding(0, 0, 4, 10) };
            // Jump to a step: any step when changing an existing site, otherwise only steps already reached.
            label.Click += (_, _) => { if (_editing || target <= _furthest) Go(target); };
            _progress.Controls.Add(label);
        }

        var footer = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(12, 8, 12, 12) };
        var cancel = new Button { Text = "Cancel", Width = 90, Height = 34, DialogResult = DialogResult.Cancel };
        _skipBets.Click += (_, _) => Go(Step.Done);
        _inBigBlinds.CheckedChanged += (_, _) =>
        {
            if (Profile.AmountsInBigBlinds == _inBigBlinds.Checked) return;
            Profile.AmountsInBigBlinds = _inBigBlinds.Checked;
            if (_step == Step.Done) _ = BuildSummaryAsync();
        };
        footer.Controls.AddRange(new Control[] { _next, _skip, _skipBets, _back, cancel, _emptyNow, _retake, _refresh });
        CancelButton = cancel;

        _content.Padding = new Padding(16, 0, 16, 0);
        Controls.Add(_content);
        Controls.Add(footer);
        Controls.Add(header);

        // An existing site opens on the summary, where any single step can be redone.
        _editing = !string.IsNullOrEmpty(profile.ProcessName);
        if (_editing)
        {
            Text = $"Change setup: {profile.Name}";
            FindOpenTable();
            Go(Step.Done);
        }
        else Go(Step.PickLobby);
    }

    private readonly bool _editing;
    private Step _furthest = Step.PickLobby;

    /// <summary>For an existing site: use an open table of that site for pictures.</summary>
    private void FindOpenTable()
    {
        _table = IntPtr.Zero;
        Win32.EnumWindows((h, _) =>
        {
            if (!Win32.IsWindowVisible(h) || Win32.IsIconic(h)) return true;
            if (!TableManager.Matches(Profile, _manager.GetProcessName(h), Win32.GetClass(h), Win32.GetTitle(h), Win32.GetWindowBounds(h), false)) return true;
            _table = h;
            _tableTitle = Win32.GetTitle(h);
            return false;
        }, IntPtr.Zero);
    }

    // ---- Navigation -----------------------------------------------------------------

    private void GoNext()
    {
        if (_step == Step.PickTable && !UseSelectedWindow()) return;
        if (_step == Step.PickLobby && !UseSelectedLobby()) return;
        if (_step == Step.Done) { Finish(); return; }
        // When changing an existing site, each step returns to the summary.
        Go(_editing ? Step.Done : _step + 1);
    }

    private void Go(Step step)
    {
        if (step < Step.PickLobby || step > Step.Done) return;
        _step = step;
        if (step > _furthest) _furthest = step;
        _ocrToken++;
        _feedback.Text = "";
        _content.Controls.Clear();
        switch (step)
        {
            case Step.PickLobby:
                Show("Step 1: Select the lobby of the site you want to set up",
                    "1. Open your poker site's lobby.\n" +
                    "2. Click the lobby in the list below.\n" +
                    "3. Press Next.\n" +
                    "Don't see it? Click \"Refresh list\". If the site has no separate lobby window, press Skip.");
                FillLobbyList();
                _content.Controls.Add(_lobbies);
                break;

            case Step.PickTable:
                Show("Step 2: Select a table from that site",
                    "1. Open any table on the site (play money is fine).\n" +
                    "2. Click that table in the list below.  (Tables are usually marked ★ because their title shows the stakes.)\n" +
                    "3. Press Next.\n" +
                    "Don't see it? Click \"Refresh list\".");
                FillWindowList();
                _content.Controls.Add(_windows);
                break;

            case Step.Pot:
                Show("Step 3: Draw a box around the pot",
                    "Steps 3-7 are only needed for the bet-size buttons. Only want table tiling and the RNG? Click \"Skip bet buttons\".\n" +
                    "1. Wait until a hand is being played, so the pot amount shows in the middle of the table, then click \"Retake picture\".\n" +
                    "2. On the picture below, press and hold the mouse, and drag a box around the pot number (e.g. \"Pot: 1.50\").\n" +
                    "3. Check the green message: it should show the pot amount. Then press Next.");
                ShowCanvas(pickPoint: false);
                if (Profile.PotRegion != null) _ = ReadRegionAsync(Profile.PotRegion, true);
                break;

            case Step.Call:
                Show("Step 4: Draw a box around the Call button",
                    "1. Wait until it's your turn AND someone has bet, so the Call button shows an amount, then click \"Retake picture\".\n" +
                    "2. Drag a box around the Call button's text (e.g. \"Call 0.50\").\n" +
                    "3. Check the green message shows the call amount. Then press Next.\n" +
                    "(Optional: press Skip if you can't get this now. Raise sizes will then be less accurate when facing a bet.)");
                ShowCanvas(pickPoint: false);
                if (Profile.CallRegion != null) _ = ReadRegionAsync(Profile.CallRegion, false);
                break;

            case Step.BetBox:
                Show("Step 5: Click on the box where you type your bet",
                    "1. Wait until it's your turn, so the bet amount box shows, then click \"Retake picture\".\n" +
                    "2. Click ONCE in the middle of the bet amount box on the picture.\n" +
                    "3. Press Next.\n" +
                    "When you use a bet button, the app types the amount into this box. It never clicks Bet/Raise for you.\n" +
                    "(If you skip this step, bet-size buttons won't show on this site's tables.)");
                ShowCanvas(pickPoint: true);
                if (Profile.BetBoxPoint != null) SetFeedback(true, "Bet box marked.");
                break;

            case Step.Board:
                Show("Step 6: Draw a box where the first flop card goes",
                    "1. Drag a box over the spot where the FIRST (left-most) flop card appears. It's fine if the spot is empty right now.\n" +
                    "2. Press Next.\n" +
                    "This lets the app switch between your preflop and postflop bet buttons by itself. (Optional: Skip, and switch them by hand.)");
                ShowCanvas(pickPoint: false);
                if (Profile.BoardRegion != null) SetFeedback(true, "Flop card area marked.");
                break;

            case Step.EmptyBoard:
                Show("Step 7: Save what an empty board looks like",
                    "1. Wait until there are NO cards on the board (before the flop, or between hands).\n" +
                    "2. Click \"The board is empty right now\".\n" +
                    "3. Press Next.");
                ShowCanvas(pickPoint: false);
                _canvas.Enabled = false;
                if (!string.IsNullOrEmpty(Profile.EmptyBoardSignature)) SetFeedback(true, "Empty board saved.");
                break;

            case Step.Done:
                if (_editing)
                    Show("Change a part of this site's setup",
                        "1. To redo one part, click it in the bar at the top (e.g. \"3  Pot\").\n" +
                        "2. Redo that step, then press \"Back to summary\".\n" +
                        "3. When you're happy, click Finish to save. (Cancel keeps the old setup.)");
                else
                    Show("Step 8: Name the site and finish",
                        "1. Type a name for this site (e.g. \"PokerStars\").\n" +
                        "2. Check the list below - everything should have a ✔.\n" +
                        "3. Click Finish.");
                if (string.IsNullOrWhiteSpace(_name.Text)) _name.Text = Profile.Name;
                _inBigBlinds.Checked = Profile.AmountsInBigBlinds;
                var top = Ui.Column(Ui.Row(Ui.Lbl("Site name:"), _name), _inBigBlinds);
                top.Dock = DockStyle.Top;
                _content.Controls.Add(_summary);
                _content.Controls.Add(top);
                _ = BuildSummaryAsync();
                break;
        }
        UpdateButtons();
    }

    private void Show(string title, string instruction)
    {
        // Progress strip: finished steps ✔, current step highlighted, later steps grey.
        for (int i = 0; i < _progress.Controls.Count; i++)
        {
            var l = (Label)_progress.Controls[i];
            bool current = i == (int)_step, done = !_editing && i < (int)_step;
            l.Cursor = _editing || i <= (int)_furthest ? Cursors.Hand : Cursors.Default;
            l.Text = $"{(done ? "✔" : (i + 1).ToString())}  {StepNames[i]}";
            l.BackColor = current ? Color.FromArgb(30, 120, 80) : done ? Color.FromArgb(220, 238, 226) : Color.FromArgb(235, 235, 235);
            l.ForeColor = current ? Color.White : done ? Color.FromArgb(20, 90, 50) : Color.Gray;
            l.Font = new Font("Segoe UI", 10, current ? FontStyle.Bold : FontStyle.Regular);
        }
        _stepTitle.Text = title;
        _instruction.Text = instruction;
    }

    private void ShowCanvas(bool pickPoint)
    {
        _canvas.Enabled = true;
        _canvas.PickPoint = pickPoint;
        _content.Controls.Add(_canvasHost);
        if (_canvas.Image == null) TakePicture();
        _canvas.Invalidate();
    }

    private void UpdateButtons()
    {
        _back.Enabled = _step > Step.PickLobby;
        _back.Visible = !_editing;
        _next.Text = _step == Step.Done ? "Finish ✔" : _editing ? "Back to summary" : "Next ▶";
        _next.Width = _editing && _step != Step.Done ? 150 : 120;
        _next.Enabled = _step switch
        {
            Step.PickTable => _windows.SelectedItem != null,
            Step.PickLobby => _lobbies.SelectedItem != null,
            Step.Pot => Profile.PotRegion != null,
            Step.Call => Profile.CallRegion != null,
            Step.BetBox => Profile.BetBoxPoint != null,
            Step.Board => Profile.BoardRegion != null,
            Step.EmptyBoard => !string.IsNullOrEmpty(Profile.EmptyBoardSignature),
            _ => true,
        };
        _skip.Visible = _step is Step.PickLobby or Step.Pot or Step.Call or Step.BetBox or Step.Board or Step.EmptyBoard;
        _skipBets.Visible = !_editing && _step is Step.Pot or Step.Call or Step.BetBox or Step.Board or Step.EmptyBoard;
        _refresh.Visible = _step is Step.PickTable or Step.PickLobby;
        _retake.Visible = _step is Step.Pot or Step.Call or Step.BetBox or Step.Board;
        _emptyNow.Visible = _step == Step.EmptyBoard;
    }

    private void SetFeedback(bool ok, string text)
    {
        _feedback.ForeColor = ok ? Color.FromArgb(20, 130, 60) : Color.FromArgb(190, 50, 30);
        _feedback.Text = (ok ? "✔ " : "✖ ") + text;
    }

    // ---- Steps 1-2: pick the lobby, then a table ---------------------------------------

    private sealed record WinEntry(IntPtr Hwnd, string Process, string Class, string Title, bool LooksLikeTable)
    {
        public override string ToString() => $"{(LooksLikeTable ? "★ " : "    ")}{Title}      ({Process})";
    }

    private static readonly Regex StakesRx = new(@"\d\s*/\s*\D{0,3}\d", RegexOptions.Compiled);
    private WinEntry? _lobby;

    /// <summary>Visible top-level windows of other programs, optionally only one program's.</summary>
    private List<WinEntry> ListWindows(string? onlyProcess, IntPtr except)
    {
        var list = new List<WinEntry>();
        uint ownPid = (uint)Environment.ProcessId;
        Win32.EnumWindows((hwnd, _) =>
        {
            if (hwnd == except || !Win32.IsWindowVisible(hwnd) || Win32.IsCloaked(hwnd)) return true;
            Win32.GetWindowThreadProcessId(hwnd, out uint pid);
            if (pid == ownPid) return true;
            var title = Win32.GetTitle(hwnd);
            var b = Win32.GetWindowBounds(hwnd);
            if (string.IsNullOrWhiteSpace(title) || b.Width < 200 || b.Height < 150) return true;
            var proc = _manager.GetProcessName(hwnd);
            if (proc.Equals("explorer.exe", StringComparison.OrdinalIgnoreCase)) return true;
            if (onlyProcess != null && !proc.Equals(onlyProcess, StringComparison.OrdinalIgnoreCase)) return true;
            list.Add(new WinEntry(hwnd, proc, Win32.GetClass(hwnd), title, StakesRx.IsMatch(title)));
            return true;
        }, IntPtr.Zero);
        return list;
    }

    private void FillLobbyList()
    {
        var list = ListWindows(null, IntPtr.Zero);
        _lobbies.Items.Clear();
        // Lobbies usually have no stakes in their title, so list those first.
        foreach (var w in list.OrderBy(w => w.LooksLikeTable).ThenBy(w => w.Title)) _lobbies.Items.Add(w);
        var current = list.FirstOrDefault(w => w.Hwnd == _lobby?.Hwnd);
        if (current != null) _lobbies.SelectedItem = current;
        _feedback.Text = "";
    }

    private bool UseSelectedLobby()
    {
        if (_lobbies.SelectedItem is not WinEntry lobby) return false;
        _lobby = lobby;
        if (Profile.Name is "New site" or "" && !string.IsNullOrEmpty(lobby.Process))
            Profile.Name = Path.GetFileNameWithoutExtension(lobby.Process);
        return true;
    }

    /// <summary>After a lobby is picked, only that program's other windows are offered as tables.</summary>
    private void FillWindowList()
    {
        var list = ListWindows(_lobby?.Process, _lobby?.Hwnd ?? IntPtr.Zero);
        _windows.Items.Clear();
        foreach (var w in list.OrderByDescending(w => w.LooksLikeTable).ThenBy(w => w.Title)) _windows.Items.Add(w);
        var current = list.FirstOrDefault(w => w.Hwnd == _table);
        if (current != null) _windows.SelectedItem = current;
        if (list.Count == 0)
            SetFeedback(false, _lobby != null
                ? $"No tables from {_lobby.Process} are open. Open a table, then click \"Refresh list\"."
                : "No windows found. Open a table, then click \"Refresh list\".");
        else _feedback.Text = "";
    }

    private bool UseSelectedWindow()
    {
        if (_windows.SelectedItem is not WinEntry w) return false;
        var before = Profile.Clone();
        _table = w.Hwnd;
        _tableTitle = w.Title;
        Profile.ProcessName = w.Process;
        Profile.ClassRegex = "^" + Regex.Escape(w.Class) + "$";
        Profile.TitleRegex = w.LooksLikeTable ? StakesRx.ToString() : "";
        Profile.TitleExcludeRegex = "(?i)lobby";
        var b = Win32.GetWindowBounds(w.Hwnd);
        Profile.MinWidth = Math.Min(300, b.Width / 2);
        Profile.MinHeight = Math.Min(200, b.Height / 2);
        if (Profile.Name is "New site" or "" && !string.IsNullOrEmpty(w.Process))
            Profile.Name = Path.GetFileNameWithoutExtension(w.Process);

        bool Matches(IntPtr h) => TableManager.Matches(Profile, _manager.GetProcessName(h), Win32.GetClass(h), Win32.GetTitle(h),
            Win32.GetWindowBounds(h), Win32.IsIconic(h));
        if (_lobby != null && Win32.IsWindow(_lobby.Hwnd) && Matches(_lobby.Hwnd))
        {
            // The rules would treat the lobby as a table: exclude its exact title.
            Profile.TitleExcludeRegex += "|^" + Regex.Escape(_lobby.Title) + "$";
            if (!Matches(_table))
            {
                Profile.TitleExcludeRegex = before.TitleExcludeRegex;
                MessageBox.Show(this, "This table and the lobby look identical to Windows, so they can't be told apart.\n" +
                                      "Check that you picked the lobby in step 1 and a table here.", "Table");
                return false;
            }
        }
        _canvas.Image = null; // new window → new picture
        return true;
    }

    // ---- Picture --------------------------------------------------------------------

    private void TakePicture()
    {
        if (_table == IntPtr.Zero || !Win32.IsWindow(_table))
        {
            if (_editing) FindOpenTable();
            if (_table == IntPtr.Zero)
            {
                SetFeedback(false, _editing
                    ? "No table from this site is open. Open one, then click \"Retake picture\"."
                    : "The table window was closed. Go back to step 2 and pick a table.");
                return;
            }
        }
        if (Win32.IsIconic(_table)) Win32.ShowWindow(_table, Win32.SW_RESTORE);
        var bmp = TableCapture.CaptureClient(_table, Profile.Capture);
        if (bmp == null) { SetFeedback(false, "Couldn't take a picture of the table."); return; }
        _canvas.Image = bmp;
        _tableTitle = Win32.GetTitle(_table);
        // Re-check what this step marks on the new picture.
        if (_step == Step.Pot && Profile.PotRegion != null) _ = ReadRegionAsync(Profile.PotRegion, true);
        if (_step == Step.Call && Profile.CallRegion != null) _ = ReadRegionAsync(Profile.CallRegion, false);
    }

    private IEnumerable<CanvasMarker> Markers()
    {
        // Only show what the current step is about, so the picture isn't cluttered.
        switch (_step)
        {
            case Step.Pot when Profile.PotRegion != null: yield return new("Pot", Color.Gold, Profile.PotRegion, null); break;
            case Step.Call when Profile.CallRegion != null: yield return new("Call", Color.OrangeRed, Profile.CallRegion, null); break;
            case Step.BetBox when Profile.BetBoxPoint != null: yield return new("Bet box", Color.Cyan, null, Profile.BetBoxPoint); break;
            case Step.Board or Step.EmptyBoard when Profile.BoardRegion != null: yield return new("First flop card", Color.LimeGreen, Profile.BoardRegion, null); break;
        }
    }

    // ---- Marking --------------------------------------------------------------------

    private void OnRectDrawn(RelRect r)
    {
        switch (_step)
        {
            case Step.Pot:
                Profile.PotRegion = r;
                _ = ReadRegionAsync(r, true);
                break;
            case Step.Call:
                Profile.CallRegion = r;
                _ = ReadRegionAsync(r, false);
                break;
            case Step.Board:
                Profile.BoardRegion = r;
                Profile.EmptyBoardSignature = null;
                SetFeedback(true, "Flop card area marked. Press Next.");
                break;
        }
        _canvas.Invalidate();
        UpdateButtons();
    }

    private void OnPointPicked(RelPoint p)
    {
        if (_step != Step.BetBox) return;
        Profile.BetBoxPoint = p;
        SetFeedback(true, "Bet box marked. Press Next.");
        _canvas.Invalidate();
        UpdateButtons();
    }

    /// <summary>Reads the marked area right away so the user sees whether it worked.</summary>
    private async Task ReadRegionAsync(RelRect region, bool isPot)
    {
        if (_canvas.Image == null) return;
        int token = ++_ocrToken;
        _feedback.ForeColor = SystemColors.GrayText;
        _feedback.Text = "Reading…";
        string text;
        try
        {
            using var crop = TableCapture.Crop(_canvas.Image, region);
            text = await OcrService.ReadTextAsync(crop);
        }
        catch (Exception ex) { if (token == _ocrToken) SetFeedback(false, ex.Message); return; }
        if (token != _ocrToken) return; // user moved on

        var value = AmountParser.ParseFirst(text, Profile.DecimalSeparator);
        // "Pot 12.5 BB" → the site shows everything in big blinds.
        if (value != null && Regex.IsMatch(text, @"\bbbs?\b", RegexOptions.IgnoreCase)) Profile.AmountsInBigBlinds = true;
        if (!isPot && text.Contains("check", StringComparison.OrdinalIgnoreCase))
            SetFeedback(false, $"I read \"{text}\" - that's the Check button. Wait until you're facing a bet, click \"Retake picture\", and draw the box again.");
        else if (value != null)
            SetFeedback(true, $"I read \"{text}\" → {value:0.##}. If that's right, press Next.");
        else
            SetFeedback(false, string.IsNullOrWhiteSpace(text)
                ? "I couldn't read any text there. Try a slightly bigger box around the number."
                : $"I read \"{text}\" but found no number. Try a box around just the amount.");
    }

    // ---- Empty board ----------------------------------------------------------------

    private void SaveEmptyBoardNow()
    {
        if (Profile.BoardRegion == null) { Go(Step.Board); return; }
        TakePicture();
        if (_canvas.Image == null) return;
        using var crop = TableCapture.Crop(_canvas.Image, Profile.BoardRegion);
        Profile.EmptyBoardSignature = TableCapture.Signature(crop);
        SetFeedback(true, "Saved. When flop cards appear, the postflop buttons will show. Press Next.");
        UpdateButtons();
    }

    // ---- Finish ---------------------------------------------------------------------

    private async Task BuildSummaryAsync()
    {
        var sb = new StringBuilder();
        string Mark(bool ok) => ok ? "✔" : "✖";
        sb.AppendLine($"{Mark(true)} Recognises tables from: {Profile.ProcessName}");
        int matching = 0;
        Win32.EnumWindows((h, _) =>
        {
            if (Win32.IsWindowVisible(h) && TableManager.Matches(Profile, _manager.GetProcessName(h), Win32.GetClass(h), Win32.GetTitle(h), Win32.GetWindowBounds(h), Win32.IsIconic(h))) matching++;
            return true;
        }, IntPtr.Zero);
        sb.AppendLine($"   Tables open right now that match: {matching}");
        var bb = AmountParser.ParseBigBlind(_tableTitle, Profile.BlindsRegex, Profile.DecimalSeparator);
        if (Profile.AmountsInBigBlinds)
            sb.AppendLine("✔ Amounts are in big blinds: a 2.5bb button types \"2.5\"");
        else sb.AppendLine($"{Mark(bb != null)} Big blind from window title: {(bb?.ToString("0.##") ?? "not found - 'bb' buttons won't work (percent-of-pot buttons still do)")}");
        if (Profile.BetBoxPoint == null)
        {
            sb.AppendLine("–  Bet-size buttons: off for this site (skipped). Table tiling and the RNG still work.");
            sb.AppendLine("   To add them later: Poker sites tab → select this site → Redo setup.");
            _summary.Text = sb.ToString().Replace("\n", "\r\n").Replace("\r\r", "\r");
            return;
        }
        sb.AppendLine($"{Mark(Profile.PotRegion != null)} Pot{(Profile.PotRegion == null ? " (skipped - '% pot' buttons won't work)" : "")}");
        sb.AppendLine($"{Mark(Profile.CallRegion != null)} Call button{(Profile.CallRegion == null ? " (skipped)" : "")}");
        sb.AppendLine($"{Mark(true)} Bet box");
        sb.AppendLine($"{Mark(!string.IsNullOrEmpty(Profile.EmptyBoardSignature))} Preflop/postflop detection{(string.IsNullOrEmpty(Profile.EmptyBoardSignature) ? " (skipped - right-click the bet buttons to switch preflop/postflop)" : "")}");
        _summary.Text = sb.ToString().Replace("\n", "\r\n").Replace("\r\r", "\r");

        if (_canvas.Image == null || Profile.PotRegion == null) return;
        try
        {
            var reading = await TableReader.ReadAsync(_canvas.Image, _tableTitle, Profile);
            var ctx = reading.ToContext(Profile);
            var list = reading.Postflop == true ? ConfigStore.Current.Bet.Postflop : ConfigStore.Current.Bet.Preflop;
            sb.AppendLine();
            sb.AppendLine($"On the last picture: pot {reading.Pot:0.##}, to call {reading.ToCall:0.##}, {(reading.Postflop == true ? "postflop" : "preflop")}.");
            sb.AppendLine("Your buttons would type: " + string.Join("   ", list.Select(b =>
                $"{b.DisplayText} → {(BetCalculator.RaiseTo(b, ctx, ConfigStore.Current.Bet.Rounding) is double v ? BetCalculator.Format(v, Profile.DecimalSeparator) : "?")}")));
            if (_step == Step.Done) _summary.Text = sb.ToString().Replace("\n", "\r\n").Replace("\r\r", "\r");
        }
        catch { /* summary is best-effort */ }
    }

    private void Finish()
    {
        Profile.Name = string.IsNullOrWhiteSpace(_name.Text) ? Profile.Name : _name.Text.Trim();
        Profile.Enabled = true;
        DialogResult = DialogResult.OK;
        Close();
    }
}
