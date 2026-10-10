using System.Text.Json;
using System.Text.Json.Nodes;
using PKHeX.Core;

namespace PokemonManager.Core;

/// <summary>
/// Describes a save's party and PC boxes for the pkmgr-box viewer: which wallpaper each box uses, which
/// icon, sprite and details to show for every slot, and the game's font.
/// </summary>
/// <remarks>
/// Viewer box 0 is the party; viewer box n is PC box n - 1. Art lives in <c>res/box</c> inside the pak and
/// is generated at build time: wallpapers, cursor and background by scripts/build-box-assets.py, and each
/// game's own icons and sprites by scripts/build-box-art.py, as sheets in <c>art/&lt;set&gt;</c> whose
/// index.json gives every image's place.
/// </remarks>
public sealed class BoxScene(string assetDir)
{
    private const int WallpaperCount = 16;
    private const int PlainWallpaper = 15;
    private readonly Dictionary<string, ArtIndex?> _indexes = [];

    public string AssetDir { get; } = assetDir;

    public bool AssetsPresent => File.Exists(Path.Combine(AssetDir, "cursor.png"));

    public JsonObject Build(SaveFile sav, string title, SlotRef start, UiFont? font = null, UiFont? fallbackFont = null)
    {
        var sets = ArtSets(sav);
        // Gen 3 Deoxys looks the way the game showing it draws it; LeafGreen's differs from FireRed's.
        bool leafGreen = sav is SAV3FRLG { Version: GameVersion.LG };
        // The party is a 3x2 grid centred on the plain wallpaper.
        var party = Box("PARTY", Wallpaper(PlainWallpaper), 3, 2, Enumerable.Range(0, 6).Select(i => i < sav.PartyCount ? sav.GetPartySlotAtIndex(i) : null), sets, leafGreen);
        party["offset_x"] = 40;
        party["offset_y"] = 48;
        var boxes = new JsonArray { (JsonNode)party };
        for (int b = 0; b < sav.BoxCount; b++)
        {
            var mons = Enumerable.Range(0, sav.BoxSlotCount).Select(s => (PKM?)sav.GetBoxSlotAtIndex(b, s));
            int columns = 6;
            int rows = (sav.BoxSlotCount + columns - 1) / columns;
            boxes.Add((JsonNode)Box(SlotRef.BoxName(sav, b), Wallpaper(WallpaperFor(sav, b)), columns, rows, mons, sets, leafGreen));
        }

        var (box, slot) = ToViewer(start);
        var scene = new JsonObject
        {
            ["title"] = title,
            ["background"] = Path.Combine(AssetDir, "background.png"),
            ["cursor"] = Path.Combine(AssetDir, "cursor.png"),
            ["box"] = box,
            ["slot"] = slot,
            ["boxes"] = boxes,
        };
        // The font to draw in, unless it lacks some of the text (OG has no Japanese): then the fallback.
        var chosen = font is not null && font.Covers(Strings(scene)) ? font : fallbackFont;
        if (chosen is not null)
            scene["font"] = chosen.Path;
        return scene;
    }

    private static IEnumerable<string?> Strings(JsonNode? node) => node switch
    {
        JsonObject o => o.SelectMany(p => Strings(p.Value)),
        JsonArray a => a.SelectMany(Strings),
        JsonValue v when v.TryGetValue<string>(out var s) => [s],
        _ => [],
    };

    /// <summary>The art sets to take a save's icons and sprites from, best first: its own game's, then
    /// Emerald's and Platinum's for anything that game lacks (Gen 5 saves use Platinum's).</summary>
    public static string[] ArtSets(SaveFile sav)
    {
        var own = sav switch
        {
            SAV1 { Version: GameVersion.YW } => "y",
            SAV1 => "rb",
            SAV2 { Version: GameVersion.C } => "c",
            SAV2 { Version: GameVersion.SI } => "silver",
            SAV2 => "gold",
            SAV3E => "e",
            SAV3FRLG => "frlg",
            SAV3 => "rs",
            _ => "pt",
        };
        return own switch
        {
            "e" => ["e", "pt"],
            "pt" => ["pt", "e"],
            _ => [own, "e", "pt"],
        };
    }

    public static (int Box, int Slot) ToViewer(SlotRef slot) => slot.IsParty ? (0, slot.Slot) : (slot.Box + 1, slot.Slot);

    public static SlotRef FromViewer(int box, int slot) => box == 0 ? SlotRef.Party(slot) : new SlotRef(box - 1, slot);

    /// <summary>The box's own wallpaper in Gen 3 saves; other games cycle through the Gen 3 set.</summary>
    private static int WallpaperFor(SaveFile sav, int box)
    {
        if (sav is SAV3 and IBoxDetailWallpaper wallpapers)
        {
            int id = wallpapers.GetBoxWallpaper(box);
            if (id is >= 0 and < WallpaperCount)
                return id;
        }
        return box % WallpaperCount;
    }

    private string Wallpaper(int id) => Path.Combine(AssetDir, "wallpapers", $"{id:00}.png");

    private JsonObject Box(string name, string wallpaper, int columns, int rows, IEnumerable<PKM?> mons, string[] sets, bool leafGreen)
    {
        var slots = new JsonArray();
        foreach (var pk in mons)
            slots.Add((JsonNode)(pk is { Species: > 0 } ? Slot(pk, sets, leafGreen) : new JsonObject()));
        return new JsonObject
        {
            ["name"] = name,
            ["wallpaper"] = wallpaper,
            ["columns"] = columns,
            ["rows"] = rows,
            ["slots"] = slots,
        };
    }

    private JsonObject Slot(PKM pk, string[] sets, bool leafGreen)
    {
        var lines = new JsonArray();
        if (pk.IsEgg)
        {
            lines.Add((JsonNode)"Egg");
        }
        else
        {
            lines.Add((JsonNode)$"Lv.{pk.CurrentLevel}");
            if (pk.Gender is 0 or 1)
                lines.Add((JsonNode)(pk.Gender == 0 ? "Male" : "Female"));
            if (pk.HeldItem > 0)
                lines.Add((JsonNode)Names.Item(pk.HeldItem, pk.Context));
            lines.Add((JsonNode)$"OT {pk.OriginalTrainerName}");
            if (pk.IsShiny)
                lines.Add((JsonNode)"Shiny");
        }

        var name = pk.IsEgg ? "EGG" : pk.IsNicknamed ? pk.Nickname : Names.Species(pk.Species).ToUpperInvariant();
        var slot = new JsonObject { ["name"] = name, ["lines"] = lines };
        if (Icon(pk, sets, leafGreen) is { } icon)
        {
            slot["icon"] = icon.Sheet;
            slot["icon_rect"] = new JsonArray(icon.X, icon.Y, 32, 64);
        }
        else
        {
            slot["icon"] = Path.Combine(AssetDir, "unknown.png");
        }
        if (Sprite(pk, sets, leafGreen) is { } sprite)
        {
            slot["sprite"] = sprite.Sheet;
            slot["sprite_rect"] = new JsonArray(sprite.X, sprite.Y, sprite.W, sprite.H);
        }
        return slot;
    }

    /// <summary>A picture in one of the art sheets.</summary>
    public sealed record ArtImage(string Sheet, int X, int Y, int W, int H);

    private sealed record ArtIndex(Dictionary<string, int[]> Icons, Dictionary<string, int[]> Sprites);

    /// <summary>The box icon (both 32x32 frames), or null for species no set has (the viewer shows "?").</summary>
    public ArtImage? Icon(PKM pk, string[] sets, bool leafGreen = false)
    {
        var names = pk.IsEgg ? ["egg"] : FormNames(pk, leafGreen);
        foreach (var set in sets)
        {
            if (Index(set) is not { } index)
                continue;
            foreach (var n in names)
            {
                if (index.Icons.TryGetValue(n, out var xy))
                    return new ArtImage(Path.Combine(AssetDir, "art", set, "icons.png"), xy[0], xy[1], 32, 64);
            }
        }
        return null;
    }

    /// <summary>The front sprite (female and shiny where the game has them), or null for species no set has
    /// (the viewer then enlarges the icon).</summary>
    public ArtImage? Sprite(PKM pk, string[] sets, bool leafGreen = false)
    {
        var names = new List<string>();
        if (pk.IsEgg)
        {
            names.Add("egg");
        }
        else
        {
            foreach (var form in FormNames(pk, leafGreen))
            {
                foreach (var gender in pk.Gender == 1 ? new[] { "-f", "" } : [""])
                {
                    if (pk.IsShiny)
                        names.Add($"{form}{gender}-shiny");
                    names.Add($"{form}{gender}");
                }
            }
        }
        foreach (var set in sets)
        {
            if (Index(set) is not { } index)
                continue;
            foreach (var n in names)
            {
                if (index.Sprites.TryGetValue(n, out var r))
                    return new ArtImage(Path.Combine(AssetDir, "art", set, "sprites.png"), r[0], r[1], r[2], r[3]);
            }
        }
        return null;
    }

    /// <summary>Art names for a Pokémon's form, best first: "&lt;dex&gt;-&lt;form&gt;" (forms in PKHeX's order;
    /// LeafGreen's Deoxys is "386-leafgreen"), then the plain species.</summary>
    private static List<string> FormNames(PKM pk, bool leafGreen)
    {
        var names = new List<string>();
        if (leafGreen && pk.Species == (ushort)Species.Deoxys)
            names.Add("386-leafgreen");
        if (pk.Form > 0 || pk.Species == (ushort)Species.Unown)
            names.Add($"{pk.Species}-{pk.Form}");
        names.Add(pk.Species.ToString());
        return names;
    }

    private ArtIndex? Index(string set)
    {
        if (_indexes.TryGetValue(set, out var cached))
            return cached;
        ArtIndex? index = null;
        var path = Path.Combine(AssetDir, "art", set, "index.json");
        try
        {
            if (File.Exists(path))
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(path));
                Dictionary<string, int[]> Read(string kind) => doc.RootElement.GetProperty(kind).EnumerateObject()
                    .ToDictionary(p => p.Name, p => p.Value.EnumerateArray().Select(v => v.GetInt32()).ToArray());
                index = new ArtIndex(Read("icons"), Read("sprites"));
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException or KeyNotFoundException or InvalidOperationException)
        {
            Console.Error.WriteLine($"Couldn't read the box art index {path}: {ex.Message}");
        }
        return _indexes[set] = index;
    }
}
