using System.IO.Compression;
using System.Text;
using PKHeX.Core;

namespace PokemonManager.Core;

/// <summary>
/// Picks the per-game background shown behind a save's menu. The game is read from the ROM's header
/// (the GBA game code at 0xAC, e.g. BPRE = FireRed USA), falling back to what the save itself says and
/// then to the file names. Art lives in <c>res/backgrounds/&lt;game&gt;</c> inside the pak, pre-scaled for each
/// screen size by scripts/build-backgrounds.py.
/// </summary>
public sealed class GameBackgrounds(string assetDir)
{
    private const int GbaCodeOffset = 0xAC;

    /// <summary>First three letters of the GBA game code; the fourth is the region/language.</summary>
    private static readonly Dictionary<string, string> GbaCodes = new()
    {
        ["AXV"] = "ruby",
        ["AXP"] = "sapphire",
        ["BPE"] = "emerald",
        ["BPR"] = "firered",
        ["BPG"] = "leafgreen",
    };

    private static readonly string[] Games = ["emerald", "firered", "leafgreen", "ruby", "sapphire"];

    public string AssetDir { get; } = assetDir;

    /// <summary>The background for a save, or null when its game has none.</summary>
    public string? Find(SaveFile sav, string savePath, IEnumerable<string> roms, (int Width, int Height)? screen)
    {
        var game = roms.Select(GameFromRom).FirstOrDefault(g => g is not null)
                   ?? GameFromSave(sav)
                   ?? GameFromName(Path.GetFileName(savePath))
                   ?? roms.Select(r => GameFromName(Path.GetFileName(r))).FirstOrDefault(g => g is not null);
        return game is null ? null : PathFor(game, screen);
    }

    public string? PathFor(string game, (int Width, int Height)? screen)
    {
        var dir = Path.Combine(AssetDir, game);
        if (screen is { } s && Path.Combine(dir, $"{s.Width}x{s.Height}.png") is var sized && File.Exists(sized))
            return sized;
        var original = Path.Combine(dir, "original.png");
        return File.Exists(original) ? original : null;
    }

    public static string? GameFromGbaCode(string code)
        => code.Length >= 3 && GbaCodes.TryGetValue(code[..3].ToUpperInvariant(), out var game) ? game : null;

    /// <summary>Reads the game code from a .gba file or the first .gba inside a .zip.</summary>
    public static string? GameFromRom(string romPath)
    {
        try
        {
            if (romPath.EndsWith(".gba", StringComparison.OrdinalIgnoreCase))
            {
                using var file = File.OpenRead(romPath);
                return GameFromHeader(file);
            }
            if (romPath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            {
                using var zip = ZipFile.OpenRead(romPath);
                var entry = zip.Entries.FirstOrDefault(e => e.Name.EndsWith(".gba", StringComparison.OrdinalIgnoreCase));
                if (entry is null)
                    return null;
                using var stream = entry.Open();
                return GameFromHeader(stream);
            }
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
        {
        }
        return null;
    }

    private static string? GameFromHeader(Stream stream)
    {
        var header = new byte[GbaCodeOffset + 4];
        int read = 0, n;
        while (read < header.Length && (n = stream.Read(header, read, header.Length - read)) > 0)
            read += n;
        return read < header.Length ? null : GameFromGbaCode(Encoding.ASCII.GetString(header, GbaCodeOffset, 4));
    }

    /// <summary>Emerald saves are told apart by PKHeX; Ruby/Sapphire and FireRed/LeafGreen share a format.</summary>
    public static string? GameFromSave(SaveFile sav) => sav.Version switch
    {
        GameVersion.E => "emerald",
        GameVersion.R => "ruby",
        GameVersion.S => "sapphire",
        GameVersion.FR => "firered",
        GameVersion.LG => "leafgreen",
        _ => null,
    };

    public static string? GameFromName(string fileName)
    {
        // "Fire Red", "Fire_Red" and "FireRed" all count.
        var name = new string(fileName.ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());
        return Games.FirstOrDefault(name.Contains);
    }

    /// <summary>Screen resolution of the NextUI device, from the PLATFORM and DEVICE variables launch.sh sees.</summary>
    public static (int Width, int Height)? ScreenSize(string? platform, string? device) => platform switch
    {
        "tg5040" => device is "brick" or "brickpro" ? (1024, 768) : (1280, 720),
        "tg5050" => (1280, 720),
        "my355" => (640, 480),
        "h700" => device switch
        {
            "rg34xx" or "rg34xxsp" or "rgsp" => (720, 480),
            "rgcubexx" => (720, 720),
            _ => (640, 480),
        },
        _ => null,
    };
}
