namespace PokemonManager;

/// <summary>
/// Folder layout. On device everything user-facing lives in /mnt/SDCARD/PokemonManager so it's easy to
/// reach from a PC; saves are read from NextUI's own Saves folder.
/// </summary>
public sealed class AppPaths
{
    public required IReadOnlyList<string> SaveRoots { get; init; }
    public required IReadOnlyList<string> RomRoots { get; init; }
    public required string DataDir { get; init; }
    public required string TempDir { get; init; }

    /// <summary>PC box art (res/box in the pak; the binary lives in bin/arm64).</summary>
    public string BoxAssetsDir { get; init; } = Environment.GetEnvironmentVariable("PKMGR_ASSETS")
        ?? Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "res", "box"));

    /// <summary>Per-game menu backgrounds (res/backgrounds in the pak).</summary>
    public string BackgroundsDir { get; init; } = Environment.GetEnvironmentVariable("PKMGR_BACKGROUNDS")
        ?? Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "res", "backgrounds"));

    /// <summary>The bundled EventsGallery files (res/gallery.zip in the pak).</summary>
    public string GalleryFile { get; init; } = Environment.GetEnvironmentVariable("PKMGR_GALLERY")
        ?? Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "res", "gallery.zip"));

    /// <summary>The games' menu fonts (res/fonts in the pak).</summary>
    public string FontsDir { get; init; } = Environment.GetEnvironmentVariable("PKMGR_FONTS")
        ?? Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "res", "fonts"));

    public string GiftsDir => Path.Combine(DataDir, "Gifts");
    public string ImportDir => Path.Combine(DataDir, "Import");
    public string ExportDir => Path.Combine(DataDir, "Export");
    public string BackupDir => Path.Combine(DataDir, "Backups");
    public string ExtraSavesDir => Path.Combine(DataDir, "Saves");
    public string SettingsFile => Path.Combine(DataDir, "settings.txt");
    public string RomCheckCache => Path.Combine(DataDir, "rom-check-cache.txt");

    public void EnsureCreated()
    {
        foreach (var dir in new[] { DataDir, GiftsDir, ImportDir, ExportDir, BackupDir, ExtraSavesDir, TempDir })
            Directory.CreateDirectory(dir);
    }

    public static AppPaths FromEnvironment(string? sdOverride = null, string? dataOverride = null)
    {
        var sd = sdOverride ?? Environment.GetEnvironmentVariable("SDCARD_PATH") ?? "/mnt/SDCARD";
        var data = dataOverride ?? Path.Combine(sd, "PokemonManager");
        var tmp = Path.Combine(Path.GetTempPath(), "pokemon-manager");
        return new AppPaths
        {
            SaveRoots = [Path.Combine(sd, "Saves"), Path.Combine(data, "Saves")],
            RomRoots = [Path.Combine(sd, "Roms")],
            DataDir = data,
            TempDir = tmp,
        };
    }
}

public sealed class AppSettings
{
    /// <summary>
    /// Allow moving Pokémon in ways the games never did (Gen 4 back to Gen 3, Gen 1 to Gen 3...) and skip the
    /// games' trade requirements (National Pokédex, story progress). Off by default.
    /// </summary>
    public bool AllowIllegalTransfers { get; set; }
    /// <summary>Only list saves whose ROM is an unmodified retail Pokémon game (on by default).</summary>
    public bool OnlyOfficialRoms { get; set; } = true;
    /// <summary>Browse Pokémon on a Gen 3 style PC box screen instead of lists (on by default).</summary>
    public bool PcBoxView { get; set; } = true;
    public bool SeenWelcome { get; set; }
    /// <summary>List gallery files in every language, not just the game's (off by default).</summary>
    public bool GalleryAllLanguages { get; set; }
    /// <summary>List the gallery's unreleased files: debug and test cards never distributed (off by default).</summary>
    public bool GalleryUnreleased { get; set; }

    public static AppSettings Load(string path)
    {
        var s = new AppSettings();
        if (!File.Exists(path))
            return s;
        foreach (var line in File.ReadAllLines(path))
        {
            var parts = line.Split('=', 2, StringSplitOptions.TrimEntries);
            if (parts.Length != 2)
                continue;
            bool on = parts[1].Equals("true", StringComparison.OrdinalIgnoreCase);
            switch (parts[0])
            {
                case "allow_illegal_transfers" or "allow_unofficial_transfers": s.AllowIllegalTransfers = on; break;
                case "seen_welcome": s.SeenWelcome = on; break;
                case "only_official_roms": s.OnlyOfficialRoms = on; break;
                case "pc_box_view": s.PcBoxView = on; break;
                case "gallery_all_languages": s.GalleryAllLanguages = on; break;
                case "gallery_unreleased": s.GalleryUnreleased = on; break;
            }
        }
        return s;
    }

    public void Save(string path)
    {
        try
        {
            File.WriteAllLines(path,
            [
                $"allow_illegal_transfers={AllowIllegalTransfers.ToString().ToLowerInvariant()}",
                $"seen_welcome={SeenWelcome.ToString().ToLowerInvariant()}",
                $"only_official_roms={OnlyOfficialRoms.ToString().ToLowerInvariant()}",
                $"pc_box_view={PcBoxView.ToString().ToLowerInvariant()}",
                $"gallery_all_languages={GalleryAllLanguages.ToString().ToLowerInvariant()}",
                $"gallery_unreleased={GalleryUnreleased.ToString().ToLowerInvariant()}",
            ]);
        }
        catch (IOException)
        {
            // settings are a convenience; never fail an operation over them
        }
    }
}
