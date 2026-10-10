using PKHeX.Core;
using PokemonManager.Core;
using Xunit;

namespace PokemonManager.Tests;

public sealed class ActionReasonsTests : IDisposable
{
    private readonly TestSaves _saves = new();

    public void Dispose() => _saves.Dispose();

    [Fact]
    public void EvolveReasonsAreShortAndSpecific()
    {
        var sav = _saves.Create(GameVersion.E, "Emerald.sav").Sav;
        Assert.Equal("It doesn't evolve by trading!", ActionReasons.Evolve(TestSaves.Make(sav, Species.Pikachu, 10)));

        var onix = TestSaves.Make(sav, Species.Onix, 20);
        Assert.Equal("It needs to hold a Metal Coat!", ActionReasons.Evolve(onix));

        var kadabra = TestSaves.Make(sav, Species.Kadabra, 20);
        Assert.Null(ActionReasons.Evolve(kadabra));
        kadabra.HeldItem = (ushort)Array.IndexOf(GameInfo.Strings.GetItemStrings(EntityContext.Gen3), "Everstone");
        Assert.Equal("It's holding an Everstone!", ActionReasons.Evolve(kadabra));

        var porygon = TestSaves.Make(sav, Species.Porygon, 20);
        Assert.Equal("It needs to hold an Up-Grade!", ActionReasons.Evolve(porygon));
    }

    [Fact]
    public void EveryReasonFitsTwoLinesOfTheGameBoyTextBox()
    {
        // 18 tiles a line, two lines (the longest reasons, with the longest item names).
        string[] reasons =
        [
            ActionReasons.NoOtherGames, ActionReasons.NoGamesToSendTo, ActionReasons.LastPartyPokemon, ActionReasons.CantBeMoved,
            "Go to a PokéCenter to evolve!", "Go to a PokéCenter to trade!", "Its HM moves need a PokéCenter!",
            "An Egg can't evolve!", "It doesn't evolve by trading!", "It's holding an Everstone!",
            "It must be traded for Karrablast!", "It needs to hold a Dragon Scale!", "It needs to hold a Deep Sea Scale!",
        ];
        foreach (var reason in reasons)
            Assert.True(FitsTwoLines(reason, 18), reason);
    }

    private static bool FitsTwoLines(string text, int width)
    {
        if (text.Length <= width)
            return true;
        int split = text.LastIndexOf(' ', Math.Min(width, text.Length - 1));
        return split > 0 && text.Length - split - 1 <= width;
    }
}
