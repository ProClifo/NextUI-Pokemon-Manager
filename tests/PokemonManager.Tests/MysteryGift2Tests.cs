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
    public void AnItemWaitsAtThePokemonCenterUntilCollected()
    {
        var sav = Crystal(unlocked: true);
        var rng = new Random(1);
        int received = 0;
        while (received < 5)
        {
            var gift = MysteryGift2.Receive(sav, 10, rng);
            Assert.NotNull(gift);
            received++;
            if (!gift.Decoration)
            {
                // the item waits for the man on the Pokémon Center's 2nd floor, blocking the next gift
                Assert.True(MysteryGift2.ItemWaiting(sav));
                Assert.NotEqual(0, sav.Data[0xBE4]);
                Assert.Equal(sav.Data[0xBE4], sav.Data[0xBE2]);
                Assert.Null(MysteryGift2.Receive(sav, 10, rng));
                // collected in the game, then saved
                sav.Data[0xBE2] = sav.Data[0xBE4] = 0;
            }
            // the game counts each one
            Assert.Equal(received, sav.Data[0xBE5]);
        }
        // five a day, the countdown started today
        Assert.Equal(new byte[] { 1, 10 }, sav.Data.Slice(0xBFA, 2).ToArray());
        Assert.Equal(0, MysteryGift2.Left(sav, 10));
        Assert.Null(MysteryGift2.Receive(sav, 10, rng));
        Assert.Equal(5, MysteryGift2.Left(sav, 11));
        // wCurDay wraps at 140
        Assert.Equal(0, MysteryGift2.Left(sav, 150));
        // the next day starts a new count
        Assert.NotNull(MysteryGift2.Receive(sav, 11, rng));
        Assert.Equal(1, sav.Data[0xBE5]);
        Assert.Equal(11, sav.Data[0xBFB]);
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
            for (int i = 0; i < 5; i++)
            {
                var gift = MysteryGift2.Receive(sav, day, rng)!;
                if (gift.Decoration)
                {
                    Assert.False(MysteryGift2.ItemWaiting(sav)); // a decoration doesn't wait anywhere
                    decorations.Add(gift.Name);
                }
                else
                    items++;
                sav.Data[0xBE2] = sav.Data[0xBE4] = 0;
            }
        }
        // each decoration once (later ones become items); about half at first
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
