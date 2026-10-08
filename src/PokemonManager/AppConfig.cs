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
    public bool AllowUnofficialTransfers { get; set; }
    /// <summary>Only list saves whose ROM is an unmodified retail Pokémon game (on by default).</summary>
    public bool OnlyOfficialRoms { get; set; } = true;
    public bool SeenWelcome { get; set; }

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
                case "allow_unofficial_transfers": s.AllowUnofficialTransfers = on; break;
                case "seen_welcome": s.SeenWelcome = on; break;
                case "only_official_roms": s.OnlyOfficialRoms = on; break;
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
                $"allow_unofficial_transfers={AllowUnofficialTransfers.ToString().ToLowerInvariant()}",
                $"seen_welcome={SeenWelcome.ToString().ToLowerInvariant()}",
                $"only_official_roms={OnlyOfficialRoms.ToString().ToLowerInvariant()}",
            ]);
        }
        catch (IOException)
        {
            // settings are a convenience; never fail an operation over them
        }
    }
}
