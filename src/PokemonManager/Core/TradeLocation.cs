using PKHeX.Core;

namespace PokemonManager.Core;

/// <summary>
/// Where a save's player stands, for trades out of the party: in the games you trade at a Pokémon Center's link
/// room, so a party Pokémon can only be transferred or trade-evolved while the save was made in a Pokémon Center
/// (any floor; not Ruby/Sapphire's Pokémon League center, which has no link room). Boxed Pokémon aren't limited.
/// Map numbers come from the pret decompilations.
/// </summary>
public static class TradeLocation
{
    /// <summary>Why a party Pokémon can't be traded from where this save stands, or null if it can.</summary>
    public static string? PartyTradeBlocked(SaveFile sav, PKM pk)
    {
        if (InPokemonCenter(sav) is not false)
            return null; // in a Pokémon Center, or a game whose map we can't read
        return $"{Names.Game(sav)} was saved outside a Pokémon Center. Party Pokémon can only be traded from a Pokémon Center: save there in-game first, or put it in a PC box.";
    }

    /// <summary>Whether the save was made in a Pokémon Center, or null when its map can't be read.</summary>
    public static bool? InPokemonCenter(SaveFile sav) => sav switch
    {
        SAV1 s1 => Gen1Centers.Contains(s1.Data[s1.Japanese ? 0x2600 : 0x260A]),
        SAV2 s2 => Gen2Map(s2) is var (group, number)
                   && (Gen2Centers.Contains((group, number)) || (group, number) == (s2.Version == GameVersion.C ? (11, 20) : (11, 9))),
        SAV3 s3 => Gen3Centers(s3).Contains(((sbyte)s3.Large[4], (sbyte)s3.Large[5])),
        _ => null,
    };

    // ---------------------------------------------------------------- Gen 1
    // wCurMap: 5 bytes after the player ID in the main data (map constants from map_constants.asm).

    /// <summary>The 11 Pokémon Centers and the Indigo Plateau lobby, which has a link receptionist too.</summary>
    private static readonly HashSet<byte> Gen1Centers = [41, 58, 64, 68, 81, 89, 133, 141, 154, 171, 182, 174];

    // ---------------------------------------------------------------- Gen 2
    // wMapGroup, wMapNumber: the end of the map data, 0x22 bytes before the party.

    private static (int Group, int Number) Gen2Map(SAV2 sav)
    {
        int offset = (sav.Version, sav.Japanese, sav.Korean) switch
        {
            (GameVersion.C, true, _) => 0x27F8,
            (GameVersion.C, _, _) => 0x2843,
            (_, true, _) => 0x281C,
            (_, _, true) => 0x28AA,
            _ => 0x2868,
        };
        return (sav.Data[offset], sav.Data[offset + 1]);
    }

    /// <summary>Every Pokémon Center 1F but Goldenrod's (Gold/Silver (11, 9), Crystal (11, 20)), and the shared 2F.</summary>
    private static readonly HashSet<(int, int)> Gen2Centers =
    [
        (1, 1), (2, 3), (4, 3), (5, 6), (6, 1), (7, 4), (7, 8), (8, 1), (10, 10), (10, 13), (12, 5), (14, 6),
        (16, 2), (17, 10), (18, 5), (19, 3), (21, 17), (22, 6), (23, 9), (25, 6), (26, 5),
        (20, 1), // Pokémon Center 2F (the Cable Club)
    ];

    // ---------------------------------------------------------------- Gen 3
    // SaveBlock1 starts with pos (4 bytes), then location: mapGroup, mapNum. (group, number) from map_groups.json.

    private static HashSet<(int, int)> Gen3Centers(SAV3 sav) => sav switch
    {
        SAV3E => EmeraldCenters,
        SAV3FRLG => FireRedLeafGreenCenters,
        _ => RubySapphireCenters,
    };

    /// <summary>Every 1F/2F, without the Pokémon League's center (16, 10): it has no 2F and no link room in R/S.</summary>
    private static readonly HashSet<(int, int)> RubySapphireCenters =
    [
        (2, 2), (2, 3),     // Oldale
        (3, 1), (3, 2),     // Dewford
        (4, 5), (4, 6),     // Lavaridge
        (5, 3), (5, 4),     // Fallarbor
        (6, 3), (6, 4),     // Verdanturf
        (7, 0), (7, 1),     // Pacifidlog
        (8, 4), (8, 5),     // Petalburg
        (9, 10), (9, 11),   // Slateport
        (10, 5), (10, 6),   // Mauville
        (11, 5), (11, 6),   // Rustboro
        (12, 2), (12, 3),   // Fortree
        (13, 6), (13, 7),   // Lilycove
        (14, 3), (14, 4),   // Mossdeep
        (15, 2), (15, 3),   // Sootopolis
        (16, 12), (16, 13), // Ever Grande City
    ];

    private static readonly HashSet<(int, int)> EmeraldCenters =
    [
        (2, 2), (2, 3),     // Oldale
        (3, 1), (3, 2),     // Dewford
        (4, 5), (4, 6),     // Lavaridge
        (5, 4), (5, 5),     // Fallarbor
        (6, 4), (6, 5),     // Verdanturf
        (7, 0), (7, 1),     // Pacifidlog
        (8, 4), (8, 5),     // Petalburg
        (9, 11), (9, 12),   // Slateport
        (10, 5), (10, 6),   // Mauville
        (11, 5), (11, 6),   // Rustboro
        (12, 2), (12, 3),   // Fortree
        (13, 6), (13, 7),   // Lilycove
        (14, 3), (14, 4),   // Mossdeep
        (15, 2), (15, 3),   // Sootopolis
        (16, 12), (16, 13), // Ever Grande City
        (16, 10), (16, 14), // Pokémon League 1F/2F (Emerald gave it a link room)
        (26, 53), (26, 54), // Battle Frontier
    ];

    private static readonly HashSet<(int, int)> FireRedLeafGreenCenters =
    [
        (5, 4), (5, 5),     // Viridian
        (6, 5), (6, 6),     // Pewter
        (7, 3), (7, 4),     // Cerulean
        (8, 0), (8, 1),     // Lavender
        (9, 1), (9, 2),     // Vermilion
        (10, 12), (10, 13), // Celadon
        (11, 5), (11, 6),   // Fuchsia
        (12, 5), (12, 6),   // Cinnabar
        (13, 0), (13, 1),   // Indigo Plateau
        (14, 6), (14, 7),   // Saffron
        (16, 0), (16, 1),   // Route 4
        (21, 0), (21, 1),   // Route 10
        (32, 0), (32, 1),   // One Island
        (33, 2), (33, 3),   // Two Island
        (34, 1), (34, 2),   // Three Island
        (35, 1), (35, 2),   // Four Island
        (36, 0), (36, 1),   // Five Island
        (37, 0), (37, 1),   // Six Island
        (31, 3), (31, 4),   // Seven Island
    ];
}
