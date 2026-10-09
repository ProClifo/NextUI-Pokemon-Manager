using PKHeX.Core;
using PokemonManager.Core;
using Xunit;

namespace PokemonManager.Tests;

public sealed class TradeRulesTests : IDisposable
{
    private readonly TestSaves _saves = new();

    public void Dispose() => _saves.Dispose();

    private SaveFile Save(GameVersion version) => _saves.Create(version, $"{version}.sav").Sav;

    [Theory]
    [InlineData(GameVersion.RD, GameVersion.C, true)]   // Time Capsule
    [InlineData(GameVersion.C, GameVersion.RD, true)]
    [InlineData(GameVersion.C, GameVersion.E, false)]   // Gen 1/2 never reach Gen 3
    [InlineData(GameVersion.RD, GameVersion.Pt, false)]
    [InlineData(GameVersion.E, GameVersion.Pt, true)]   // Pal Park
    [InlineData(GameVersion.Pt, GameVersion.E, false)]
    [InlineData(GameVersion.Pt, GameVersion.B2, true)]  // Poké Transfer
    [InlineData(GameVersion.B2, GameVersion.Pt, false)]
    [InlineData(GameVersion.E, GameVersion.B2, true)]   // by way of Gen 4
    public void OnlyTheGamesRoutesAreAllowed(GameVersion from, GameVersion to, bool allowed)
    {
        var source = Save(from);
        var dest = Save(to);
        foreach (var sav in new[] { source, dest })
        {
            switch (sav)
            {
                case SAV3 s3: s3.NationalDex = true; break;
                case SAV4 s4: s4.NationalDex = true; break;
                case SAV5 s5: s5.Zukan.IsNationalDexUnlocked = true; break;
            }
        }
        var pk = TestSaves.Make(source, Species.Pikachu, 20);
        var result = TradeRules.CheckTransfer(pk, source, dest);
        Assert.True(allowed == result.Ok, result.Message);
    }

    [Fact]
    public void PalParkRefusesHmMoves()
    {
        var em = (SAV3)Save(GameVersion.E);
        var pt = (SAV4)Save(GameVersion.Pt);
        pt.NationalDex = true;
        var mudkip = TestSaves.Make(em, Species.Mudkip, 20);
        mudkip.SetMove(1, (ushort)Move.Surf);
        Assert.Contains("Surf", TradeRules.CheckTransfer(mudkip, em, pt).Message);
        mudkip.SetMove(1, (ushort)Move.WaterGun);
        Assert.True(TradeRules.CheckTransfer(mudkip, em, pt).Ok);
    }

    [Fact]
    public void FireRedNeedsCelioAndEmeraldNeedsToBeChampion()
    {
        var fr = (SAV3)Save(GameVersion.FR);
        var em = (SAV3)Save(GameVersion.E);
        var pk = TestSaves.Make(fr, Species.Machoke, 30);

        Assert.Contains("Celio", TradeRules.CheckTransfer(pk, fr, em).Message);
        fr.SetEventFlag(0x844, true); // FLAG_SYS_CAN_LINK_WITH_RS
        Assert.Contains("Champion", TradeRules.CheckTransfer(pk, fr, em).Message);
        em.SetEventFlag(0x87F, true); // FLAG_IS_CHAMPION
        Assert.True(TradeRules.CheckTransfer(pk, fr, em).Ok);

        // Ruby/Sapphire have no requirement of their own; only FireRed's applies.
        var ruby = (SAV3)Save(GameVersion.R);
        fr.SetEventFlag(0x844, false);
        Assert.False(TradeRules.CheckTransfer(pk, ruby, fr).Ok);
        fr.SetEventFlag(0x844, true);
        Assert.True(TradeRules.CheckTransfer(pk, ruby, fr).Ok);
    }

    [Fact]
    public void RegionalDexLimitsTradesUntilTheNationalDex()
    {
        var fr = (SAV3)Save(GameVersion.FR);
        var em = (SAV3)Save(GameVersion.E);
        fr.SetEventFlag(0x844, true);
        em.SetEventFlag(0x87F, true);

        var bulbasaur = TestSaves.Make(fr, Species.Bulbasaur, 10); // Kanto, but not in the Hoenn Pokédex
        Assert.Contains("can't receive Bulbasaur", TradeRules.CheckTransfer(bulbasaur, fr, em).Message);
        em.NationalDex = true;
        Assert.True(TradeRules.CheckTransfer(bulbasaur, fr, em).Ok);

        var treecko = TestSaves.Make(em, Species.Treecko, 10); // Hoenn, beyond FireRed's Kanto Pokédex
        Assert.Contains("can't receive Treecko", TradeRules.CheckTransfer(treecko, em, fr).Message);
        fr.NationalDex = true;
        Assert.True(TradeRules.CheckTransfer(treecko, em, fr).Ok);

        // Gifts and imports follow the same rule.
        var fresh = (SAV3)Save(GameVersion.LG);
        Assert.False(TradeRules.CheckReceive(TestSaves.Make(fresh, Species.Jirachi, 5), fresh).Ok);
        Assert.True(TradeRules.CheckReceive(TestSaves.Make(fresh, Species.Mew, 5), fresh).Ok);
    }

    [Fact]
    public void MewAndDeoxysMustBeFromAnEvent()
    {
        var em = (SAV3)Save(GameVersion.E);
        var ruby = (SAV3)Save(GameVersion.R);
        em.NationalDex = true;
        var mew = TestSaves.Make(ruby, Species.Mew, 30);
        Assert.Contains("official event", TradeRules.CheckTransfer(mew, ruby, em).Message);
        mew.FatefulEncounter = true;
        Assert.True(TradeRules.CheckTransfer(mew, ruby, em).Ok);
    }
}
