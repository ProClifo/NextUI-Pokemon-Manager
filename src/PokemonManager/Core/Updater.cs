using System.Diagnostics;
using System.IO.Compression;
using System.Text.Json;

namespace PokemonManager.Core;

/// <summary>
/// Updates the pak from the project's GitHub releases: the newest release (pre-releases included) is offered when
/// its tag differs from the one this pak was built for (res/release.txt, written by the release build), and its
/// SD-card zip for this device's platform replaces the pak's files. Downloads use .NET's HTTP client, or the
/// device's curl or wget when .NET can't make HTTPS connections there.
/// </summary>
public sealed class Updater(string pakDir, string tempDir)
{
    public const string Repository = "ProClifo/NextUI-Pokemon-Manager";
    private const string UserAgent = "PokemonManager-NextUI";

    public sealed record Release(string Tag, string Name, string? AssetUrl, long AssetSize);

    /// <summary>The release this pak came from ("dev" for a local build).</summary>
    /// <summary>
    /// False when the device's Wi-Fi is known to be off or not connected (wlan0 down), so the check on startup
    /// doesn't wait on a network that isn't there; true when connected or when it can't tell.
    /// </summary>
    public static bool MaybeOnline()
    {
        try
        {
            var state = "/sys/class/net/wlan0/operstate";
            return !File.Exists(state) || File.ReadAllText(state).Trim() is "up" or "unknown";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return true;
        }
    }

    public string InstalledTag
    {
        get
        {
            try
            {
                var file = Path.Combine(pakDir, "res", "release.txt");
                return File.Exists(file) ? File.ReadAllText(file).Trim() : "dev";
            }
            catch (IOException)
            {
                return "dev";
            }
        }
    }

    /// <summary>
    /// The newest published release with a zip for <paramref name="platform"/>: a stable one, or with
    /// <paramref name="beta"/> (settings.txt's hidden beta_updates) a pre-release ("v0.2.53-beta.1") too. A
    /// <paramref name="timeout"/> bounds each attempt to reach GitHub (the check on startup), otherwise they get minutes.
    /// </summary>
    public Release Latest(string platform, bool beta = false, TimeSpan? timeout = null)
        => Pick(DownloadText($"https://api.github.com/repos/{Repository}/releases?per_page=100", timeout), platform, beta);

    /// <summary>The newest release in GitHub's release list (JSON) that <see cref="Latest"/> would offer.</summary>
    public static Release Pick(string json, string platform, bool beta)
    {
        using var doc = JsonDocument.Parse(json);
        // GitHub lists releases by tag name as text (alpha.9 before alpha.14): newest published first instead
        var releases = doc.RootElement.EnumerateArray()
            .OrderByDescending(r => r.TryGetProperty("published_at", out var p) && p.ValueKind == JsonValueKind.String ? p.GetDateTimeOffset() : DateTimeOffset.MinValue);
        foreach (var r in releases)
        {
            if (r.TryGetProperty("draft", out var draft) && draft.GetBoolean())
                continue;
            if (!beta && r.TryGetProperty("prerelease", out var pre) && pre.GetBoolean())
                continue;
            string tag = r.GetProperty("tag_name").GetString() ?? "";
            string name = r.TryGetProperty("name", out var n) && n.GetString() is { Length: > 0 } s ? s : tag;
            string? url = null;
            long size = 0;
            foreach (var a in r.GetProperty("assets").EnumerateArray())
            {
                if (a.GetProperty("name").GetString() == $"PokemonManager-{platform}-sdcard.zip")
                {
                    url = a.GetProperty("browser_download_url").GetString();
                    size = a.GetProperty("size").GetInt64();
                }
            }
            // a release still being built (or one without this device's zip) isn't offered
            if (url is not null)
                return new Release(tag, name, url, size);
        }
        throw new InvalidDataException($"There are no releases for this device ({platform}) yet.");
    }

    /// <summary>
    /// Whether <paramref name="tag"/> is a newer version than <paramref name="installed"/>, by semantic version
    /// ("v0.2.53-beta.1" is newer than v0.2.52 and older than v0.2.53), so turning betas off never offers an older
    /// stable release. A tag that isn't a version ("dev") counts as older than any release.
    /// </summary>
    public static bool IsNewer(string tag, string installed)
    {
        if (Parse(tag) is not { } a)
            return false;
        if (Parse(installed) is not { } b)
            return true;
        for (int i = 0; i < 3; i++)
        {
            if (a.Numbers[i] != b.Numbers[i])
                return a.Numbers[i] > b.Numbers[i];
        }
        // same version: a release is newer than its pre-releases, pre-releases compare part by part
        if (a.Pre.Length == 0 || b.Pre.Length == 0)
            return a.Pre.Length == 0 && b.Pre.Length != 0;
        for (int i = 0; i < Math.Min(a.Pre.Length, b.Pre.Length); i++)
        {
            bool an = int.TryParse(a.Pre[i], out var x), bn = int.TryParse(b.Pre[i], out var y);
            int c = an && bn ? x.CompareTo(y) : an ? -1 : bn ? 1 : string.CompareOrdinal(a.Pre[i], b.Pre[i]);
            if (c != 0)
                return c > 0;
        }
        return a.Pre.Length > b.Pre.Length;

        static (int[] Numbers, string[] Pre)? Parse(string version)
        {
            var v = version.Trim().TrimStart('v', 'V');
            int dash = v.IndexOf('-');
            var core = (dash < 0 ? v : v[..dash]).Split('.');
            var numbers = new int[3];
            if (core.Length is < 1 or > 3)
                return null;
            for (int i = 0; i < core.Length; i++)
            {
                if (!int.TryParse(core[i], out numbers[i]))
                    return null;
            }
            return (numbers, dash < 0 ? [] : v[(dash + 1)..].Split('.'));
        }
    }

    /// <summary>
    /// Downloads the release's zip and puts its pak files (Tools/&lt;platform&gt;/Pokemon Manager.pak/...) over this
    /// pak's. A failed download or unpacking changes nothing; files are then renamed over the old ones, so the
    /// running program's file can be replaced. Returns how many files were installed.
    /// </summary>
    public int Install(Release release, string platform)
    {
        if (release.AssetUrl is null)
            throw new InvalidDataException($"{release.Tag} has no download for this device ({platform}).");
        Directory.CreateDirectory(tempDir);
        var zipPath = Path.Combine(tempDir, "update.zip");
        var staging = Path.Combine(tempDir, "staging");
        try
        {
            Download(release.AssetUrl, zipPath);
            using var zip = ZipFile.OpenRead(zipPath);
            string prefix = $"Tools/{platform}/Pokemon Manager.pak/";
            var entries = zip.Entries.Where(e => e.FullName.StartsWith(prefix, StringComparison.Ordinal) && e.Name.Length > 0).ToList();
            if (entries.Count == 0)
                throw new InvalidDataException("The download doesn't contain the pak.");
            // Everything is unpacked first (a full SD card or a bad zip stops here, changing nothing), then moved in.
            var root = Path.GetFullPath(pakDir);
            if (Directory.Exists(staging))
                Directory.Delete(staging, true);
            var moves = new List<(string From, string To)>();
            foreach (var entry in entries)
            {
                var relative = entry.FullName[prefix.Length..];
                var target = Path.GetFullPath(Path.Combine(root, relative));
                if (!target.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal))
                    continue; // nothing outside the pak
                var staged = Path.GetFullPath(Path.Combine(staging, relative));
                Directory.CreateDirectory(Path.GetDirectoryName(staged)!);
                entry.ExtractToFile(staged, overwrite: true);
                moves.Add((staged, target));
            }
            foreach (var (from, to) in moves)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(to)!);
                // renamed over the old file (the staging folder is on the same card), so the running program's
                // file can be replaced
                File.Move(from, to, overwrite: true);
            }
            // Releases record their tag, but older ones didn't: noted here so the same update isn't offered again.
            File.WriteAllText(Path.Combine(root, "res", "release.txt"), release.Tag + "\n");
            return moves.Count;
        }
        finally
        {
            try
            {
                File.Delete(zipPath);
                if (Directory.Exists(staging))
                    Directory.Delete(staging, true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }
        }
    }

    private static string DownloadText(string url, TimeSpan? timeout)
    {
        try
        {
            using var http = Client(timeout);
            return http.GetStringAsync(url).GetAwaiter().GetResult();
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or PlatformNotSupportedException or TypeInitializationException or DllNotFoundException)
        {
            Console.Error.WriteLine($"update: .NET couldn't reach GitHub ({ex.Message}); trying curl/wget");
            var file = Path.GetTempFileName();
            try
            {
                RunDownloader(url, file, timeout);
                return File.ReadAllText(file);
            }
            finally
            {
                File.Delete(file);
            }
        }
    }

    private static void Download(string url, string path)
    {
        try
        {
            using var http = Client();
            using var response = http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead).GetAwaiter().GetResult();
            response.EnsureSuccessStatusCode();
            using var input = response.Content.ReadAsStream();
            using var output = File.Create(path);
            input.CopyTo(output);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or PlatformNotSupportedException or TypeInitializationException or DllNotFoundException)
        {
            Console.Error.WriteLine($"update: .NET couldn't download ({ex.Message}); trying curl/wget");
            RunDownloader(url, path);
        }
    }

    private static HttpClient Client(TimeSpan? timeout = null)
    {
        var http = new HttpClient { Timeout = timeout ?? TimeSpan.FromMinutes(10) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent);
        return http;
    }

    /// <summary>curl, else wget (busybox's included), saving <paramref name="url"/> to <paramref name="path"/>.</summary>
    private static void RunDownloader(string url, string path, TimeSpan? timeout = null)
    {
        string seconds = timeout is { } t ? ((int)Math.Ceiling(t.TotalSeconds)).ToString() : "";
        (string Tool, string[] Args)[] tools =
        [
            ("curl", timeout is null ? ["-fsSL", "-A", UserAgent, "-o", path, url] : ["-fsSL", "--max-time", seconds, "-A", UserAgent, "-o", path, url]),
            ("wget", timeout is null ? ["-q", "-U", UserAgent, "-O", path, url] : ["-q", "-T", seconds, "-U", UserAgent, "-O", path, url]),
        ];
        foreach (var (tool, args) in tools)
        {
            try
            {
                var psi = new ProcessStartInfo(tool) { UseShellExecute = false, RedirectStandardError = true, RedirectStandardOutput = true };
                foreach (var a in args)
                    psi.ArgumentList.Add(a);
                using var p = Process.Start(psi);
                if (p is null)
                    continue;
                var error = p.StandardError.ReadToEnd();
                p.WaitForExit();
                if (p.ExitCode == 0 && File.Exists(path) && new FileInfo(path).Length > 0)
                    return;
                Console.Error.WriteLine($"update: {tool} failed ({p.ExitCode}): {error.Trim()}");
            }
            catch (System.ComponentModel.Win32Exception)
            {
                // not installed
            }
        }
        throw new HttpRequestException("Couldn't reach GitHub. Check that Wi-Fi is on and connected.");
    }
}
