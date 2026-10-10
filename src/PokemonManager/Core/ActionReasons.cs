using PKHeX.Core;

namespace PokemonManager.Core;

/// <summary>
/// Why the PC's Transfer or Evolve can't be used on a Pokémon, short enough for the PC screen's message box
/// ("No games to send to!"): every one fits the Game Boy text box's two lines of 18 characters. Null means it
/// can be used.
/// </summary>
public static class ActionReasons
{
    public const string NoOtherGames = "No other games found!";
    public const string NoGamesToSendTo = "No games to send to!";
    public const string LastPartyPokemon = "It's your last party Pokémon!";
    public const string CantBeMoved = "This Pokémon can't be moved!";

    /// <summary>Why a party Pokémon can't be traded (or trade-evolved) from where the save was made.</summary>
    public static string? PartyTrade(SaveFile sav, PKM pk, bool evolve)
    {
        if (TradeLocation.PartyTradeBlocked(sav, pk) is null)
            return null;
        // Gen 5 with the C-Gear trades anywhere, except Pokémon that know an HM move.
        if (sav is SAV5 s5 && TradeLocation.HasCGear(s5))
            return "Its HM moves need a PokéCenter!";
        return evolve ? "Go to a PokéCenter to evolve!" : "Go to a PokéCenter to trade!";
    }

    /// <summary>Why a trade wouldn't evolve this Pokémon as it is now.</summary>
    public static string? Evolve(PKM pk)
    {
        if (pk.IsEgg)
            return "An Egg can't evolve!";
        var options = TradeEvolution.GetOptions(pk);
        if (options.Count == 0)
            return "It doesn't evolve by trading!";
        if (TradeEvolution.HoldsEverstone(pk))
            return "It's holding an Everstone!";
        if (options.Any(o => o.ConditionsMet))
            return null;
        var option = options[0];
        if (option.Method == EvolutionType.TradeShelmetKarrablast)
            return $"It must be traded for {(pk.Species == (ushort)Species.Karrablast ? "Shelmet" : "Karrablast")}!";
        var item = Names.Item(option.RequiredItem, pk.Context);
        return $"It needs to hold {(IsVowel(item[0]) ? "an" : "a")} {item}!";
    }

    private static bool IsVowel(char c) => "AEIOUaeiou".Contains(c);
}
