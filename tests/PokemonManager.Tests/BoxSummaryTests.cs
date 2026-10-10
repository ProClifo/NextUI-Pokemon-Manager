using PKHeX.Core;
using PokemonManager.Core;
using Xunit;

namespace PokemonManager.Tests;

public sealed class BoxSummaryTests : IDisposable
{
    private readonly TestSaves _saves = new();
    public void Dispose() => _saves.Dispose();

    [Fact]
    public void EventPokemonFromAnotherTrainerWereObtainedInAFatefulEncounter()
    {
        var sav = _saves.Create(GameVersion.E, "E.sav").Sav;
        var mew = TestSaves.Make(sav, Species.Mew, 10);
        mew.OriginalTrainerName = "Aura";
        mew.TID16 = 20078;
        mew.FatefulEncounter = true;
        mew.MetLevel = 10;
        mew.PID = 0; // Hardy (Gen 3 natures come from the PID)
        Assert.Equal("{HARDY} nature,\nobtained in a fateful\nencounter at Lv{10}.", BoxSummary.Memo(mew, sav));
    }

    [Fact]
    public void OwnPokemonWereMetSomewhere()
    {
        var sav = _saves.Create(GameVersion.E, "E.sav").Sav;
        var mudkip = TestSaves.Make(sav, Species.Mudkip, 5);
        mudkip.MetLevel = 5;
        mudkip.MetLocation = 16; // Route 101
        var nature = GameInfo.Strings.natures[(int)mudkip.Nature].ToUpperInvariant();
        Assert.Equal($"{{{nature}}} nature,\nmet at Lv{{5}},\n{{ROUTE 101}}.", BoxSummary.Memo(mudkip, sav));
    }

    [Fact]
    public void SummaryHasStatsAndMovesForABoxedPokemon()
    {
        var sav = _saves.Create(GameVersion.E, "E.sav").Sav;
        var pk = TestSaves.Make(sav, Species.Pikachu, 30);
        pk.SetMove(0, (ushort)Move.ThunderShock);
        pk.Move1_PP = pk.GetMovePP(pk.Move1, 0);
        var s = BoxSummary.For(pk, sav, inParty: false);
        Assert.Equal("PIKACHU", (string?)s["nickname"]);
        Assert.Equal("PIKACHU", (string?)s["species"]);
        Assert.Equal("30", (string?)s["level"]);
        Assert.Equal("electric", (string?)s["types"]![0]);
        Assert.Equal("THUNDER SHOCK", (string?)s["moves"]![0]!["name"]);
        Assert.Equal(30, (int)s["moves"]![0]!["max_pp"]!);
        Assert.NotEqual("0", (string?)s["attack"]); // worked out for a box Pokémon
        Assert.Equal("-", (string?)s["moves"]![1]!["name"]);
    }
}
