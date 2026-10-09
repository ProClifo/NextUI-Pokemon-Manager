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
    /// <summary>First three letters of the GBA/NDS game code; the fourth is the region/language.</summary>
    private static readonly Dictionary<string, string> GbaCodes = new()
    {
        ["AXV"] = "ruby",
        ["AXP"] = "sapphire",
        ["BPE"] = "emerald",
        ["BPR"] = "firered",
        ["BPG"] = "leafgreen",
        ["ADA"] = "diamond",
        ["APA"] = "pearl",
        ["CPU"] = "platinum",
        ["IPK"] = "heartgold",
        ["IPG"] = "soulsilver",
        ["IRB"] = "black",
        ["IRA"] = "white",
        ["IRE"] = "black2",
        ["IRD"] = "white2",
    };

    /// <summary>Every game a background can be made for (assets/backgrounds/&lt;game&gt;.png), longest names first so
    /// a file name with "LeafGreen" isn't read as Green, "FireRed" as Red, "HeartGold" as Gold or "Black 2" as Black.</summary>
    private static readonly string[] Games =
    [
        "soulsilver", "heartgold", "leafgreen", "platinum", "sapphire", "emerald", "firered", "diamond", "crystal",
        "yellow", "silver", "black2", "white2", "black", "white", "pearl", "green", "ruby", "blue", "gold", "red",
    ];

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

    /// <summary>The game named by a GBA ROM's header (a .gba file or one inside a .zip).</summary>
    public static string? GameFromRom(string romPath)
        => RomHeader.ReadGameCode(romPath) is { } code ? GameFromGbaCode(code) : null;

    /// <summary>The game PKHeX reads from the save, when it can tell (Ruby/Sapphire, FireRed/LeafGreen and
    /// Red/Blue share save formats; the ROM or the file name tells those apart).</summary>
    public static string? GameFromSave(SaveFile sav) => sav.Version switch
    {
        GameVersion.RD => "red",
        GameVersion.GN => "green",
        GameVersion.BU => "blue",
        GameVersion.YW => "yellow",
        GameVersion.GD => "gold",
        GameVersion.SI => "silver",
        GameVersion.C => "crystal",
        GameVersion.E => "emerald",
        GameVersion.R => "ruby",
        GameVersion.S => "sapphire",
        GameVersion.FR => "firered",
        GameVersion.LG => "leafgreen",
        GameVersion.D => "diamond",
        GameVersion.P => "pearl",
        GameVersion.Pt => "platinum",
        GameVersion.HG => "heartgold",
        GameVersion.SS => "soulsilver",
        GameVersion.B => "black",
        GameVersion.W => "white",
        GameVersion.B2 => "black2",
        GameVersion.W2 => "white2",
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
