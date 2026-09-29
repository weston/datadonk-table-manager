using System.Text.Json;
using System.Text.Json.Serialization;

namespace DataDonkTM.Core;

/// <summary>
/// Loads/saves the single JSON config file. If a config.json sits next to the exe, that file is used
/// (portable mode); otherwise %APPDATA%\DataDonkTM\config.json.
/// </summary>
public static class ConfigStore
{
    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static string ConfigPath { get; } = ResolvePath();
    public static AppConfig Current { get; private set; } = new();

    /// <summary>Raised after any Save(); listeners re-read what they need.</summary>
    public static event Action? Saved;

    private static string ResolvePath()
    {
        var portable = Path.Combine(AppContext.BaseDirectory, "config.json");
        if (File.Exists(portable)) return portable;
        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DataDonkTM");
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, "config.json");
    }

    public static void Load()
    {
        AppConfig? cfg = null;
        if (File.Exists(ConfigPath))
        {
            try { cfg = JsonSerializer.Deserialize<AppConfig>(File.ReadAllText(ConfigPath), JsonOptions); }
            catch (Exception ex)
            {
                var backup = ConfigPath + ".broken-" + DateTime.Now.ToString("yyyyMMdd-HHmmss");
                File.Copy(ConfigPath, backup, true);
                MessageBox.Show($"Could not read config ({ex.Message}).\nA backup was saved to:\n{backup}\nStarting with defaults.",
                    "DataDonk Table Manager", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }
        Current = cfg ?? CreateDefault();
        Normalize(Current);
        // The default profile is the one activated at startup.
        if (Current.DefaultProfileId != null && Current.Profiles.Any(p => p.Id == Current.DefaultProfileId))
            Current.ActiveProfileId = Current.DefaultProfileId;
        Save();
    }

    public static void Save()
    {
        var tmp = ConfigPath + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(Current, JsonOptions));
        File.Move(tmp, ConfigPath, true);
        Saved?.Invoke();
    }

    private static void Normalize(AppConfig c)
    {
        c.Profiles ??= new();
        c.Sites ??= new();
        c.Bet ??= new();
        c.Rng ??= new();
        if (c.Profiles.Count == 0)
            c.Profiles.Add(new TilingProfile { Name = "Default" });
        if (c.DefaultProfileId == null || c.Profiles.All(p => p.Id != c.DefaultProfileId))
            c.DefaultProfileId = c.Profiles[0].Id;
        if (c.ActiveProfileId == null || c.Profiles.All(p => p.Id != c.ActiveProfileId))
            c.ActiveProfileId = c.DefaultProfileId;
        if (c.Bet.Preflop.Count > BetSettings.MaxButtons) c.Bet.Preflop.RemoveRange(BetSettings.MaxButtons, c.Bet.Preflop.Count - BetSettings.MaxButtons);
        if (c.Bet.Postflop.Count > BetSettings.MaxButtons) c.Bet.Postflop.RemoveRange(BetSettings.MaxButtons, c.Bet.Postflop.Count - BetSettings.MaxButtons);
    }

    private static AppConfig CreateDefault()
    {
        var c = new AppConfig();
        c.Sites.AddRange(SitePresets.All());
        return c;
    }
}
