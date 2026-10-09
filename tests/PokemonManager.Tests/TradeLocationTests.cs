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
}
