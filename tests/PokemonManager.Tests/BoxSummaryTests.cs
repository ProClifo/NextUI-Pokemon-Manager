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
    public void PlatinumMemoGivesTheDateThePlaceAndTheCharacteristic()
    {
        var sav = BlankSaveFile.Get(GameVersion.Pt, "LUCAS", LanguageID.English);
        var pk = new PK4
        {
            Species = (ushort)Species.Shinx, CurrentLevel = 5, Version = GameVersion.Pt, MetLocation = 16, MetLevel = 4,
            MetDate = new DateOnly(2009, 3, 22), OriginalTrainerName = sav.OT, TID16 = sav.TID16, OriginalTrainerGender = sav.Gender,
            PID = 15, // Modest (raises Sp. Atk: likes dry food)
        };
        var lines = BoxSummary.Memo(pk, sav).Split('\n');
        Assert.Equal(["{Modest} nature.", "Mar. 22, 2009", "{Route 201}", "Met at Lv. 4.", ""], lines[..5]);
        Assert.Equal(GameInfo.Strings.characteristics[pk.Characteristic], lines[5]);
        Assert.Equal("Likes {dry} food.", lines[6]);

        pk.OriginalTrainerName = "BARRY";
        Assert.Equal(["Apparently met at", "Lv. 4."], BoxSummary.Memo(pk, sav).Split('\n')[3..5]);
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

    [Theory]
    [InlineData(GameVersion.E, "It will take some time.", "found\nby the DAY CARE couple.")]
    [InlineData(GameVersion.FR, "take some time.", "found by the\nDAY-CARE couple.")]
    public void Gen3EggShowsTheGamesEggPage(GameVersion version, string state, string memo)
    {
        using var saves = new TestSaves();
        var sav = saves.Create(version, "Egg.sav").Sav;
        var pk = TestSaves.Make(sav, Species.Bagon, 5);
        pk.IsEgg = true;
        pk.CurrentFriendship = 20; // egg cycles left
        var s = BoxSummary.For(pk, sav, inParty: false);
        Assert.Equal("?????", (string?)s["ot"]);
        Assert.Equal("?????", (string?)s["id"]);
        Assert.Equal("mystery", (string?)s["types"]![0]);
        Assert.EndsWith(state, (string?)s["state"]);
        Assert.EndsWith(memo, (string?)s["memo"]);
    }

    [Theory]
    [InlineData(GameVersion.RD)]
    [InlineData(GameVersion.C)]
    public void GameBoyTypesAreNamedRight(GameVersion version)
    {
        using var saves = new TestSaves();
        var sav = saves.Create(version, "GB.sav").Sav;
        string Types(Species species) => string.Join("/", BoxSummary.For(TestSaves.Make(sav, species, 20), sav, false)["type_names"]!.AsArray().Select(t => (string?)t));
        Assert.Equal("FIRE", Types(Species.Charmander));
        Assert.Equal("GHOST/POISON", Types(Species.Gengar));
        Assert.Equal("WATER/PSYCHIC", Types(Species.Slowpoke));
    }
}
