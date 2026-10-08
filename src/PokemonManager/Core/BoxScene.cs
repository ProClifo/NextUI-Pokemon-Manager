using System.Text.Json.Nodes;
using PKHeX.Core;

namespace PokemonManager.Core;

/// <summary>
/// Describes a save's party and PC boxes for the pkmgr-box viewer: which wallpaper each box uses and
/// which Gen 3 icon, sprite and details to show for every slot.
/// </summary>
/// <remarks>
/// Viewer box 0 is the party; viewer box n is PC box n - 1. Art lives in <c>res/box</c> inside the pak
/// and is generated at build time by scripts/build-box-assets.py.
/// </remarks>
public sealed class BoxScene(string assetDir)
{
    private const int WallpaperCount = 16;
    private const int PlainWallpaper = 15;
    private const ushort LastGen3Species = 386;

    public string AssetDir { get; } = assetDir;

    public bool AssetsPresent => File.Exists(Path.Combine(AssetDir, "cursor.png"));

    public JsonObject Build(SaveFile sav, string title, SlotRef start)
    {
        // The party is a 3x2 grid centred on the plain wallpaper.
        var party = Box("PARTY", Wallpaper(PlainWallpaper), 3, 2, Enumerable.Range(0, 6).Select(i => i < sav.PartyCount ? sav.GetPartySlotAtIndex(i) : null));
        party["offset_x"] = 40;
        party["offset_y"] = 48;
        var boxes = new JsonArray { party };
        for (int b = 0; b < sav.BoxCount; b++)
        {
            var mons = Enumerable.Range(0, sav.BoxSlotCount).Select(s => (PKM?)sav.GetBoxSlotAtIndex(b, s));
            int columns = 6;
            int rows = (sav.BoxSlotCount + columns - 1) / columns;
            boxes.Add(Box(SlotRef.BoxName(sav, b), Wallpaper(WallpaperFor(sav, b)), columns, rows, mons));
        }

        var (box, slot) = ToViewer(start);
        return new JsonObject
        {
            ["title"] = title,
            ["background"] = Path.Combine(AssetDir, "background.png"),
            ["cursor"] = Path.Combine(AssetDir, "cursor.png"),
            ["box"] = box,
            ["slot"] = slot,
            ["boxes"] = boxes,
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

    private JsonObject Box(string name, string wallpaper, int columns, int rows, IEnumerable<PKM?> mons)
    {
        var slots = new JsonArray();
        foreach (var pk in mons)
            slots.Add((JsonNode)(pk is { Species: > 0 } ? Slot(pk) : new JsonObject()));
        return new JsonObject
        {
            ["name"] = name,
            ["wallpaper"] = wallpaper,
            ["columns"] = columns,
            ["rows"] = rows,
            ["slots"] = slots,
        };
    }

    private JsonObject Slot(PKM pk)
    {
        var lines = new JsonArray();
        if (pk.IsEgg)
        {
            lines.Add("Egg");
        }
        else
        {
            lines.Add($"Lv.{pk.CurrentLevel}");
            if (pk.Gender is 0 or 1)
                lines.Add(pk.Gender == 0 ? "Male" : "Female");
            if (pk.HeldItem > 0)
                lines.Add(Names.Item(pk.HeldItem, pk.Context));
            lines.Add($"OT {pk.OriginalTrainerName}");
            if (pk.IsShiny)
                lines.Add("Shiny");
        }

        var name = pk.IsEgg ? "EGG" : pk.IsNicknamed ? pk.Nickname : Names.Species(pk.Species).ToUpperInvariant();
        return new JsonObject
        {
            ["name"] = name,
            ["icon"] = IconPath(pk),
            ["sprite"] = SpritePath(pk),
            ["lines"] = lines,
        };
    }

    public string IconPath(PKM pk)
    {
        var dir = Path.Combine(AssetDir, "icons");
        if (pk.IsEgg)
            return Path.Combine(dir, "egg.png");
        if (pk.Species > LastGen3Species)
            return Path.Combine(dir, "unknown.png");
        if (pk.Species == (ushort)Species.Unown)
            return Path.Combine(dir, $"201-{pk.Form}.png");
        return Path.Combine(dir, $"{pk.Species}.png");
    }

    /// <summary>Front sprite, or empty for species without Gen 3 art (the viewer then enlarges the icon).</summary>
    public string SpritePath(PKM pk)
    {
        var dir = Path.Combine(AssetDir, "sprites");
        if (pk.IsEgg)
            return Path.Combine(dir, "egg.png");
        if (pk.Species > LastGen3Species)
            return "";
        var name = pk.Species switch
        {
            (ushort)Species.Unown or (ushort)Species.Castform => $"{pk.Species}-{pk.Form}",
            _ => pk.Species.ToString(),
        };
        return Path.Combine(dir, pk.IsShiny ? $"{name}-shiny.png" : $"{name}.png");
    }
}
