namespace DataDonkTM.UI;

/// <summary>Small helpers to build forms in code (no designer files, easier to review).</summary>
internal static class Ui
{
    public static Button Btn(string text, EventHandler onClick, int width = 150)
    {
        var b = new Button { Text = text, Width = width, Height = 28, Margin = new Padding(3) };
        b.Click += onClick;
        return b;
    }

    public static Label Lbl(string text, bool autoSize = true) =>
        new() { Text = text, AutoSize = autoSize, Margin = new Padding(3, 7, 3, 3) };

    public static Label Note(string text, int width = 560) => new()
    {
        Text = text, AutoSize = true, MaximumSize = new Size(width, 0), ForeColor = SystemColors.GrayText, Margin = new Padding(3, 6, 3, 6),
    };

    public static NumericUpDown Num(decimal min, decimal max, decimal value, int decimals = 0, decimal increment = 1) => new()
    {
        Minimum = min, Maximum = max, DecimalPlaces = decimals, Increment = increment,
        Value = Math.Clamp(value, min, max), Width = 80,
    };

    public static CheckBox Chk(string text, bool value) => new() { Text = text, Checked = value, AutoSize = true, Margin = new Padding(3, 6, 3, 3) };

    public static ComboBox Combo<T>(T value) where T : struct, Enum
    {
        var c = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 160 };
        foreach (var v in Enum.GetValues<T>()) c.Items.Add(v);
        c.SelectedItem = value;
        return c;
    }

    public static FlowLayoutPanel Row(params Control[] controls)
    {
        var p = new FlowLayoutPanel { AutoSize = true, WrapContents = false, FlowDirection = FlowDirection.LeftToRight, Margin = new Padding(0) };
        p.Controls.AddRange(controls);
        return p;
    }

    public static FlowLayoutPanel Column(params Control[] controls)
    {
        var p = new FlowLayoutPanel { AutoSize = true, WrapContents = false, FlowDirection = FlowDirection.TopDown, Margin = new Padding(0) };
        p.Controls.AddRange(controls);
        return p;
    }

    /// <summary>Simple text input dialog. Returns null if cancelled.</summary>
    public static string? Prompt(IWin32Window? owner, string title, string label, string initial = "")
    {
        using var f = new Form
        {
            Text = title, FormBorderStyle = FormBorderStyle.FixedDialog, StartPosition = FormStartPosition.CenterParent,
            MinimizeBox = false, MaximizeBox = false, ClientSize = new Size(360, 110), ShowInTaskbar = false,
        };
        var lbl = new Label { Text = label, Left = 12, Top = 12, AutoSize = true };
        var box = new TextBox { Text = initial, Left = 12, Top = 36, Width = 336 };
        var ok = new Button { Text = "OK", DialogResult = DialogResult.OK, Left = 192, Top = 72, Width = 75 };
        var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Left = 273, Top = 72, Width = 75 };
        f.Controls.AddRange(new Control[] { lbl, box, ok, cancel });
        f.AcceptButton = ok; f.CancelButton = cancel;
        return f.ShowDialog(owner) == DialogResult.OK && !string.IsNullOrWhiteSpace(box.Text) ? box.Text.Trim() : null;
    }

    /// <summary>The DataDonk logo (app.ico, embedded), for window title bars.</summary>
    public static Icon AppIcon { get; } = LoadIcon(null);

    /// <summary>The logo at the system's small-icon size, for the tray.</summary>
    public static Icon TrayIcon { get; } = LoadIcon(SystemInformation.SmallIconSize);

    private static Icon LoadIcon(Size? size)
    {
        using var stream = typeof(Ui).Assembly.GetManifestResourceStream("DataDonkTM.app.ico")!;
        return size is Size s ? new Icon(stream, s) : new Icon(stream);
    }
}
