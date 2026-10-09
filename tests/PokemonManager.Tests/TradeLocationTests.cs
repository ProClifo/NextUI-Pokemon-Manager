using PKHeX.Core;
using PokemonManager.Core;
using Xunit;

namespace PokemonManager.Tests;

public sealed class TradeLocationTests : IDisposable
{
    private readonly TestSaves _saves = new();
    public void Dispose() => _saves.Dispose();

    [Theory]
    [InlineData(GameVersion.RD, LanguageID.English, 0x25F3)]
    [InlineData(GameVersion.RD, LanguageID.Japanese, 0x25EE)]
    [InlineData(GameVersion.GD, LanguageID.English, 0x23DB)]
    [InlineData(GameVersion.C, LanguageID.English, 0x23DC)]
    public void GameBoyMapOffsetsShareTheLayoutPkhexReads(GameVersion version, LanguageID language, int moneyOffset)
    {
        // The map offsets were worked out from the same layout as money (BCD in Gen 1, big-endian in Gen 2).
        var sav = _saves.Create(version, $"{version}{language}.sav", language: language).Sav;
        sav.Money = 123456;
        var money = sav.Data.Slice(moneyOffset, 3).ToArray();
        Assert.Equal(version == GameVersion.RD ? new byte[] { 0x12, 0x34, 0x56 } : new byte[] { 0x01, 0xE2, 0x40 }, money);
    }

    [Theory]
    [InlineData(GameVersion.RD, LanguageID.English, 0x260A, new byte[] { 41 }, new byte[] { 0 })]     // Viridian PC; Pallet Town
    [InlineData(GameVersion.YW, LanguageID.Japanese, 0x2600, new byte[] { 174 }, new byte[] { 40 })] // Indigo lobby; Oak's lab
    [InlineData(GameVersion.GD, LanguageID.English, 0x2868, new byte[] { 11, 9 }, new byte[] { 11, 20 })]
    [InlineData(GameVersion.C, LanguageID.English, 0x2843, new byte[] { 11, 20 }, new byte[] { 11, 9 })]
    [InlineData(GameVersion.C, LanguageID.English, 0x2843, new byte[] { 20, 1 }, new byte[] { 20, 2 })]
    [InlineData(GameVersion.C, LanguageID.Japanese, 0x27F8, new byte[] { 26, 5 }, new byte[] { 24, 4 })]
    public void GameBoyCentersAreRecognised(GameVersion version, LanguageID language, int offset, byte[] center, byte[] elsewhere)
    {
        var sav = _saves.Create(version, $"{version}{language}.sav", language: language).Sav;
        center.CopyTo(sav.Data[offset..]);
        Assert.True(TradeLocation.InPokemonCenter(sav));
        elsewhere.CopyTo(sav.Data[offset..]);
        Assert.False(TradeLocation.InPokemonCenter(sav));
    }

    [Theory]
    [InlineData(GameVersion.R, 16, 12, true)]  // Ever Grande City
    [InlineData(GameVersion.R, 16, 10, false)] // R/S Pokémon League: no link room
    [InlineData(GameVersion.E, 16, 10, true)]  // Emerald gave it one
    [InlineData(GameVersion.E, 0, 0, false)]
    [InlineData(GameVersion.FR, 13, 1, true)]  // Indigo Plateau 2F
    [InlineData(GameVersion.LG, 3, 0, false)]
    public void Gen3CentersAreRecognised(GameVersion version, int group, int number, bool center)
    {
        var sav = (SAV3)_saves.Create(version, $"{version}.sav").Sav;
        sav.Large[4] = (byte)group;
        sav.Large[5] = (byte)number;
        Assert.Equal(center, TradeLocation.InPokemonCenter(sav));
    }

    [Fact]
    public void Gen3MapSitsAfterThePosition()
    {
        var sav = (SAV3)_saves.Create(GameVersion.E, "E.sav").Sav;
        Assert.Equal(sav.PartyCount, sav.Large[0x234]);
    }

    [Theory]
    [InlineData(GameVersion.D, 6, true)]       // Jubilife PC 1F
    [InlineData(GameVersion.Pt, 496, true)]    // Pokémon League lobby B1F
    [InlineData(GameVersion.Pt, 3, false)]
    [InlineData(GameVersion.HG, 300, true)]    // Indigo Plateau
    [InlineData(GameVersion.SS, 2, false)]     // Union Room
    public void Gen4CentersAreRecognised(GameVersion version, int map, bool center)
    {
        var sav = (SAV4)_saves.Create(version, $"{version}.sav").Sav;
        sav.M = map;
        Assert.Equal(center, TradeLocation.InPokemonCenter(sav));
    }

    [Theory]
    [InlineData(GameVersion.W, 8, true)]       // Striaton
    [InlineData(GameVersion.W, 435, false)]    // Aspertia's center is B2W2's
    [InlineData(GameVersion.B2, 435, true)]
    [InlineData(GameVersion.B2, 422, false)]   // Union Room
    public void Gen5CentersAreRecognised(GameVersion version, int zone, bool center)
    {
        var sav = (SAV5)_saves.Create(version, $"{version}.sav").Sav;
        sav.PlayerPosition.M = zone;
        Assert.Equal(center, TradeLocation.InPokemonCenter(sav));
    }

    [Theory]
    [InlineData(GameVersion.B, 0x73)]
    [InlineData(GameVersion.W2, 0x6A)]
    public void Gen5TradesAnywhereWithTheCGearUnlessItKnowsAnHm(GameVersion version, int cgearFlag)
    {
        var sav = (SAV5)_saves.Create(version, $"{version}.sav").Sav;
        sav.PlayerPosition.M = 0; // outside
        var pk = TestSaves.Make(sav, Species.Pidove, 20);
        pk.SetMove(0, (ushort)Move.Tackle);
        Assert.NotNull(TradeLocation.PartyTradeBlocked(sav, pk)); // no C-Gear yet

        sav.EventWork.SetEventFlag(cgearFlag, true);
        Assert.Null(TradeLocation.PartyTradeBlocked(sav, pk));

        pk.SetMove(1, (ushort)Move.Fly);
        Assert.Contains("HM", TradeLocation.PartyTradeBlocked(sav, pk));
        sav.PlayerPosition.M = 8; // Striaton's Pokémon Center
        Assert.Null(TradeLocation.PartyTradeBlocked(sav, pk));
    }
}
