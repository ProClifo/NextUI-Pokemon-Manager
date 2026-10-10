using PKHeX.Core;

namespace PokemonManager.Core;

/// <summary>
/// Ruby/Sapphire/Emerald's decorations: the PC's eight decoration inventories in SaveBlock1 (pokeemerald
/// include/global.h: decorationDesks..decorationCushions; pokeruby: decorDesk..decorCushion), one byte per
/// decoration (src/data/decoration/header.h ids, the same in all three games). A decoration that's placed in the
/// player's room or Secret Base stays in its inventory, marked in use, so it can't be sent away.
/// </summary>
public static class Decorations
{
    public static readonly string[] Categories = ["Desk", "Chair", "Plant", "Ornament", "Mat", "Poster", "Doll", "Cushion"];
    private static readonly int[] Sizes = [10, 10, 10, 30, 30, 10, 40, 10];

    /// <summary>Every decoration's name and category, by id (gDecorations).</summary>
    public static readonly (string Name, int Category)[] All =
    [
        ("SMALL DESK", 0),
        ("SMALL DESK", 0),
        ("POKéMON DESK", 0),
        ("HEAVY DESK", 0),
        ("RAGGED DESK", 0),
        ("COMFORT DESK", 0),
        ("PRETTY DESK", 0),
        ("BRICK DESK", 0),
        ("CAMP DESK", 0),
        ("HARD DESK", 0),
        ("SMALL CHAIR", 1),
        ("POKéMON CHAIR", 1),
        ("HEAVY CHAIR", 1),
        ("PRETTY CHAIR", 1),
        ("COMFORT CHAIR", 1),
        ("RAGGED CHAIR", 1),
        ("BRICK CHAIR", 1),
        ("CAMP CHAIR", 1),
        ("HARD CHAIR", 1),
        ("RED PLANT", 2),
        ("TROPICAL PLANT", 2),
        ("PRETTY FLOWERS", 2),
        ("COLORFUL PLANT", 2),
        ("BIG PLANT", 2),
        ("GORGEOUS PLANT", 2),
        ("RED BRICK", 3),
        ("YELLOW BRICK", 3),
        ("BLUE BRICK", 3),
        ("RED BALLOON", 3),
        ("BLUE BALLOON", 3),
        ("YELLOW BALLOON", 3),
        ("RED TENT", 3),
        ("BLUE TENT", 3),
        ("SOLID BOARD", 3),
        ("SLIDE", 3),
        ("FENCE LENGTH", 3),
        ("FENCE WIDTH", 3),
        ("TIRE", 3),
        ("STAND", 3),
        ("MUD BALL", 3),
        ("BREAKABLE DOOR", 3),
        ("SAND ORNAMENT", 3),
        ("SILVER SHIELD", 3),
        ("GOLD SHIELD", 3),
        ("GLASS ORNAMENT", 3),
        ("TV", 3),
        ("ROUND TV", 3),
        ("CUTE TV", 3),
        ("GLITTER MAT", 4),
        ("JUMP MAT", 4),
        ("SPIN MAT", 4),
        ("C Low NOTE MAT", 4),
        ("D NOTE MAT", 4),
        ("E NOTE MAT", 4),
        ("F NOTE MAT", 4),
        ("G NOTE MAT", 4),
        ("A NOTE MAT", 4),
        ("B NOTE MAT", 4),
        ("C High NOTE MAT", 4),
        ("SURF MAT", 4),
        ("THUNDER MAT", 4),
        ("FIRE BLAST MAT", 4),
        ("POWDER SNOW MAT", 4),
        ("ATTRACT MAT", 4),
        ("FISSURE MAT", 4),
        ("SPIKES MAT", 4),
        ("BALL POSTER", 5),
        ("GREEN POSTER", 5),
        ("RED POSTER", 5),
        ("BLUE POSTER", 5),
        ("CUTE POSTER", 5),
        ("PIKA POSTER", 5),
        ("LONG POSTER", 5),
        ("SEA POSTER", 5),
        ("SKY POSTER", 5),
        ("KISS POSTER", 5),
        ("PICHU DOLL", 6),
        ("PIKACHU DOLL", 6),
        ("MARILL DOLL", 6),
        ("TOGEPI DOLL", 6),
        ("CYNDAQUIL DOLL", 6),
        ("CHIKORITA DOLL", 6),
        ("TOTODILE DOLL", 6),
        ("JIGGLYPUFF DOLL", 6),
        ("MEOWTH DOLL", 6),
        ("CLEFAIRY DOLL", 6),
        ("DITTO DOLL", 6),
        ("SMOOCHUM DOLL", 6),
        ("TREECKO DOLL", 6),
        ("TORCHIC DOLL", 6),
        ("MUDKIP DOLL", 6),
        ("DUSKULL DOLL", 6),
        ("WYNAUT DOLL", 6),
        ("BALTOY DOLL", 6),
        ("KECLEON DOLL", 6),
        ("AZURILL DOLL", 6),
        ("SKITTY DOLL", 6),
        ("SWABLU DOLL", 6),
        ("GULPIN DOLL", 6),
        ("LOTAD DOLL", 6),
        ("SEEDOT DOLL", 6),
        ("PIKA CUSHION", 7),
        ("ROUND CUSHION", 7),
        ("KISS CUSHION", 7),
        ("ZIGZAG CUSHION", 7),
        ("SPIN CUSHION", 7),
        ("DIAMOND CUSHION", 7),
        ("BALL CUSHION", 7),
        ("GRASS CUSHION", 7),
        ("FIRE CUSHION", 7),
        ("WATER CUSHION", 7),
        ("SNORLAX DOLL", 6),
        ("RHYDON DOLL", 6),
        ("LAPRAS DOLL", 6),
        ("VENUSAUR DOLL", 6),
        ("CHARIZARD DOLL", 6),
        ("BLASTOISE DOLL", 6),
        ("WAILMER DOLL", 6),
        ("REGIROCK DOLL", 6),
        ("REGICE DOLL", 6),
        ("REGISTEEL DOLL", 6)
    ];

    public sealed record Owned(int Category, int Index, byte Id, bool InUse)
    {
        public string Name => All[Id].Name;
    }

    public static bool Supported(SaveFile sav) => sav is SAV3RS or SAV3E;

    private static int InventoryOffset(SAV3 sav) => sav is SAV3E ? 0x2734 : 0x26A0;
    private static int RoomOffset(SAV3 sav) => sav is SAV3E ? 0x271C : 0x2688;            // 12 decorations
    private static int SecretBaseOffset(SAV3 sav) => sav is SAV3E ? 0x1AAE : 0x1A1A;      // the player's base, 16

    private static Span<byte> Inventory(SAV3 sav, int category)
    {
        int offset = InventoryOffset(sav);
        for (int i = 0; i < category; i++)
            offset += Sizes[i];
        return sav.Large.Slice(offset, Sizes[category]);
    }

    public static int Capacity(int category) => Sizes[category];

    /// <summary>The decorations of a category in the PC, the ones placed somewhere marked in use.</summary>
    public static List<Owned> List(SAV3 sav, int category)
    {
        var placed = new Dictionary<byte, int>();
        foreach (var id in sav.Large.Slice(RoomOffset(sav), 12).ToArray().Concat(sav.Large.Slice(SecretBaseOffset(sav), 16).ToArray()))
        {
            if (id != 0)
                placed[id] = placed.GetValueOrDefault(id) + 1;
        }
        var owned = new List<Owned>();
        var inventory = Inventory(sav, category);
        for (int i = 0; i < inventory.Length; i++)
        {
            byte id = inventory[i];
            if (id == 0 || id >= All.Length)
                continue;
            bool inUse = placed.GetValueOrDefault(id) > 0;
            if (inUse)
                placed[id]--;
            owned.Add(new Owned(category, i, id, inUse));
        }
        return owned;
    }

    public static int Count(SAV3 sav, int category) => List(sav, category).Count;

    public static bool HasRoom(SAV3 sav, int category) => Inventory(sav, category).IndexOf((byte)0) >= 0;

    /// <summary>Moves a decoration from one game's PC to another's. Changes are made in memory only.</summary>
    public static OpResult Send(SAV3 from, Owned decoration, SAV3 to)
    {
        if (!Supported(to))
            return OpResult.Fail($"{Names.Game(to)} has no decorations.");
        if (decoration.InUse)
            return OpResult.Fail($"The {decoration.Name} is in use. Put it away in-game first.");
        var source = Inventory(from, decoration.Category);
        if (source[decoration.Index] != decoration.Id)
            return OpResult.Fail("That decoration isn't there anymore.");
        var dest = Inventory(to, decoration.Category);
        int free = dest.IndexOf((byte)0);
        if (free < 0)
            return OpResult.Fail($"{Names.Game(to)} has no room for another {Categories[decoration.Category].ToLowerInvariant()}.");
        dest[free] = decoration.Id;
        // The game keeps each inventory packed to the front (CondenseDecorationsInCategory).
        for (int i = decoration.Index; i < source.Length - 1; i++)
            source[i] = source[i + 1];
        source[^1] = 0;
        return OpResult.Success($"The {decoration.Name} was sent to {Names.Game(to)}'s PC!");
    }
}
