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
        var gifts = MysteryGift2.Receive(sav, 10, new Random(1));
        Assert.NotNull(gifts);
        Assert.Equal(5, gifts.Count);
        var bag = sav.Inventory;
        var names = GameInfo.GetStrings("en").GetItemStrings(EntityContext.Gen2);
        foreach (var gift in gifts.Where(g => !g.Decoration))
            Assert.Contains(bag.Pouches, p => p.Items.Any(i => i.Count > 0 && names[i.Index] == gift.Name));
        // the game sees today's five, with the countdown started today
        Assert.Equal(5, sav.Data[0xBE5]);
        Assert.Equal(new byte[] { 1, 10 }, sav.Data.Slice(0xBFA, 2).ToArray());
        Assert.Equal(0, MysteryGift2.Left(sav, 10));
        Assert.Equal(5, MysteryGift2.Left(sav, 11));
        // wCurDay wraps at 140
        Assert.Equal(0, MysteryGift2.Left(sav, 150));
    }

    [Fact]
    public void HalfTheGiftsAreDecorationsUntilTheyreAllReceived()
    {
        var sav = Crystal(unlocked: true);
        var rng = new Random(3);
        var decorations = new List<string>();
        int items = 0;
        for (int day = 0; day < 60; day++)
        {
            var gifts = MysteryGift2.Receive(sav, day, rng)!;
            Assert.Equal(5, gifts.Count);
            decorations.AddRange(gifts.Where(g => g.Decoration).Select(g => g.Name));
            items += gifts.Count(g => !g.Decoration);
            // the bag would fill up: empty it each day
            var bag = sav.Inventory;
            foreach (var pouch in bag.Pouches)
                foreach (var i in pouch.Items)
                    i.Clear();
            bag.CopyTo(sav);
        }
        // each decoration once (later ones become items); the first day's are about half
        Assert.Equal(decorations.Count, decorations.Distinct().Count());
        Assert.InRange(decorations.Count, 10, 34);
        Assert.DoesNotContain("Pikachu Bed", decorations);
        Assert.Equal(300, decorations.Count + items);
        // the game's received flags: Jigglypuff Doll is DECOFLAG 22, byte 2 bit 6
        if (decorations.Contains("Jigglypuff Doll"))
            Assert.Equal(0x40, sav.Data[0xBF0 + 2] & 0x40);
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
