using PKHeX.Core;
using PokemonManager.Core;
using Xunit;

namespace PokemonManager.Tests;

public sealed class MysteryGift2Tests
{
    private static SAV2 Crystal(bool unlocked)
    {
        var sav = new SAV2(LanguageID.English, GameVersion.C);
        sav.Data[0xBE3] = sav.Data[0xBE5] = (byte)(unlocked ? 0 : 0xFF);
        return sav;
    }

    [Fact]
    public void HiddenUntilTheGoldenrodGirlUnlocksIt()
    {
        Assert.Equal(0, MysteryGift2.Left(Crystal(unlocked: false), 10));
        Assert.Equal(5, MysteryGift2.Left(Crystal(unlocked: true), 10));
        Assert.False(MysteryGift2.Supported(new SAV2(LanguageID.Japanese, GameVersion.C)));
    }

    [Fact]
    public void FiveGiftsADayGoIntoTheBag()
    {
        var sav = Crystal(unlocked: true);
        var items = MysteryGift2.Receive(sav, 10, new Random(1));
        Assert.NotNull(items);
        Assert.Equal(5, items.Count);
        var bag = sav.Inventory;
        foreach (var item in items.Distinct())
            Assert.Contains(bag.Pouches, p => p.HasItem(item));
        // the game sees today's five, with the countdown started today
        Assert.Equal(5, sav.Data[0xBE5]);
        Assert.Equal(new byte[] { 1, 10 }, sav.Data.Slice(0xBFA, 2).ToArray());
        Assert.Equal(0, MysteryGift2.Left(sav, 10));
        Assert.Equal(5, MysteryGift2.Left(sav, 11));
        // wCurDay wraps at 140
        Assert.Equal(0, MysteryGift2.Left(sav, 150));
    }

    [Fact]
    public void ItemOddsFollowTheGame()
    {
        var rng = new Random(7);
        var tiers = new int[4];
        for (int i = 0; i < 200_000; i++)
        {
            int index = MysteryGift2.Sample(rng, (ushort)rng.Next(0x10000));
            tiers[index < 0x10 ? 0 : index < 0x18 ? 1 : index < 0x20 ? 2 : 3]++;
            Assert.InRange(index, 0, 0x21);
        }
        // 231/256, 25/256 * 206/256, 25/256 * 50/256 * 206/256, 25/256 * (50/256)^2
        Assert.InRange(tiers[0] / 200_000.0, 0.897, 0.908);
        Assert.InRange(tiers[1] / 200_000.0, 0.076, 0.081);
        Assert.InRange(tiers[2] / 200_000.0, 0.013, 0.019);
        Assert.InRange(tiers[3] / 200_000.0, 0.002, 0.006);
    }
}
