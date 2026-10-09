using PKHeX.Core;

namespace PokemonManager.Core;

/// <summary>
/// The games' own rules for moving Pokémon around, applied unless "Illegal transfers" is on: which
/// generations can reach which, and how far each game must have progressed before it can send or receive.
/// Gen 3 rules follow the trade code of the pret decompilations (pokeemerald/pokefirered src/trade.c);
/// Ruby/Sapphire's trade code has no such checks of its own.
/// </summary>
public static class TradeRules
{
    // pokefirered include/constants/flags.h
    private const int FlagCanLinkWithRS = 0x844;   // FLAG_SYS_CAN_LINK_WITH_RS: the Sapphire was delivered to Celio
    // pokeemerald include/constants/flags.h
    private const int FlagIsChampionE = 0x87F;     // FLAG_IS_CHAMPION

    private const ushort KantoDexEnd = 151;        // KANTO_SPECIES_END

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
    public static OpResult CheckTransfer(PKM pk, SaveFile source, SaveFile dest)
    {
        var route = CheckRoute(pk, source.Generation, dest.Generation);
        if (!route.Ok)
            return route;
        if (source.Generation == 3 && dest.Generation == 3)
            return Gen3Trade(pk, source, dest);
        return CheckReceive(pk, dest);
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

    /// <summary>Whether this save can take in <paramref name="pk"/> at all (for gifts, imports and transfers).</summary>
    public static OpResult CheckReceive(PKM pk, SaveFile dest)
    {
        if (dest is SAV3E or SAV3FRLG && !((SAV3)dest).NationalDex && !InRegionalDex(dest, pk.Species))
            return OpResult.Fail($"{Names.Game(dest)} can't receive {Names.Species(pk)} until it has the National Pokédex.");
        return OpResult.Success("");
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
            if (!((SAV3)sav).NationalDex)
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
