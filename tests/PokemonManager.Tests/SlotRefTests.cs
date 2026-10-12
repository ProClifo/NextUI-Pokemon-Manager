using PKHeX.Core;
using PokemonManager.Core;
using Xunit;

namespace PokemonManager.Tests;

public sealed class SlotRefTests : IDisposable
{
    private readonly TestSaves _saves = new();
    public void Dispose() => _saves.Dispose();

    [Theory]
    [InlineData(GameVersion.GD)]
    [InlineData(GameVersion.RD)]
    public void ClearingAGameBoyBoxSlotMovesTheRestUp(GameVersion version)
    {
        var entry = _saves.Create(version, $"{version}.sav");
        var sav = entry.Sav;
        Species[] species = [Species.Pidgey, Species.Rattata, Species.Zubat];
        for (int i = 0; i < species.Length; i++)
            new SlotRef(0, i).Set(sav, TestSaves.Make(sav, species[i], 10));

        new SlotRef(0, 0).Clear(sav);
        var after = _saves.Roundtrip(entry).Sav;
        Assert.Equal((ushort)Species.Rattata, new SlotRef(0, 0).Get(after).Species);
        Assert.Equal((ushort)Species.Zubat, new SlotRef(0, 1).Get(after).Species);
        Assert.Equal(0, new SlotRef(0, 2).Get(after).Species);
    }

    [Fact]
    public void ClearingAGen3BoxSlotLeavesTheOthersInPlace()
    {
        var sav = _saves.Create(GameVersion.E, "E.sav").Sav;
        new SlotRef(0, 0).Set(sav, TestSaves.Make(sav, Species.Pidgey, 10));
        new SlotRef(0, 1).Set(sav, TestSaves.Make(sav, Species.Rattata, 10));
        new SlotRef(0, 0).Clear(sav);
        Assert.Equal(0, new SlotRef(0, 0).Get(sav).Species);
        Assert.Equal((ushort)Species.Rattata, new SlotRef(0, 1).Get(sav).Species);
    }

    [Fact]
    public void AnUnnamedDsBoxIsCalledBoxN()
    {
        var sav = BlankSaveFile.Get(GameVersion.HG, "ETHAN", LanguageID.English);
        Assert.Equal("Box 2", SlotRef.BoxName(sav, 1));
    }
}
