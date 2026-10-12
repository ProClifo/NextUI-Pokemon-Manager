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

    /// <summary>The newest published release with a zip for <paramref name="platform"/>.</summary>
    public Release Latest(string platform)
    {
        var json = DownloadText($"https://api.github.com/repos/{Repository}/releases?per_page=100");
        using var doc = JsonDocument.Parse(json);
        // GitHub lists releases by tag name as text (alpha.9 before alpha.14): newest published first instead
        var releases = doc.RootElement.EnumerateArray()
            .OrderByDescending(r => r.TryGetProperty("published_at", out var p) && p.ValueKind == JsonValueKind.String ? p.GetDateTimeOffset() : DateTimeOffset.MinValue);
        foreach (var r in releases)
        {
            if (r.TryGetProperty("draft", out var draft) && draft.GetBoolean())
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

    private static string DownloadText(string url)
    {
        try
        {
            using var http = Client();
            return http.GetStringAsync(url).GetAwaiter().GetResult();
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or PlatformNotSupportedException or TypeInitializationException or DllNotFoundException)
        {
            Console.Error.WriteLine($"update: .NET couldn't reach GitHub ({ex.Message}); trying curl/wget");
            var file = Path.GetTempFileName();
            try
            {
                RunDownloader(url, file);
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

    private static HttpClient Client()
    {
        var http = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent);
        return http;
    }

    /// <summary>curl, else wget (busybox's included), saving <paramref name="url"/> to <paramref name="path"/>.</summary>
    private static void RunDownloader(string url, string path)
    {
        (string Tool, string[] Args)[] tools =
        [
            ("curl", ["-fsSL", "-A", UserAgent, "-o", path, url]),
            ("wget", ["-q", "-U", UserAgent, "-O", path, url]),
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
