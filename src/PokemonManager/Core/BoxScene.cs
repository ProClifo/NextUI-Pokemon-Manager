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

    /// <summary>The box art and at least Emerald's PC/summary skin (res/box/ui/e) are in the pak.</summary>
    public bool AssetsPresent => File.Exists(Path.Combine(AssetDir, "cursor.png")) && File.Exists(Path.Combine(AssetDir, "ui", "e", "layout.json"));

    /// <param name="language">The game's language as the gallery names it (ENG, GER, JPN...; null: the save's).</param>
    public JsonObject Build(SaveFile sav, string title, SlotRef start, UiFont? font = null, UiFont? fallbackFont = null,
        Func<SlotRef, (string? TransferBlocked, string? EvolveBlocked)>? actions = null, string? language = null)
    {
        // Names of Pokémon, moves, items and so on, and the screens' own words, in the game's language.
        var code = UiStrings.Code(language, sav);
        using var scope = UiStrings.Scope(code);
        var scene = BuildScene(sav, title, start, font, fallbackFont, actions);
        scene["language"] = code;
        scene["strings"] = UiStrings.Json(code);
        return scene;
    }

    private JsonObject BuildScene(SaveFile sav, string title, SlotRef start, UiFont? font, UiFont? fallbackFont,
        Func<SlotRef, (string? TransferBlocked, string? EvolveBlocked)>? actions)
    {
        var sets = ArtSets(sav);
        // Gen 3 Deoxys looks the way the game showing it draws it; LeafGreen's differs from FireRed's.
        bool leafGreen = sav is SAV3FRLG { Version: GameVersion.LG };
        // The party is a 3x2 grid centred on the plain wallpaper.
        var party = Box("PARTY", Wallpaper(sav, PlainWallpaper), 3, 2, Enumerable.Range(0, 6).Select(i => i < sav.PartyCount ? sav.GetPartySlotAtIndex(i) : null), sets, leafGreen, sav, SlotRef.PartyBox, actions);
        party["offset_x"] = 40;
        party["offset_y"] = 48;
        var boxes = new JsonArray { (JsonNode)party };
        for (int b = 0; b < sav.BoxCount; b++)
        {
            var mons = Enumerable.Range(0, sav.BoxSlotCount).Select(s => (PKM?)sav.GetBoxSlotAtIndex(b, s));
            int columns = 6;
            int rows = (sav.BoxSlotCount + columns - 1) / columns;
            boxes.Add((JsonNode)Box(SlotRef.BoxName(sav, b), Wallpaper(sav, WallpaperFor(sav, b)), columns, rows, mons, sets, leafGreen, sav, b, actions));
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
            ["skin"] = Skin(sav),
        };
        if (LIsA(sav))
            scene["l_is_a"] = true;
        // The PC's windows in the frame picked in the game's options
        var (menuFrame, messageFrame) = WindowFrames(sav);
        if (menuFrame is not null)
            scene["menu_frame"] = menuFrame;
        if (messageFrame is not null)
            scene["message_frame"] = messageFrame;
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

    /// <summary>
    /// The box's own wallpaper in Gen 3 and Gen 4 saves (their skins have the game's wallpapers: Diamond/Pearl and
    /// HeartGold/SoulSilver 24, Platinum 32); Black/White cycle through the first 16.
    /// </summary>
    private static int WallpaperFor(SaveFile sav, int box)
    {
        int count = sav switch { SAV4Pt => 32, SAV4DP => 24, SAV4HGSS => 24, _ => WallpaperCount };
        if (sav is (SAV3 or SAV4) and IBoxDetailWallpaper wallpapers)
        {
            int id = wallpapers.GetBoxWallpaper(box);
            if (id >= 0 && id < count)
                return id;
        }
        return box % WallpaperCount;
    }

    /// <summary>
    /// A box wallpaper from the save's own game: Ruby/Sapphire's and FireRed/LeafGreen's are in their skins
    /// (scripts/pc_ui), Emerald's (used by every other game) in res/box/wallpapers.
    /// </summary>
    private string Wallpaper(SaveFile sav, int id)
    {
        var own = Path.Combine(Skin(sav), "wallpapers", $"{id:00}.png");
        return File.Exists(own) ? own : Path.Combine(AssetDir, "wallpapers", $"{id:00}.png");
    }

    /// <summary>
    /// The folder of the PC and summary screens' art and layout (res/box/ui/&lt;game&gt;): Red/Blue's, Yellow's,
    /// Gold/Silver's and Crystal's Game Boy PCs (scripts/gb_ui), Ruby/Sapphire's, FireRed/LeafGreen's,
    /// Diamond/Pearl's, Platinum's (also for Black/White, whose PC art no decompilation has), HeartGold/SoulSilver's,
    /// and Emerald's for Emerald (scripts/pc_ui).
    /// </summary>
    /// <summary>
    /// The frame picked in the game's options, as the skin's image: (menu frame, message box frame), either null
    /// when the game has no such option or the skin lacks the image.
    /// Gen 2: wTextboxFrame (PKHeX's TextBoxFrame), all text boxes, frames/N.png.
    /// Gen 3: optionsWindowFrameType, the PC's menus and messages; Emerald and Ruby/Sapphire's 20 frames are
    /// window_frames/N.png, FireRed/LeafGreen's 10 window_frames/typeN.png.
    /// Gen 4: Options.frame (pokeplatinum include/game_options.h: bits 10-14 of the u16 just before the
    /// trainer's name in the general block), the message box (message_box_00..19 = message_frames/N.png).
    /// </summary>
    public (string? Menu, string? Message) WindowFrames(SaveFile sav)
    {
        string? Existing(string name) => File.Exists(Path.Combine(Skin(sav), name)) ? name : null;
        switch (sav)
        {
            case SAV2 s2:
                var gb = Existing($"frames/{s2.TextBoxFrame + 1}.png");
                return (gb, gb);
            case SAV3 s3:
                int type = s3.SmallBlock.OptionWindowFrame;
                var gba = Existing(sav is SAV3FRLG ? $"window_frames/type{type + 1}.png" : $"window_frames/{type + 1}.png");
                return (gba, gba);
            case SAV4 s4 when Options4(s4) is { } options:
                return (null, Existing($"message_frames/{((options >> 10) & 0x1F) + 1}.png"));
            default:
                return (null, null);
        }
    }

    /// <summary>
    /// Whether the game's BUTTON MODE is L=A: Gen 3's optionsButtonMode (SaveBlock2 byte 0x13,
    /// OPTIONS_BUTTON_MODE_L_EQUALS_A = 2), Gen 4's Options.buttonMode (bits 8-9, OPTIONS_BUTTON_MODE_L_IS_A = 2).
    /// </summary>
    public static bool LIsA(SaveFile sav) => sav switch
    {
        SAV3 s3 => s3.Small.Length > 0x13 && s3.Small[0x13] == 2,
        SAV4 s4 => Options4(s4) is { } options && ((options >> 8) & 3) == 2,
        _ => false,
    };

    /// <summary>The Gen 4 Options u16 (PlayerSave.options, 4 bytes before the trainer's name).</summary>
    internal static int? Options4(SAV4 sav)
    {
        int offset = sav switch { SAV4Pt => 0x64, SAV4DP or SAV4HGSS => 0x60, _ => -1 };
        var general = sav.General;
        return offset >= 0 && offset + 2 <= general.Length ? general[offset] | (general[offset + 1] << 8) : null;
    }

    public string Skin(SaveFile sav)
    {
        var game = sav switch
        {
            SAV1 { Version: GameVersion.YW } => "y",
            SAV1 => "rb",
            SAV2 { Version: GameVersion.C } => "c",
            SAV2 => "gs",
            SAV3RS => "rs",
            SAV3FRLG => "frlg",
            SAV4DP => "dp",
            SAV4Pt => "pt",
            SAV4HGSS => "hgss",
            SAV5 => "pt", // no decompilation has Black/White's PC art: the nearest DS one
            _ => "e",
        };
        var dir = Path.Combine(AssetDir, "ui", game);
        return Directory.Exists(dir) ? dir : Path.Combine(AssetDir, "ui", "e");
    }

    private JsonObject Box(string name, string wallpaper, int columns, int rows, IEnumerable<PKM?> mons, string[] sets, bool leafGreen, SaveFile sav, int box,
        Func<SlotRef, (string? TransferBlocked, string? EvolveBlocked)>? actions)
    {
        var slots = new JsonArray();
        int index = 0;
        foreach (var pk in mons)
        {
            var at = new SlotRef(box, index++);
            if (pk is not { Species: > 0 })
            {
                slots.Add((JsonNode)new JsonObject());
                continue;
            }
            var slot = Slot(pk, sets, leafGreen);
            try
            {
                slot["summary"] = BoxSummary.For(pk, sav, at.IsParty);
            }
            catch (Exception ex)
            {
                // One odd Pokémon mustn't keep the PC from opening: it just gets a bare summary.
                Console.Error.WriteLine($"No summary for {Names.Species(pk)} in box {box} slot {at.Slot}: {ex}");
                slot["summary"] = new JsonObject { ["nickname"] = Names.Species(pk).ToUpperInvariant(), ["egg"] = pk.IsEgg };
            }
            // The viewer's menu greys out what can't be done.
            if (actions?.Invoke(at) is { } can)
            {
                slot["can_transfer"] = can.TransferBlocked is null;
                slot["can_evolve"] = can.EvolveBlocked is null;
                // Shown in the message box when the greyed-out choice is picked.
                if (can.TransferBlocked is { } whyNotTransfer)
                    slot["transfer_why"] = whyNotTransfer;
                if (can.EvolveBlocked is { } whyNotEvolve)
                    slot["evolve_why"] = whyNotEvolve;
            }
            slots.Add((JsonNode)slot);
        }
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

        var name = pk.IsEgg ? UiStrings.EggName(pk.Format).ToUpperInvariant() : pk.IsNicknamed ? pk.Nickname : Names.Species(pk.Species).ToUpperInvariant();
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
