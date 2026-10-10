using PKHeX.Core;
using PokemonManager.Core;
using Xunit;

namespace PokemonManager.Tests;

public sealed class PartnerTradeTests : IDisposable
{
    private readonly TestSaves _saves = new();

    public void Dispose() => _saves.Dispose();

    [Fact]
    public void KarrablastAndShelmetSwapAndBothEvolve()
    {
        var black = _saves.Create(GameVersion.B, "Black.sav");
        var white = _saves.Create(GameVersion.W, "White.sav", trainer: "MISTY");
        var karrablast = TestSaves.Make(black.Sav, Species.Karrablast, 25);
        new SlotRef(0, 3).Set(black.Sav, karrablast);
        new SlotRef(2, 7).Set(white.Sav, TestSaves.Make(white.Sav, Species.Shelmet, 22));

        Assert.True(PartnerTrade.Applies(karrablast));
        Assert.Equal("No Shelmet to trade with!", ActionReasons.Evolve(karrablast));
        var partners = PartnerTrade.Find(karrablast, [white]);
        var partner = Assert.Single(partners);
        Assert.Null(ActionReasons.Evolve(karrablast, partnerAvailable: true));

        var result = PartnerTrade.Trade(black, new SlotRef(0, 3), partner, allowUnofficial: false);
        Assert.True(result.Ok, result.Message);

        // Each took the other's place, evolved.
        var accelgor = new SlotRef(0, 3).Get(black.Sav);
        var escavalier = new SlotRef(2, 7).Get(white.Sav);
        Assert.Equal((ushort)Species.Accelgor, accelgor.Species);
        Assert.Equal((ushort)Species.Escavalier, escavalier.Species);
        Assert.Equal("MISTY", accelgor.OriginalTrainerName);
        Assert.Equal("ASH", escavalier.OriginalTrainerName);
    }

    [Fact]
    public void APartnerHoldingAnEverstoneIsNotOffered()
    {
        var black = _saves.Create(GameVersion.B, "Black.sav");
        var white = _saves.Create(GameVersion.W, "White.sav");
        var everstone = (ushort)Array.IndexOf(GameInfo.Strings.GetItemStrings(EntityContext.Gen5), "Everstone");
        new SlotRef(0, 0).Set(white.Sav, TestSaves.Make(white.Sav, Species.Shelmet, 22, everstone));
        Assert.Empty(PartnerTrade.Find(TestSaves.Make(black.Sav, Species.Karrablast, 25), [white]));
    }
}
