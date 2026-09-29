using System.Diagnostics;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;

namespace DataDonkTM.Core;

/// <summary>
/// The ONLY code in this app that uses the network. It talks to GitHub and nothing else:
///  - CheckAsync asks GitHub for the latest release's version number (no data about you is sent).
///  - DownloadAndInstallAsync (only when you click Update) downloads DataDonkTM.exe from that
///    release, swaps it in after this app closes, and starts the new version.
/// Automatic checks can be turned off in the "About" tab.
/// </summary>
public static class UpdateChecker
{
    public const string Owner = "weston";
    public const string Repo = "datadonk-table-manager";
    public const string AssetName = "DataDonkTM.exe";

    public static string ReleasesPage => $"https://github.com/{Owner}/{Repo}/releases/latest";
    public static string GuidePage => $"https://github.com/{Owner}/{Repo}/blob/main/INSTALL.md";

    public static Version CurrentVersion
    {
        get
        {
            var v = Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0, 0);
            return new Version(v.Major, v.Minor, Math.Max(0, v.Build));
        }
    }

    /// <summary>Git commit this exe was built from (the build embeds it in the version info), or null.</summary>
    public static string? SourceCommit
    {
        get
        {
            var info = Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
            int plus = info?.IndexOf('+') ?? -1;
            return plus >= 0 ? info![(plus + 1)..] : null;
        }
    }

    public static string SourcePage => SourceCommit is string c
        ? $"https://github.com/{Owner}/{Repo}/tree/{c}"
        : $"https://github.com/{Owner}/{Repo}";

    public sealed record Release(Version Version, string PageUrl, string? DownloadUrl, long Size);

    private static HttpClient NewClient(TimeSpan timeout)
    {
        var http = new HttpClient { Timeout = timeout };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("DataDonkTM-update-check");
        return http;
    }

    /// <summary>Latest published release, or null if it can't be reached.</summary>
    public static async Task<Release?> CheckAsync()
    {
        try
        {
            using var http = NewClient(TimeSpan.FromSeconds(15));
            var json = await http.GetStringAsync($"https://api.github.com/repos/{Owner}/{Repo}/releases/latest");
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var tag = root.GetProperty("tag_name").GetString() ?? "";
            if (!Version.TryParse(tag.TrimStart('v', 'V'), out var version)) return null;
            string? url = null;
            long size = 0;
            foreach (var a in root.GetProperty("assets").EnumerateArray())
                if (string.Equals(a.GetProperty("name").GetString(), AssetName, StringComparison.OrdinalIgnoreCase))
                {
                    url = a.GetProperty("browser_download_url").GetString();
                    size = a.GetProperty("size").GetInt64();
                }
            return new Release(version, root.GetProperty("html_url").GetString() ?? ReleasesPage, url, size);
        }
        catch { return null; }
    }

    public static bool IsNewer(Release r) => r.Version > CurrentVersion;

    /// <summary>
    /// Downloads the new exe next to the current one, then starts a tiny script that waits for this
    /// app to close, replaces the exe and starts the new version. Settings are untouched.
    /// Returns false (with a reason) if the update can't be done automatically.
    /// </summary>
    public static async Task<(bool Ok, string Error)> DownloadAndInstallAsync(Release r, IProgress<double> progress, CancellationToken ct)
    {
        var exe = Environment.ProcessPath;
        if (r.DownloadUrl == null) return (false, "This release has no DataDonkTM.exe to download.");
        if (exe == null || !exe.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) return (false, "Can't find this app's own file.");
        var newFile = exe + ".new";
        try
        {
            using var http = NewClient(TimeSpan.FromMinutes(30));
            using var resp = await http.GetAsync(r.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, ct);
            resp.EnsureSuccessStatusCode();
            long total = resp.Content.Headers.ContentLength ?? r.Size;
            await using (var src = await resp.Content.ReadAsStreamAsync(ct))
            await using (var dst = File.Create(newFile))
            {
                var buffer = new byte[81920];
                long done = 0;
                int n;
                while ((n = await src.ReadAsync(buffer, ct)) > 0)
                {
                    await dst.WriteAsync(buffer.AsMemory(0, n), ct);
                    done += n;
                    if (total > 0) progress.Report((double)done / total);
                }
            }
            if (total > 0 && new FileInfo(newFile).Length != total)
                throw new IOException("The download was incomplete.");
        }
        catch (Exception ex)
        {
            try { File.Delete(newFile); } catch { }
            return (false, ex is UnauthorizedAccessException
                ? "Windows didn't allow saving next to the app. Move DataDonkTM.exe to a normal folder (e.g. Documents) and try again."
                : ex.Message);
        }

        // Swap the files once this process has exited, then restart.
        var script = Path.Combine(Path.GetTempPath(), "DataDonkTM-update.cmd");
        int pid = Environment.ProcessId;
        File.WriteAllText(script,
            "@echo off\r\n" +
            ":wait\r\n" +
            $"tasklist /FI \"PID eq {pid}\" 2>nul | find \" {pid} \" >nul &&(timeout /t 1 /nobreak >nul & goto wait)\r\n" +
            $"move /y \"{newFile}\" \"{exe}\" >nul\r\n" +
            $"start \"\" \"{exe}\"\r\n" +
            "del \"%~f0\"\r\n");
        Process.Start(new ProcessStartInfo("cmd.exe", $"/c \"{script}\"") { CreateNoWindow = true, UseShellExecute = false });
        return (true, "");
    }

    public static void OpenInBrowser(string url) =>
        Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
}
