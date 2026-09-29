using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;
using DataDonkTM.Betting;
using DataDonkTM.Core;

namespace DataDonkTM.UI;

/// <summary>Settings window + tray icon. Closing the window hides it to the tray.</summary>
internal sealed class MainForm : Form
{
    private readonly TableManager _manager;
    private bool _startHidden;
    private bool _exiting;
    private readonly NotifyIcon _tray;

    private readonly ListBox _profiles = new() { Width = 300, Height = 300, IntegralHeight = false, FormattingEnabled = true };
    private readonly CheckBox _tilingEnabled;
    private readonly CheckBox _autoPlace = Ui.Chk("Automatically place new tables into free slots", true);
    private readonly CheckBox _snapOnDrag = Ui.Chk("Snap tables to a slot when dragged (drop on a taken slot = swap)", true);
    private readonly CheckBox _preferPlaying = Ui.Chk("New tables fill playing slots first (else observing first)", true);
    private readonly Label _profileInfo = Ui.Lbl("");

    private readonly CheckedListBox _sites = new() { Width = 300, Height = 300, IntegralHeight = false, CheckOnClick = true, FormattingEnabled = true };
    private bool _fillingSites;
    private readonly List<Button> _siteButtons = new();

    private static AppConfig Cfg => ConfigStore.Current;

    public MainForm(TableManager manager, bool startHidden)
    {
        _manager = manager;
        _startHidden = startHidden;
        Text = "DataDonk Table Manager";
        Icon = Ui.AppIcon;
        ClientSize = new Size(900, 600);
        MinimumSize = new Size(760, 520);
        StartPosition = FormStartPosition.CenterScreen;

        _tilingEnabled = Ui.Chk("Tiling enabled", Cfg.TilingEnabled);

        var tabs = new TabControl { Dock = DockStyle.Fill };
        tabs.TabPages.Add(BuildTilingTab());
        tabs.TabPages.Add(BuildSitesTab());
        tabs.TabPages.Add(BuildBetTab());
        tabs.TabPages.Add(BuildRngTab());
        tabs.TabPages.Add(BuildAboutTab());
        Controls.Add(tabs);
        Controls.Add(BuildUpdateBanner()); // added last so it docks above the tabs

        _tray = new NotifyIcon { Icon = Ui.TrayIcon, Text = "DataDonk Table Manager", Visible = true, ContextMenuStrip = BuildTrayMenu() };
        _tray.DoubleClick += (_, _) => ShowSettings();
        _tray.BalloonTipClicked += (_, _) => { if (_update != null) ShowSettings(); };

        RefreshProfiles();
        RefreshSites();

        // First update check shortly after start, then twice a day.
        _updateTimer.Tick += async (_, _) => { _updateTimer.Interval = 12 * 60 * 60 * 1000; await CheckForUpdatesAsync(manual: false); };
        _updateTimer.Start();
    }

    // ---- Updates --------------------------------------------------------------------

    private UpdateChecker.Release? _update;
    private readonly System.Windows.Forms.Timer _updateTimer = new() { Interval = 5000 };
    private readonly Panel _updateBanner = new() { Dock = DockStyle.Top, Height = 44, Visible = false, BackColor = Color.FromArgb(255, 214, 102), Padding = new Padding(10, 6, 10, 6) };
    private readonly Label _updateText = new() { AutoSize = true, Font = new Font("Segoe UI", 10.5f, FontStyle.Bold), Margin = new Padding(3, 8, 12, 3) };
    private readonly Label _versionInfo = new() { AutoSize = true, Margin = new Padding(3, 7, 3, 3) };

    private Control BuildUpdateBanner()
    {
        var update = Ui.Btn("Update now", (_, _) => UpdateNow(), 110);
        var whatsNew = new LinkLabel { Text = "What's new", AutoSize = true, Margin = new Padding(12, 9, 3, 3) };
        whatsNew.LinkClicked += (_, _) => { if (_update != null) UpdateChecker.OpenInBrowser(_update.PageUrl); };
        _updateBanner.Controls.Add(Ui.Row(_updateText, update, whatsNew));
        return _updateBanner;
    }

    private async Task CheckForUpdatesAsync(bool manual)
    {
        if (!manual && !Cfg.CheckForUpdates) return;
        var release = await UpdateChecker.CheckAsync();
        if (release == null)
        {
            if (manual) MessageBox.Show(this, "Couldn't reach GitHub to check for updates. Check your internet connection and try again.", "Updates");
            return;
        }
        if (!UpdateChecker.IsNewer(release))
        {
            if (manual) MessageBox.Show(this, $"You have the latest version (v{UpdateChecker.CurrentVersion}).", "Updates");
            return;
        }
        bool firstTime = _update == null || _update.Version != release.Version;
        _update = release;
        _updateText.Text = $"⬆  A new version is available: v{release.Version}  (you have v{UpdateChecker.CurrentVersion})";
        _updateBanner.Visible = true;
        _tray.Text = $"DataDonk Table Manager - update available (v{release.Version})";
        if (firstTime && !manual)
            _tray.ShowBalloonTip(5000, "Update available", $"Version {release.Version} of DataDonk Table Manager is ready. Click here to update.", ToolTipIcon.Info);
        if (manual) UpdateNow();
    }

    private void UpdateNow()
    {
        if (_update == null) return;
        ShowSettings();
        if (MessageBox.Show(this, $"Update to version {_update.Version} now?\n\nThe app will download the new version, close, and start again by itself. Your settings are kept.",
                "Update", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return;

        using var cts = new CancellationTokenSource();
        using var dlg = new Form
        {
            Text = "Updating", FormBorderStyle = FormBorderStyle.FixedDialog, StartPosition = FormStartPosition.CenterParent,
            ClientSize = new Size(420, 110), MinimizeBox = false, MaximizeBox = false, ShowInTaskbar = false, ControlBox = false,
        };
        var label = new Label { Text = "Downloading the new version…", Left = 16, Top = 14, AutoSize = true };
        var bar = new ProgressBar { Left = 16, Top = 40, Width = 388, Height = 20, Maximum = 1000 };
        var cancel = new Button { Text = "Cancel", Left = 314, Top = 70, Width = 90 };
        cancel.Click += (_, _) => cts.Cancel();
        dlg.Controls.AddRange(new Control[] { label, bar, cancel });
        dlg.Shown += async (_, _) =>
        {
            var progress = new Progress<double>(p => bar.Value = (int)Math.Clamp(p * 1000, 0, 1000));
            var (ok, error) = await UpdateChecker.DownloadAndInstallAsync(_update, progress, cts.Token);
            dlg.Tag = ok ? null : error;
            dlg.DialogResult = ok ? DialogResult.OK : DialogResult.Abort;
        };
        var result = dlg.ShowDialog(this);
        if (result == DialogResult.OK) { ExitApp(); return; }

        if (cts.IsCancellationRequested) return;
        if (MessageBox.Show(this, $"The update didn't work: {dlg.Tag}\n\nOpen the download page to update by hand?", "Update",
                MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes)
            UpdateChecker.OpenInBrowser(_update.PageUrl);
    }

    // ---- Window / tray behaviour ----------------------------------------------------

    protected override void SetVisibleCore(bool value)
    {
        if (_startHidden)
        {
            _startHidden = false;
            if (!IsHandleCreated) CreateHandle();
            value = false;
        }
        base.SetVisibleCore(value);
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (!_exiting && e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            Hide();
            _tray.ShowBalloonTip(2000, "Still running", "DataDonk Table Manager keeps running in the tray. Right-click the icon to exit.", ToolTipIcon.Info);
            return;
        }
        _tray.Visible = false;
        base.OnFormClosing(e);
    }

    private void ShowSettings()
    {
        Show();
        if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
        Activate();
    }

    private void ExitApp()
    {
        _exiting = true;
        Close();
        Application.Exit();
    }

    private ContextMenuStrip BuildTrayMenu()
    {
        var menu = new ContextMenuStrip();
        menu.Opening += (_, _) =>
        {
            menu.Items.Clear();
            if (_update != null)
            {
                menu.Items.Add(new ToolStripMenuItem($"⬆ Update to v{_update.Version}", null, (_, _) => UpdateNow())
                    { Font = new Font(menu.Font, FontStyle.Bold) });
                menu.Items.Add(new ToolStripSeparator());
            }
            menu.Items.Add("Settings", null, (_, _) => ShowSettings());
            var profiles = new ToolStripMenuItem("Activate profile");
            foreach (var p in Cfg.Profiles)
            {
                var id = p.Id;
                profiles.DropDownItems.Add(new ToolStripMenuItem(p.Name + (p.Id == Cfg.DefaultProfileId ? "  (default)" : ""), null,
                    (_, _) => { _manager.ActivateProfile(id); RefreshProfiles(); }) { Checked = p.Id == Cfg.ActiveProfileId });
            }
            menu.Items.Add(profiles);
            menu.Items.Add(new ToolStripMenuItem("Tiling enabled", null, (_, _) => _tilingEnabled.Checked = !_tilingEnabled.Checked) { Checked = Cfg.TilingEnabled });
            menu.Items.Add("Re-tile open tables", null, (_, _) => _manager.RetileAll());
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Exit", null, (_, _) => ExitApp());
        };
        menu.Items.Add("…"); // Opening is only raised when there is at least one item
        return menu;
    }

    private void SaveAndRefresh()
    {
        ConfigStore.Save();
        _manager.RefreshOverlays();
    }

    // ---- Tiling tab -----------------------------------------------------------------

    private TabPage BuildTilingTab()
    {
        var page = new TabPage("Table tiling") { Padding = new Padding(10) };
        _profiles.Format += (_, e) =>
        {
            var p = (TilingProfile)e.ListItem!;
            var tags = new List<string>();
            if (p.Id == Cfg.ActiveProfileId) tags.Add("active");
            if (p.Id == Cfg.DefaultProfileId) tags.Add("default");
            e.Value = p.Name + (tags.Count > 0 ? $"   [{string.Join(", ", tags)}]" : "");
        };
        _profiles.SelectedIndexChanged += (_, _) => ShowSelectedProfile();
        _profiles.DoubleClick += (_, _) => EditLayout();

        _tilingEnabled.CheckedChanged += (_, _) =>
        {
            Cfg.TilingEnabled = _tilingEnabled.Checked;
            ConfigStore.Save();
            if (Cfg.TilingEnabled) _manager.RetileAll();
        };
        _autoPlace.CheckedChanged += (_, _) => { if (Selected is { } p) { p.AutoPlaceNewTables = _autoPlace.Checked; ConfigStore.Save(); } };
        _snapOnDrag.CheckedChanged += (_, _) => { if (Selected is { } p) { p.SnapOnDrag = _snapOnDrag.Checked; ConfigStore.Save(); } };
        _preferPlaying.CheckedChanged += (_, _) => { if (Selected is { } p) { p.NewTablesPreferPlaying = _preferPlaying.Checked; ConfigStore.Save(); } };

        var buttons = Ui.Column(
            Ui.Btn("Edit layout", (_, _) => EditLayout()),
            Ui.Btn("New profile", (_, _) => NewProfile()),
            Ui.Btn("Rename", (_, _) => RenameProfile()),
            Ui.Btn("Duplicate", (_, _) => DuplicateProfile()),
            Ui.Btn("Delete", (_, _) => DeleteProfile()),
            Ui.Btn("Set as default", (_, _) => { if (Selected is { } p) { Cfg.DefaultProfileId = p.Id; ConfigStore.Save(); RefreshProfiles(); } }),
            Ui.Btn("Activate", (_, _) => { if (Selected is { } p) { _manager.ActivateProfile(p.Id); RefreshProfiles(); } }),
            Ui.Btn("Re-tile open tables", (_, _) => _manager.RetileAll()));

        var options = Ui.Column(
            new Label { Text = "Selected profile", Font = new Font(Font, FontStyle.Bold), AutoSize = true, Margin = new Padding(3, 12, 3, 3) },
            _profileInfo, _autoPlace, _snapOnDrag, _preferPlaying,
            Ui.Note("The default profile is activated when the app starts. Playing slots get the RNG and bet-button overlays; observing slots don't. " +
                    "Double-click a profile (or 'Edit layout') to open the full-screen layout editor across all monitors."));

        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 2 };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 310));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 170));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.Controls.Add(_tilingEnabled, 0, 0);
        layout.SetColumnSpan(_tilingEnabled, 3);
        layout.Controls.Add(_profiles, 0, 1);
        layout.Controls.Add(buttons, 1, 1);
        layout.Controls.Add(options, 2, 1);
        _profiles.Dock = DockStyle.Fill;
        page.Controls.Add(layout);
        return page;
    }

    private TilingProfile? Selected => _profiles.SelectedItem as TilingProfile;

    private void RefreshProfiles(TilingProfile? select = null)
    {
        select ??= Selected ?? Cfg.ActiveProfile;
        _profiles.BeginUpdate();
        _profiles.Items.Clear();
        foreach (var p in Cfg.Profiles) _profiles.Items.Add(p);
        _profiles.EndUpdate();
        if (select != null && Cfg.Profiles.Contains(select)) _profiles.SelectedItem = select;
        else if (_profiles.Items.Count > 0) _profiles.SelectedIndex = 0;
        ShowSelectedProfile();
    }

    private void ShowSelectedProfile()
    {
        var p = Selected;
        foreach (var c in new[] { _autoPlace, _snapOnDrag, _preferPlaying }) c.Enabled = p != null;
        if (p == null) { _profileInfo.Text = ""; return; }
        _autoPlace.Checked = p.AutoPlaceNewTables;
        _snapOnDrag.Checked = p.SnapOnDrag;
        _preferPlaying.Checked = p.NewTablesPreferPlaying;
        int playing = p.Slots.Count(s => s.Type == SlotType.Playing);
        _profileInfo.Text = $"{p.Name}: {p.Slots.Count} slots ({playing} playing, {p.Slots.Count - playing} observing)";
    }

    private void EditLayout()
    {
        var p = Selected;
        if (p == null) return;
        Hide();
        using var editor = new LayoutEditorForm(p, _manager);
        if (editor.ShowDialog() == DialogResult.OK)
        {
            p.Slots = editor.ResultSlots;
            ConfigStore.Save();
            if (p.Id == Cfg.ActiveProfileId) _manager.RetileAll();
        }
        ShowSettings();
        RefreshProfiles(p);
    }

    private void NewProfile()
    {
        var name = Ui.Prompt(this, "New profile", "Profile name:", $"Profile {Cfg.Profiles.Count + 1}");
        if (name == null) return;
        var p = new TilingProfile { Name = name };
        Cfg.Profiles.Add(p);
        ConfigStore.Save();
        RefreshProfiles(p);
        EditLayout();
    }

    private void RenameProfile()
    {
        if (Selected is not { } p) return;
        var name = Ui.Prompt(this, "Rename profile", "Profile name:", p.Name);
        if (name == null) return;
        p.Name = name;
        ConfigStore.Save();
        RefreshProfiles(p);
    }

    private void DuplicateProfile()
    {
        if (Selected is not { } p) return;
        var copy = p.Clone();
        copy.Id = Guid.NewGuid().ToString("N");
        copy.Name = p.Name + " (copy)";
        Cfg.Profiles.Add(copy);
        ConfigStore.Save();
        RefreshProfiles(copy);
    }

    private void DeleteProfile()
    {
        if (Selected is not { } p) return;
        if (Cfg.Profiles.Count == 1) { MessageBox.Show(this, "You need at least one profile."); return; }
        if (MessageBox.Show(this, $"Delete profile '{p.Name}'?", "Delete", MessageBoxButtons.YesNo) != DialogResult.Yes) return;
        Cfg.Profiles.Remove(p);
        if (Cfg.DefaultProfileId == p.Id) Cfg.DefaultProfileId = Cfg.Profiles[0].Id;
        if (Cfg.ActiveProfileId == p.Id) _manager.ActivateProfile(Cfg.DefaultProfileId!);
        ConfigStore.Save();
        RefreshProfiles(Cfg.ActiveProfile);
    }

    // ---- Sites tab ------------------------------------------------------------------

    private TabPage BuildSitesTab()
    {
        var page = new TabPage("Poker sites") { Padding = new Padding(10) };
        _sites.Format += (_, e) => e.Value = ((SiteProfile)e.ListItem!).Name;
        _sites.ItemCheck += (_, e) =>
        {
            if (_fillingSites) return; // Items.Add(item, checked) raises ItemCheck too
            ((SiteProfile)_sites.Items[e.Index]).Enabled = e.NewValue == CheckState.Checked;
            ConfigStore.Save();
            _manager.Scan();
        };
        _sites.DoubleClick += (_, _) => { if (SelectedSite is { } s) RunWizard(s); };

        var top = Ui.Column(
            Ui.Btn("Set up a poker site", (_, _) => RunWizard(null), 190),
            new Label { Text = "Your sites (ticked = on)", Font = new Font(Font, FontStyle.Bold), AutoSize = true, Margin = new Padding(3, 14, 3, 3) });

        // These act on the selected site, so they are greyed out when none is selected.
        _siteButtons.AddRange(new[]
        {
            Ui.Btn("Redo setup", (_, _) => { if (SelectedSite is { } s) RunWizard(s); }, 190),
            Ui.Btn("Delete", (_, _) => DeleteSite(), 190),
            Ui.Btn("Advanced settings", (_, _) => EditSite(), 190),
            Ui.Btn("Export (to share)", (_, _) => ExportSite(), 190),
        });
        _sites.SelectedIndexChanged += (_, _) => UpdateSiteButtons();
        var buttons = Ui.Column(
            _siteButtons[0], _siteButtons[1],
            new Label { Text = "More", AutoSize = true, ForeColor = SystemColors.GrayText, Margin = new Padding(3, 16, 3, 0) },
            _siteButtons[2], _siteButtons[3],
            Ui.Btn("Import a site file", (_, _) => ImportSite(), 190));

        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2 };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 330));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.Controls.Add(top, 0, 0);
        layout.SetColumnSpan(top, 2);
        _sites.Dock = DockStyle.Fill;
        layout.Controls.Add(_sites, 0, 1);
        layout.Controls.Add(buttons, 1, 1);
        page.Controls.Add(layout);
        return page;
    }

    /// <summary>Guided setup. With no site given, reuses an existing site for the same program if there is one.</summary>
    private void RunWizard(SiteProfile? existing)
    {
        using var wizard = new SiteWizardForm(existing?.Clone() ?? new SiteProfile { Name = "New site" }, _manager);
        if (wizard.ShowDialog(this) != DialogResult.OK) return;
        var result = wizard.Profile;
        existing ??= Cfg.Sites.FirstOrDefault(s => s.ProcessName.Equals(result.ProcessName, StringComparison.OrdinalIgnoreCase));
        int i = existing == null ? -1 : Cfg.Sites.IndexOf(existing);
        if (i >= 0)
        {
            result.Id = existing!.Id;
            result.RngPosition = existing.RngPosition;
            result.BetPanelPosition = existing.BetPanelPosition;
            Cfg.Sites[i] = result;
        }
        else Cfg.Sites.Add(result);
        ConfigStore.Save();
        _manager.Scan();
        _manager.RefreshOverlays();
        RefreshSites(result);
        MessageBox.Show(this, $"'{result.Name}' is set up.\n\nNext: in the 'Table tiling' tab, click 'Edit layout' to choose where tables go. " +
                              "Bet buttons and the RNG appear on tables in playing slots.", "Site ready");
    }

    private SiteProfile? SelectedSite => _sites.SelectedItem as SiteProfile;

    private void RefreshSites(SiteProfile? select = null)
    {
        select ??= SelectedSite;
        _fillingSites = true;
        _sites.BeginUpdate();
        _sites.Items.Clear();
        foreach (var s in Cfg.Sites) _sites.Items.Add(s, s.Enabled);
        _sites.EndUpdate();
        _fillingSites = false;
        if (select != null && Cfg.Sites.Contains(select)) _sites.SelectedItem = select;
        else if (_sites.Items.Count > 0) _sites.SelectedIndex = 0;
        UpdateSiteButtons();
    }

    private void UpdateSiteButtons()
    {
        foreach (var b in _siteButtons) b.Enabled = SelectedSite != null;
    }

    private void EditSite(SiteProfile? site = null)
    {
        site ??= SelectedSite;
        if (site == null) return;
        using var editor = new SiteEditorForm(site.Clone(), _manager);
        if (editor.ShowDialog(this) != DialogResult.OK) return;
        int i = Cfg.Sites.IndexOf(site);
        if (i >= 0) Cfg.Sites[i] = editor.Profile; else Cfg.Sites.Add(editor.Profile);
        ConfigStore.Save();
        _manager.Scan();
        _manager.RefreshOverlays();
        RefreshSites(editor.Profile);
    }

    private void DeleteSite()
    {
        if (SelectedSite is not { } s) return;
        if (MessageBox.Show(this, $"Delete site '{s.Name}'?", "Delete", MessageBoxButtons.YesNo) != DialogResult.Yes) return;
        Cfg.Sites.Remove(s);
        ConfigStore.Save();
        _manager.Scan();
        RefreshSites();
    }

    private void ImportSite()
    {
        using var dlg = new OpenFileDialog { Filter = "Site profile (*.json)|*.json|All files|*.*" };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            var s = JsonSerializer.Deserialize<SiteProfile>(File.ReadAllText(dlg.FileName), ConfigStore.JsonOptions)
                    ?? throw new InvalidDataException("Empty file.");
            s.Id = Guid.NewGuid().ToString("N");
            s.Enabled = false; // review before enabling
            Cfg.Sites.Add(s);
            ConfigStore.Save();
            RefreshSites(s);
            MessageBox.Show(this, $"Imported '{s.Name}' (disabled). Check it with Redo setup, then tick it to turn it on.", "Import");
        }
        catch (Exception ex) { MessageBox.Show(this, "Import failed: " + ex.Message); }
    }

    private void ExportSite()
    {
        if (SelectedSite is not { } s) return;
        using var dlg = new SaveFileDialog { Filter = "Site profile (*.json)|*.json", FileName = s.Name + ".json" };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        File.WriteAllText(dlg.FileName, JsonSerializer.Serialize(s, ConfigStore.JsonOptions));
    }

    // ---- Bet buttons tab ------------------------------------------------------------

    private TabPage BuildBetTab()
    {
        var page = new TabPage("Bet buttons") { Padding = new Padding(10), AutoScroll = true };
        var bet = Cfg.Bet;

        var enabled = Ui.Chk("Show bet-size buttons on tables in playing slots", bet.Enabled);
        enabled.CheckedChanged += (_, _) => { bet.Enabled = enabled.Checked; SaveAndRefresh(); };

        var width = Ui.Num(24, 200, bet.ButtonWidth);
        var height = Ui.Num(14, 80, bet.ButtonHeight);
        width.ValueChanged += (_, _) => { bet.ButtonWidth = (int)width.Value; SaveAndRefresh(); };
        height.ValueChanged += (_, _) => { bet.ButtonHeight = (int)height.Value; SaveAndRefresh(); };
        var rounding = Ui.Combo(bet.Rounding);
        rounding.SelectedIndexChanged += (_, _) => { bet.Rounding = (RoundingMode)rounding.SelectedItem!; ConfigStore.Save(); };

        var grids = new TableLayoutPanel { ColumnCount = 2, AutoSize = true, Margin = new Padding(0, 8, 0, 8) };
        grids.Controls.Add(BuildButtonEditor("Preflop buttons", bet.Preflop), 0, 0);
        grids.Controls.Add(BuildButtonEditor("Postflop buttons", bet.Postflop), 1, 0);

        page.Controls.Add(Ui.Column(
            enabled,
            grids,
            Ui.Row(Ui.Lbl("Button width"), width, Ui.Lbl("height"), height, Ui.Lbl("   Rounding"), rounding)));
        return page;
    }

    private Control BuildButtonEditor(string title, List<BetButton> list)
    {
        var binding = new BindingList<BetButton>(list) { AllowNew = true };
        var grid = new DataGridView
        {
            Width = 240, Height = 250, AutoGenerateColumns = false, AllowUserToAddRows = false, AllowUserToResizeRows = false,
            RowHeadersVisible = false, SelectionMode = DataGridViewSelectionMode.FullRowSelect, MultiSelect = false,
            EditMode = DataGridViewEditMode.EditOnEnter,
        };
        grid.Columns.Add(new DataGridViewComboBoxColumn
        {
            HeaderText = "Type", DataPropertyName = nameof(BetButton.Unit), Width = 110,
            DataSource = new[] { new { V = BetUnit.BigBlinds, N = "BB" }, new { V = BetUnit.PotPercent, N = "% pot" } },
            ValueMember = "V", DisplayMember = "N", ValueType = typeof(BetUnit),
        });
        grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Value", DataPropertyName = nameof(BetButton.Value), AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill });
        grid.DataSource = binding;
        grid.DataError += (_, e) => { e.ThrowException = false; };
        grid.CellValueChanged += (_, _) => SaveAndRefresh();
        grid.CurrentCellDirtyStateChanged += (_, _) => { if (grid.CurrentCell is DataGridViewComboBoxCell) grid.CommitEdit(DataGridViewDataErrorContexts.Commit); };

        void Move(int delta)
        {
            if (grid.CurrentRow == null) return;
            int i = grid.CurrentRow.Index, j = i + delta;
            if (j < 0 || j >= binding.Count) return;
            (binding[i], binding[j]) = (binding[j], binding[i]);
            binding.ResetBindings();
            grid.CurrentCell = grid.Rows[j].Cells[0];
            SaveAndRefresh();
        }

        var buttons = Ui.Row(
            Ui.Btn("Add", (_, _) =>
            {
                if (binding.Count >= BetSettings.MaxButtons) { MessageBox.Show(this, "Maximum is 8 buttons."); return; }
                binding.Add(new BetButton(BetUnit.PotPercent, 50));
                SaveAndRefresh();
            }, 70),
            Ui.Btn("Remove", (_, _) =>
            {
                if (grid.CurrentRow == null) return;
                binding.RemoveAt(grid.CurrentRow.Index);
                SaveAndRefresh();
            }, 70),
            Ui.Btn("▲", (_, _) => Move(-1), 40),
            Ui.Btn("▼", (_, _) => Move(1), 40));

        return Ui.Column(new Label { Text = title, Font = new Font(Font, FontStyle.Bold), AutoSize = true }, grid, buttons);
    }

    // ---- RNG tab --------------------------------------------------------------------

    private TabPage BuildRngTab()
    {
        var page = new TabPage("RNG") { Padding = new Padding(10) };
        var enabled = Ui.Chk("Show an RNG box on every table in a playing slot", Cfg.Rng.Enabled);
        enabled.CheckedChanged += (_, _) => { Cfg.Rng.Enabled = enabled.Checked; SaveAndRefresh(); };
        var size = Ui.Num(16, 120, Cfg.Rng.Size);
        size.ValueChanged += (_, _) => { Cfg.Rng.Size = (int)size.Value; SaveAndRefresh(); };
        page.Controls.Add(Ui.Column(
            enabled,
            Ui.Row(Ui.Lbl("Box height (px)"), size),
            Ui.Note("Left-click the box for a new number from 1 to 100 (uniform, from the OS cryptographic random generator). " +
                    "Right-click and drag to move it - the position is saved per site. Scroll the mouse wheel over it to resize, or right-click for a menu.")));
        return page;
    }

    // ---- About tab ------------------------------------------------------------------

    private TabPage BuildAboutTab()
    {
        var page = new TabPage("About") { Padding = new Padding(16) };

        LinkLabel Link(string text, string url)
        {
            var l = new LinkLabel { Text = text, UseMnemonic = false, AutoSize = true, Font = new Font("Segoe UI", 10.5f), Margin = new Padding(3, 4, 3, 4) };
            l.LinkClicked += (_, _) => UpdateChecker.OpenInBrowser(url);
            return l;
        }

        _versionInfo.Text = $"Version {UpdateChecker.CurrentVersion}";
        _versionInfo.Font = new Font("Segoe UI", 10.5f);
        var autoCheck = Ui.Chk("Check for updates automatically", Cfg.CheckForUpdates);
        autoCheck.CheckedChanged += (_, _) => { Cfg.CheckForUpdates = autoCheck.Checked; ConfigStore.Save(); };

        page.Controls.Add(Ui.Column(
            new Label { Text = "DataDonk Table Manager", Font = new Font("Segoe UI", 16, FontStyle.Bold), AutoSize = true, Margin = new Padding(3, 0, 3, 6) },
            _versionInfo,
            Link(UpdateChecker.SourceCommit is string c ? $"Built from commit {c[..Math.Min(7, c.Length)]}" : "Source code", UpdateChecker.SourcePage),
            Ui.Row(Ui.Btn("Check for updates", async (_, _) => await CheckForUpdatesAsync(manual: true), 140), autoCheck),
            new Label { AutoSize = true, Height = 8 },
            Link("datadonk.com", "https://datadonk.com"),
            Link("piotools.com", "https://piotools.com"),
            Link("Installation & update guide", UpdateChecker.GuidePage),
            new Label { AutoSize = true, Height = 8 },
            Ui.Btn("Open settings folder", (_, _) => Process.Start("explorer.exe", $"/select,\"{ConfigStore.ConfigPath}\""), 160)));
        return page;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _tray.Dispose();
        base.Dispose(disposing);
    }
}
