using PKHeX.Core;
using PokemonManager.Core;
using Xunit;

namespace PokemonManager.Tests;

public sealed class TransferTests : IDisposable
{
    private readonly TestSaves _saves = new();

    public void Dispose() => _saves.Dispose();

    [Fact]
    public void MovesBetweenGen3Games()
    {
        var fr = _saves.Create(GameVersion.FR, "FireRed.sav");
        var em = _saves.Create(GameVersion.E, "Emerald.sav", "MAY");
        new SlotRef(0, 0).Set(fr.Sav, TestSaves.Make(fr.Sav, Species.Machoke));
        // FireRed can link with Emerald once the Sapphire is with Celio, and Emerald once it's Champion.
        ((SAV3)fr.Sav).SetEventFlag(0x844, true);
        ((SAV3)em.Sav).SetEventFlag(0x87F, true);
        fr = _saves.Roundtrip(fr);

        var result = TransferService.Transfer(fr, new SlotRef(0, 0), em, TransferMode.Move, false, out var placed);
        Assert.True(result.Ok, result.Message);
        _saves.Library.Write(em);
        _saves.Library.Write(fr);

        var emAfter = _saves.Reload(em.Path);
        var frAfter = _saves.Reload(fr.Path);
        Assert.Equal((ushort)Species.Machoke, placed.Get(emAfter.Sav).Species);
        Assert.Equal("ASH", placed.Get(emAfter.Sav).OriginalTrainerName);
        Assert.Equal(0, new SlotRef(0, 0).Get(frAfter.Sav).Species);
    }

    [Fact]
    public void ConvertsGen3ToGen4()
    {
        var em = _saves.Create(GameVersion.E, "Emerald.sav");
        var hg = _saves.Create(GameVersion.HG, "HeartGold.sav");
        TestSaves.Progress(hg.Sav); // Pal Park needs the National Pokédex
        new SlotRef(1, 4).Set(em.Sav, TestSaves.Make(em.Sav, Species.Mudkip, 12));

        var result = TransferService.Transfer(em, new SlotRef(1, 4), hg, TransferMode.Copy, false, out var placed);
        Assert.True(result.Ok, result.Message);
        hg = _saves.Roundtrip(hg);

        var pk = placed.Get(hg.Sav);
        Assert.IsType<PK4>(pk);
        Assert.Equal((ushort)Species.Mudkip, pk.Species);
        Assert.Equal((ushort)Species.Mudkip, new SlotRef(1, 4).Get(em.Sav).Species); // copy keeps original
    }

    [Fact]
    public void RefusesBackwardsTransferUnlessUnofficialAllowed()
    {
        var pt = _saves.Create(GameVersion.Pt, "Platinum.sav");
        var em = _saves.Create(GameVersion.E, "Emerald.sav");
        new SlotRef(0, 0).Set(pt.Sav, TestSaves.Make(pt.Sav, Species.Bulbasaur, 10));

        var refused = TransferService.Transfer(pt, new SlotRef(0, 0), em, TransferMode.Move, false, out _);
        Assert.False(refused.Ok);
        Assert.Contains("can't go back from Gen 4 to Gen 3", refused.Message);

        var forced = TransferService.Transfer(pt, new SlotRef(0, 0), em, TransferMode.Move, true, out var placed);
        Assert.True(forced.Ok, forced.Message);
        Assert.IsType<PK3>(placed.Get(em.Sav));
    }

    [Fact]
    public void RefusesSpeciesTheTargetGameLacks()
    {
        var pt = _saves.Create(GameVersion.Pt, "Platinum.sav");
        var hg = _saves.Create(GameVersion.HG, "HeartGold.sav");
        var em = _saves.Create(GameVersion.E, "Emerald.sav");
        new SlotRef(0, 0).Set(pt.Sav, TestSaves.Make(pt.Sav, Species.Lucario, 30));

        Assert.True(TransferService.Transfer(pt, new SlotRef(0, 0), hg, TransferMode.Copy, false, out _).Ok);
        Assert.False(TransferService.Transfer(pt, new SlotRef(0, 0), em, TransferMode.Copy, true, out _).Ok);
    }

    [Fact]
    public void TimeCapsuleBetweenGen1AndGen2()
    {
        var red = _saves.Create(GameVersion.RD, "Red.sav");
        var crystal = _saves.Create(GameVersion.C, "Crystal.sav", "GOLD");
        TestSaves.Progress(red.Sav); // the Cable Club needs the Pokédex
        new SlotRef(0, 0).Set(red.Sav, TestSaves.Make(red.Sav, Species.Kadabra, 25));

        var result = TransferService.Transfer(red, new SlotRef(0, 0), crystal, TransferMode.Move, false, out var placed);
        Assert.True(result.Ok, result.Message);
        var pk = placed.Get(crystal.Sav);
        Assert.IsType<PK2>(pk);
        Assert.Equal((ushort)Species.Kadabra, pk.Species);

        // Arriving by trade in the same era is what triggers the evolution.
        var evo = Assert.Single(TradeEvolution.GetOptions(pk));
        Assert.True(evo.ConditionsMet);
    }

    [Fact]
    public void WontMoveLastPartyMember()
    {
        var em = _saves.Create(GameVersion.E, "Emerald.sav");
        var fr = _saves.Create(GameVersion.FR, "FireRed.sav");
        em.Sav.SetPartySlotAtIndex(TestSaves.Make(em.Sav, Species.Treecko, 5), 0);

        var result = TransferService.Transfer(em, SlotRef.Party(0), fr, TransferMode.Move, false, out _);
        Assert.False(result.Ok);
    }

    [Fact]
    public void WritingMakesABackup()
    {
        var em = _saves.Create(GameVersion.E, "Emerald.sav");
        var before = File.ReadAllBytes(em.Path);
        new SlotRef(0, 0).Set(em.Sav, TestSaves.Make(em.Sav, Species.Zigzagoon, 3));

        var backup = _saves.Library.Write(em);

        Assert.Equal(before, File.ReadAllBytes(backup));
        Assert.NotEqual(before, File.ReadAllBytes(em.Path));
        Assert.Equal(0x20000, new FileInfo(em.Path).Length);
    }
}
