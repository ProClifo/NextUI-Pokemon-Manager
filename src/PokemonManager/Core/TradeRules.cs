using PKHeX.Core;

namespace PokemonManager.Core;

/// <summary>
/// The games' own rules for moving Pokémon around, applied unless "Illegal transfers" is on: which
/// generations can reach which, and how far each game must have progressed before it can send or receive.
/// Rules follow the pret decompilations: pokered/pokeyellow (Cable Club), pokegold/pokecrystal (Trade
/// Center, Time Capsule), pokeemerald/pokefirered src/trade.c (Ruby/Sapphire have no checks of their own) and
/// pokediamond/pokeplatinum/pokeheartgold (Pal Park). Gen 5's Poké Transfer Lab needing the National Pokédex
/// isn't decompiled; it's how the games are documented.
/// </summary>
public static class TradeRules
{
    // pokefirered include/constants/flags.h
    private const int FlagCanLinkWithRS = 0x844;   // FLAG_SYS_CAN_LINK_WITH_RS: the Sapphire was delivered to Celio
    // pokeemerald include/constants/flags.h
    private const int FlagIsChampionE = 0x87F;     // FLAG_IS_CHAMPION

    private const ushort KantoDexEnd = 151;        // KANTO_SPECIES_END

    // Gen 1/2 event flags (pokered/pokeyellow and pokegold/pokecrystal constants/event_flags.asm)
    private const int EventGotPokedex1 = 37;        // EVENT_GOT_POKEDEX: the Cable Club needs it
    private const int EventGaveMysteryEggToElm = 31; // the Trade Center needs it
    private const int EventMetBill = 1810;          // set at new game, cleared when Bill turns the Time Capsule on
    // Set by Mr. Pokémon's house script right after `setflag ENGINE_POKEDEX` (maps/MrPokemonsHouse.asm); the
    // engine flag itself lives outside PKHeX's event flags.
    private const int EventRivalNewBarkTown2 = 1725;

    // FLAG_SYS_POKEDEX_GET (include/constants/flags.h): SYSTEM_FLAGS + 1 in Ruby/Sapphire (0x800) and Emerald
    // (0x860), SYS_FLAGS + 0x29 in FireRed/LeafGreen.
    private const int FlagPokedexRS = 0x801, FlagPokedexE = 0x861, FlagPokedexFRLG = 0x829;

    // Gen 3 IsNationalPokedexEnabled: flag + var + the Pokédex magic byte (src/event_data.c)
    private const int FlagNationalDexE = 0x896, FlagNationalDexRS = 2102, FlagNationalDexFRLG = 0x840;
    private const int VarNationalDexRSE = 70, VarNationalDexFRLG = 78;
    private const ushort VarNationalDexValueRSE = 0x302, VarNationalDexValueFRLG = 0x6258;

    /// <summary>Gen 3's HMs, which Pal Park refuses to carry.</summary>
    private static readonly HashSet<ushort> HmMoves3 =
    [
        (ushort)Move.Cut, (ushort)Move.Fly, (ushort)Move.Surf, (ushort)Move.Strength, (ushort)Move.Flash,
        (ushort)Move.RockSmash, (ushort)Move.Waterfall, (ushort)Move.Dive,
    ];

    /// <summary>Gen 4's HMs (Defog in D/P/Pt, Whirlpool in HG/SS), which Poké Transfer refuses to carry.</summary>
    private static readonly HashSet<ushort> HmMoves4 =
    [
        (ushort)Move.Cut, (ushort)Move.Fly, (ushort)Move.Surf, (ushort)Move.Strength, (ushort)Move.Defog,
        (ushort)Move.RockSmash, (ushort)Move.Waterfall, (ushort)Move.RockClimb, (ushort)Move.Whirlpool,
    ];

    /// <summary>
    /// Whether <paramref name="pk"/> could go from <paramref name="source"/> to <paramref name="dest"/> in the
    /// real games: by trade within a generation, the Time Capsule between Gen 1 and 2, Pal Park from Gen 3 to
    /// Gen 4 and Poké Transfer from Gen 4 to Gen 5.
    /// </summary>
    /// <param name="sourceLanguage">The games' languages as gallery codes (ENG, JPN...), when known: Pal Park
    /// only migrates from a Gen 3 game of the same language.</param>
    public static OpResult CheckTransfer(PKM pk, SaveFile source, SaveFile dest, string? sourceLanguage = null, string? destLanguage = null)
    {
        var route = CheckRoute(pk, source.Generation, dest.Generation);
        if (!route.Ok)
            return route;
        return (source.Generation, dest.Generation) switch
        {
            ( <= 2, <= 2) => GameBoyLink(source, dest),
            (3, 3) => Gen3Trade(pk, source, dest),
            (3, 4) => PalPark(dest, sourceLanguage, destLanguage),
            (_, 5) when source.Generation < 5 => PokeTransfer(dest),
            _ => CheckReceive(pk, dest),
        };
    }

    /// <summary>
    /// For a Pokémon from a file rather than a save (Import): it must be able to reach this game from the
    /// generation it was saved in, and the game must be able to receive it.
    /// </summary>
    public static OpResult CheckFile(PKM pk, SaveFile dest)
    {
        var route = CheckRoute(pk, pk.Format, dest.Generation);
        return route.Ok ? CheckReceive(pk, dest) : route;
    }

    /// <summary>Whether the games had a way to move a Pokémon from Gen <paramref name="from"/> to Gen <paramref name="to"/>.</summary>
    private static OpResult CheckRoute(PKM pk, int from, int to)
    {
        bool route = from == to || (from <= 2 && to <= 2) || (from >= 3 && to > from && to <= 5);
        if (!route)
        {
            return OpResult.Fail(from <= 2 || to <= 2
                ? $"Gen {from} and Gen {to} games can't exchange Pokémon: Gen 1 and 2 only trade with each other."
                : $"Pokémon can't go back from Gen {from} to Gen {to}.");
        }

        if (from >= 3 && to > from)
        {
            if (pk.IsEgg)
                return OpResult.Fail("Eggs can't be migrated to a newer generation. Hatch it first.");
            if (from == 3 && HmMove(pk, HmMoves3) is { } hm3)
                return OpResult.Fail($"{Names.Species(pk)} knows {hm3}. Pal Park won't take Pokémon that know HM moves; make it forget the move first.");
            if (to == 5 && HmMove(pk, HmMoves4) is { } hm4)
                return OpResult.Fail($"{Names.Species(pk)} knows {hm4}. Poké Transfer won't take Pokémon that know HM moves; make it forget the move first.");
        }
        return OpResult.Success("");
    }

    /// <summary>
    /// Whether this save can take in <paramref name="pk"/> from another game (imported files): Game Boy games
    /// trade in their link rooms, so those must be open; Emerald and FireRed/LeafGreen need the National
    /// Pokédex for Pokémon outside their regional Pokédex. Not used for distributions (Distributions, Gallery,
    /// Wonder Cards), which the real games received without any of this.
    /// </summary>
    public static OpResult CheckReceive(PKM pk, SaveFile dest)
    {
        if (dest.Generation <= 2 && LinkRoomClosed(dest) is { } closed)
            return OpResult.Fail(closed);
        if (dest is SAV3E or SAV3FRLG && !NationalDex3((SAV3)dest) && !InRegionalDex(dest, pk.Species))
            return OpResult.Fail($"{Names.Game(dest)} can't receive {Names.Species(pk)} until it has the National Pokédex.");
        return OpResult.Success("");
    }

    /// <summary>
    /// Whether the player has received the Pokédex, which distributions need. Gen 1-4 read the games' own
    /// flag; Black/White have no decompilation, and register the Pokémon you have once you get the Pokédex,
    /// so a Pokédex with anything caught counts.
    /// </summary>
    public static bool HasPokedex(SaveFile sav) => sav switch
    {
        SAV1 gen1 => gen1.GetEventFlag(EventGotPokedex1),
        SAV2 gen2 => gen2.GetEventFlag(EventRivalNewBarkTown2),
        SAV3E e => e.GetEventFlag(FlagPokedexE),
        SAV3FRLG frlg => frlg.GetEventFlag(FlagPokedexFRLG),
        SAV3 rs => rs.GetEventFlag(FlagPokedexRS),
        SAV4 gen4 => PokedexObtained4(gen4),
        SAV5 gen5 => Enumerable.Range(1, gen5.MaxSpeciesID).Any(s => gen5.GetCaught((ushort)s)),
        _ => true,
    };

    /// <summary>Distributions can only be received once the player has the Pokédex.</summary>
    public static OpResult CheckDistribution(SaveFile sav) => HasPokedex(sav)
        ? OpResult.Success("")
        : OpResult.Fail($"{Names.Game(sav)} can't receive distributions until you've received the Pokédex.");

    /// <summary>The Pokédex block's pokedexObtained byte, just before nationalDexObtained (pokeplatinum include/pokedex.h).</summary>
    private static bool PokedexObtained4(SAV4 sav)
    {
        int offset = sav switch { SAV4DP => 0x138, SAV4Pt => 0x318, _ => 0x336 };
        var dex = sav.Dex.Data;
        return offset < dex.Length && dex[offset] != 0;
    }

    /// <summary>Gen 1/2 trades (Cable Club / Trade Center) and Gen 1 &lt;-&gt; Gen 2 (Time Capsule).</summary>
    private static OpResult GameBoyLink(SaveFile source, SaveFile dest)
    {
        bool timeCapsule = source.Generation != dest.Generation;
        foreach (var sav in new[] { source, dest })
        {
            if (sav is SAV2 gen2 && timeCapsule)
            {
                if (gen2.GetEventFlag(EventMetBill))
                    return OpResult.Fail($"{Names.Game(sav)}'s Time Capsule opens once Bill has switched it on, in the Ecruteak City Pokémon Center.");
            }
            else if (LinkRoomClosed(sav) is { } closed)
            {
                return OpResult.Fail(closed);
            }
        }
        return OpResult.Success("");
    }

    private static string? LinkRoomClosed(SaveFile sav) => sav switch
    {
        SAV1 gen1 when !gen1.GetEventFlag(EventGotPokedex1)
            => $"{Names.Game(sav)} can't trade until you've received the Pokédex from Professor Oak.",
        SAV2 gen2 when !gen2.GetEventFlag(EventGaveMysteryEggToElm)
            => $"{Names.Game(sav)} can't trade until you've given the Mystery Egg to Professor Elm.",
        _ => null,
    };

    /// <summary>Pal Park opens from the main menu once the Gen 4 game has the National Pokédex.</summary>
    private static OpResult PalPark(SaveFile dest, string? sourceLanguage, string? destLanguage)
    {
        if (!NationalDex4((SAV4)dest))
            return OpResult.Fail($"{Names.Game(dest)} can't use Pal Park until it has the National Pokédex.");
        if (sourceLanguage is not null && destLanguage is not null && sourceLanguage != destLanguage)
            return OpResult.Fail($"Pal Park only migrates from a Gen 3 game of the same language ({GalleryLanguage.Name(sourceLanguage)} to {GalleryLanguage.Name(destLanguage)} isn't allowed).");
        return OpResult.Success("");
    }

    /// <summary>The Poké Transfer Lab on Route 15 opens once the Gen 5 game has the National Pokédex.</summary>
    private static OpResult PokeTransfer(SaveFile dest)
    {
        if (dest is SAV5 gen5 && !gen5.Zukan.IsNationalDexUnlocked)
            return OpResult.Fail($"{Names.Game(dest)} can't use the Poké Transfer Lab until it has the National Pokédex.");
        return OpResult.Success("");
    }

    /// <summary>The game's own IsNationalPokedexEnabled: PKHeX's NationalDex only checks the magic byte.</summary>
    public static bool NationalDex3(SAV3 sav) => sav.NationalDex && sav switch
    {
        SAV3FRLG => sav.GetEventFlag(FlagNationalDexFRLG) && sav.GetWork(VarNationalDexFRLG) == VarNationalDexValueFRLG,
        SAV3E => sav.GetEventFlag(FlagNationalDexE) && sav.GetWork(VarNationalDexRSE) == VarNationalDexValueRSE,
        _ => sav.GetEventFlag(FlagNationalDexRS) && sav.GetWork(VarNationalDexRSE) == VarNationalDexValueRSE,
    };

    /// <summary>
    /// The Pokédex block's own National Pokédex flag, which the main menu checks for Pal Park (PKHeX's
    /// NationalDex is the copy in the trainer card; both are set together).
    /// </summary>
    public static bool NationalDex4(SAV4 sav)
    {
        int offset = sav switch { SAV4DP => 0x139, SAV4Pt => 0x319, _ => 0x337 };
        var dex = sav.Dex.Data;
        return sav.NationalDex || (offset < dex.Length && dex[offset] != 0);
    }

    /// <summary>Trades between two Gen 3 games, as pokeemerald/pokefirered's CanTradeSelectedMon and GetGameProgressForLinkTrade decide.</summary>
    private static OpResult Gen3Trade(PKM pk, SaveFile source, SaveFile dest)
    {
        // Linking FireRed/LeafGreen with Ruby/Sapphire/Emerald needs the Sevii Islands finished on the
        // FR/LG side, and Emerald must be Champion.
        bool frlgSource = source is SAV3FRLG, frlgDest = dest is SAV3FRLG;
        if (frlgSource != frlgDest)
        {
            var frlg = (SAV3)(frlgSource ? source : dest);
            var hoenn = (SAV3)(frlgSource ? dest : source);
            if (!frlg.GetEventFlag(FlagCanLinkWithRS))
                return OpResult.Fail($"{Names.Game(frlg)} can't link with {Names.Game(hoenn)} until the Sapphire has been delivered to Celio on One Island.");
            if (hoenn is SAV3E && !hoenn.GetEventFlag(FlagIsChampionE))
                return OpResult.Fail($"{Names.Game(hoenn)} can't link with {Names.Game(frlg)} until it has become Champion.");
        }

        foreach (var (sav, sends) in new[] { (source, true), (dest, false) })
        {
            if (sav is not (SAV3E or SAV3FRLG))
                continue; // Ruby/Sapphire don't check
            if (!NationalDex3((SAV3)sav))
            {
                if (pk.IsEgg)
                    return OpResult.Fail($"{Names.Game(sav)} can't trade Eggs until it has the National Pokédex.");
                if (!InRegionalDex(sav, pk.Species))
                    return OpResult.Fail(sends
                        ? $"{Names.Game(sav)} can't trade away {Names.Species(pk)} until it has the National Pokédex."
                        : $"{Names.Game(sav)} can't receive {Names.Species(pk)} until it has the National Pokédex.");
            }
            if (pk.Species is (ushort)Species.Mew or (ushort)Species.Deoxys && !pk.FatefulEncounter)
                return OpResult.Fail($"{Names.Game(sav)} refuses to trade a {Names.Species(pk)} that isn't from an official event.");
        }
        return OpResult.Success("");
    }

    private static bool InRegionalDex(SaveFile sav, ushort species) => sav switch
    {
        SAV3FRLG => species is > 0 and <= KantoDexEnd,
        SAV3 => HoennDex.Contains(species),
        _ => true,
    };

    private static string? HmMove(PKM pk, HashSet<ushort> hms)
    {
        for (int i = 0; i < 4; i++)
        {
            var move = pk.GetMove(i);
            if (hms.Contains(move))
                return GameInfo.Strings.movelist[move];
        }
        return null;
    }

    /// <summary>The Hoenn Pokédex (national numbers), from pokeemerald include/constants/pokedex.h up to HOENN_DEX_DEOXYS.</summary>
    private static readonly HashSet<ushort> HoennDex =
    [
        25, 26, 27, 28, 37, 38, 39, 40, 41, 42, 43, 44, 45, 54, 55, 63, 64, 65, 66, 67, 68, 72, 73, 74, 75, 76, 81, 82, 84, 85,
        88, 89, 100, 101, 109, 110, 111, 112, 116, 117, 118, 119, 120, 121, 127, 129, 130, 169, 170, 171, 172, 174, 177, 178,
        182, 183, 184, 202, 203, 214, 218, 219, 222, 227, 230, 231, 232,
        .. Enumerable.Range(252, 386 - 252 + 1).Select(i => (ushort)i),
    ];
}
