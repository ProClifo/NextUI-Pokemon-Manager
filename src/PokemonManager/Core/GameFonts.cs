using System.Text.Json;
using PKHeX.Core;

namespace PokemonManager.Core;

/// <summary>
/// A game's own menu font, as TrueType files sized for each place text is drawn (see scripts/build-fonts.py):
/// minui-list's items and title, and minui-presenter's messages (drawn at <see cref="MessageSize"/>).
/// </summary>
public sealed record GameFont(string List, string Title, string Message, int MessageSize, IReadOnlySet<char> Chars)
{
    /// <summary>Whether every character of the text is in the font; otherwise the NextUI font is used.</summary>
    public bool Covers(IEnumerable<string?> texts)
        => texts.All(t => t is null || t.All(c => char.IsControl(c) || Chars.Contains(c)));
}

/// <summary>Picks a save's game font from res/fonts in the pak (fonts.json lists the files).</summary>
public sealed class GameFonts(string dir, int scale)
{
    private Dictionary<string, GameFont>? _fonts;

    /// <summary>The device's UI scale: 3 on the TrimUI Brick, 2 everywhere else NextUI runs.</summary>
    public static int Scale(string? platform, string? device) => platform == "tg5040" && device is "brick" or "brickpro" ? 3 : 2;

    /// <summary>The font file set for a save's game (null for Gen 5, which has no decompiled font, and for
    /// Japanese/Korean games, whose text the Latin fonts can't show).</summary>
    public GameFont? For(SaveFile sav, string language)
    {
        if (language is GalleryLanguage.Japanese or GalleryLanguage.Korean)
            return null;
        var name = sav switch
        {
            SAV1 => "gen1",
            SAV2 { Version: GameVersion.C } => "gen2-c",
            SAV2 => "gen2-gs",
            SAV3FRLG => "gen3-frlg",
            SAV3 => "gen3-rse",
            SAV4 => "gen4",
            _ => null,
        };
        return name is not null && (_fonts ??= Load()).TryGetValue(name, out var font) ? font : null;
    }

    private Dictionary<string, GameFont> Load()
    {
        var fonts = new Dictionary<string, GameFont>();
        var manifest = Path.Combine(dir, "fonts.json");
        if (!File.Exists(manifest))
            return fonts;
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(manifest));
            foreach (var font in doc.RootElement.EnumerateObject())
            {
                if (!font.Value.TryGetProperty($"{scale}x", out var files))
                    continue;
                string File(string role) => Path.Combine(dir, files.GetProperty(role).GetString()!);
                var chars = System.IO.File.ReadAllText(Path.Combine(dir, font.Value.GetProperty("chars").GetString()!));
                var entry = new GameFont(File("list"), File("title"), File("message"), files.GetProperty("message_size").GetInt32(),
                    chars.ToHashSet());
                if (System.IO.File.Exists(entry.List) && System.IO.File.Exists(entry.Title) && System.IO.File.Exists(entry.Message))
                    fonts[font.Name] = entry;
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException or KeyNotFoundException or InvalidOperationException)
        {
            Console.Error.WriteLine($"Couldn't read the game fonts: {ex.Message}");
        }
        return fonts;
    }
}
