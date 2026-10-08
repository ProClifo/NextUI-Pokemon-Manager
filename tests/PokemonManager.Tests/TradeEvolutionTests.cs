using PKHeX.Core;
using PokemonManager.Core;
using Xunit;

namespace PokemonManager.Tests;

public sealed class TradeEvolutionTests : IDisposable
{
    private readonly TestSaves _saves = new();

    public void Dispose() => _saves.Dispose();

    [Theory]
    [InlineData(GameVersion.RD, Species.Kadabra, Species.Alakazam)]
    [InlineData(GameVersion.C, Species.Graveler, Species.Golem)]
    [InlineData(GameVersion.E, Species.Machoke, Species.Machamp)]
    [InlineData(GameVersion.Pt, Species.Haunter, Species.Gengar)]
    [InlineData(GameVersion.B2, Species.Boldore, Species.Gigalith)]
    public void EvolvesPlainTradeEvolutions(GameVersion version, Species from, Species to)
    {
        var entry = _saves.Create(version, $"{version}.sav");
        var slot = new SlotRef(0, 0);
        slot.Set(entry.Sav, TestSaves.Make(entry.Sav, from, 40));

        var option = Assert.Single(TradeEvolution.GetOptions(slot.Get(entry.Sav)));
        Assert.True(option.ConditionsMet);
        Assert.True(TradeEvolution.Evolve(entry.Sav, slot, option).Ok);

        entry = _saves.Roundtrip(entry);
        var pk = slot.Get(entry.Sav);
        Assert.Equal((ushort)to, pk.Species);
        Assert.Equal(Names.Species((ushort)to), pk.Nickname, ignoreCase: true);
    }

    [Theory]
    [InlineData(GameVersion.E, Species.Onix, Species.Steelix, "Metal Coat")]
    [InlineData(GameVersion.HG, Species.Seadra, Species.Kingdra, "Dragon Scale")]
    [InlineData(GameVersion.C, Species.Scyther, Species.Scizor, "Metal Coat")]
    public void ItemEvolutionUsesUpTheItem(GameVersion version, Species from, Species to, string item)
    {
        var entry = _saves.Create(version, $"{version}.sav");
        var slot = new SlotRef(0, 0);
        var blank = TestSaves.Make(entry.Sav, from, 40);
        var option = Assert.Single(TradeEvolution.GetOptions(blank));

        // The evolution table's item ID must name the same item as the game's item list.
        Assert.Equal(item, Names.Item(option.RequiredItem, blank.Context));
        Assert.False(option.ConditionsMet);

        slot.Set(entry.Sav, TestSaves.Make(entry.Sav, from, 40, option.RequiredItem));
        var holding = Assert.Single(TradeEvolution.GetOptions(slot.Get(entry.Sav)));
        Assert.True(holding.ConditionsMet);

        var result = TradeEvolution.Evolve(entry.Sav, slot, holding);
        Assert.True(result.Ok, result.Message);
        entry = _saves.Roundtrip(entry);
        Assert.Equal((ushort)to, slot.Get(entry.Sav).Species);
        Assert.Equal(0, slot.Get(entry.Sav).HeldItem);
    }

    [Fact]
    public void ClamperlOffersBothEvolutions()
    {
        var entry = _saves.Create(GameVersion.E, "Emerald.sav");
        var options = TradeEvolution.GetOptions(TestSaves.Make(entry.Sav, Species.Clamperl));
        Assert.Equal(2, options.Count);
        Assert.Contains(options, o => o.Species == (ushort)Species.Huntail);
        Assert.Contains(options, o => o.Species == (ushort)Species.Gorebyss);
    }

    [Fact]
    public void EvolveAllSkipsPokemonMissingTheirItem()
    {
        var entry = _saves.Create(GameVersion.E, "Emerald.sav");
        new SlotRef(0, 0).Set(entry.Sav, TestSaves.Make(entry.Sav, Species.Kadabra));
        new SlotRef(0, 1).Set(entry.Sav, TestSaves.Make(entry.Sav, Species.Onix));
        new SlotRef(0, 2).Set(entry.Sav, TestSaves.Make(entry.Sav, Species.Pikachu));

        var (count, _) = TradeEvolution.EvolveAllEligible(entry.Sav);

        Assert.Equal(1, count);
        Assert.Equal((ushort)Species.Alakazam, new SlotRef(0, 0).Get(entry.Sav).Species);
        Assert.Equal((ushort)Species.Onix, new SlotRef(0, 1).Get(entry.Sav).Species);
    }

    [Fact]
    public void KeepsNicknames()
    {
        var entry = _saves.Create(GameVersion.E, "Emerald.sav");
        var pk = TestSaves.Make(entry.Sav, Species.Haunter);
        pk.SetNickname("SPOOKY");
        var slot = new SlotRef(0, 0);
        slot.Set(entry.Sav, pk);

        TradeEvolution.Evolve(entry.Sav, slot, TradeEvolution.GetOptions(pk)[0]);

        Assert.Equal("SPOOKY", slot.Get(entry.Sav).Nickname);
        Assert.Equal((ushort)Species.Gengar, slot.Get(entry.Sav).Species);
    }
}
