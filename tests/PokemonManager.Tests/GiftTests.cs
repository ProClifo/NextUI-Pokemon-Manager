using PKHeX.Core;
using PokemonManager.Core;
using Xunit;

namespace PokemonManager.Tests;

public sealed class GiftTests : IDisposable
{
    private readonly TestSaves _saves = new();

    public void Dispose() => _saves.Dispose();

    private static T FirstPokemonGift<T>(IEnumerable<T> db) where T : MysteryGift
        => db.First(g => g.IsEntity && !g.IsEgg && g.Species != 0);

    [Fact]
    public void Gen4CardGoesIntoAlbumAndDeliveryQueue()
    {
        var entry = _saves.Create(GameVersion.Pt, "Platinum.sav");
        var pcd = FirstPokemonGift(EncounterEvent.MGDB_G4);

        // Mystery Gift must be unlocked in-game first (the Jubilife TV questionnaire).
        var locked = GiftService.InjectCard(entry.Sav, pcd);
        Assert.False(locked.Ok);
        Assert.Contains("Jubilife TV", locked.Message);
        ((SAV4)entry.Sav).IsMysteryGiftUnlocked = true;

        var result = GiftService.InjectCard(entry.Sav, pcd);
        Assert.True(result.Ok, result.Message);

        entry = _saves.Roundtrip(entry);
        IMysteryGiftStorage storage = ((IMysteryGiftStorageProvider)entry.Sav).MysteryGiftStorage;
        var gifts = Enumerable.Range(0, storage.GiftCountMax).Select(storage.GetMysteryGift).ToList();
        Assert.Contains(gifts, g => g is PCD p && !p.IsEmpty && p.CardID == pcd.CardID);
        Assert.Contains(gifts, g => g is PGT p && !p.IsEmpty);
        Assert.True(((MysteryBlock4)storage).IsDeliveryManActive);
    }

    [Fact]
    public void Gen5CardSurvivesReEncryption()
    {
        var entry = _saves.Create(GameVersion.B2, "Black2.sav");
        var pgf = FirstPokemonGift(EncounterEvent.MGDB_G5);

        var injected = GiftService.InjectCard(entry.Sav, pgf);
        Assert.True(injected.Ok, injected.Message);
        entry = _saves.Roundtrip(entry);

        var storage = ((IMysteryGiftStorageProvider)entry.Sav).MysteryGiftStorage;
        var first = storage.GetMysteryGift(0);
        Assert.Equal(pgf.CardID, first.CardID);
        Assert.Equal(pgf.Species, first.Species);
    }

    [Fact]
    public void Gen6And7CardsInject()
    {
        var xy = _saves.Create(GameVersion.X, "X.sav");
        Assert.True(GiftService.InjectCard(xy.Sav, FirstPokemonGift(EncounterEvent.MGDB_G6)).Ok);
        var sm = _saves.Create(GameVersion.SN, "Sun.sav");
        Assert.True(GiftService.InjectCard(sm.Sav, FirstPokemonGift(EncounterEvent.MGDB_G7)).Ok);
    }

    [Fact]
    public void GiftFromFileRedeemsToPc()
    {
        var entry = _saves.Create(GameVersion.SW, "Sword.sav");
        // HOME gifts are only legal with a HOME tracker, which only HOME itself can add.
        var wc8 = FirstPokemonGift(EncounterEvent.MGDB_G8.Where(g => !g.IsHOMEGift));
        var path = Path.Combine(_saves.Dir, "gift.wc8");
        File.WriteAllBytes(path, wc8.Data.ToArray());

        var files = GiftService.ListFiles(_saves.Dir, entry.Sav);
        var gift = Assert.Single(files).Gift!;
        Assert.False(GiftService.SupportsAlbum(entry.Sav));

        var result = GiftService.Redeem(entry.Sav, gift);
        Assert.True(result.Ok, result.Message);
        entry = _saves.Roundtrip(entry);
        Assert.Equal(wc8.Species, new SlotRef(0, 0).Get(entry.Sav).Species);
    }

    [Theory]
    [InlineData(GameVersion.RD)]
    [InlineData(GameVersion.C)]
    [InlineData(GameVersion.E)]
    [InlineData(GameVersion.FR)]
    [InlineData(GameVersion.Pt)]
    [InlineData(GameVersion.W2)]
    public void BuiltInEventLibraryRedeems(GameVersion version)
    {
        // A new game with just the Pokédex: distributions needed no National Pokédex or other progress.
        var entry = _saves.Create(version, $"{version}.sav");
        var events = GiftService.BuiltInEvents(entry.Sav);
        Assert.NotEmpty(events);

        var ev = events.First(e => e.Encounter is not MysteryGift { IsEntity: false });
        Assert.Contains("until you've received the Pokédex", GiftService.Redeem(entry.Sav, ev.Encounter).Message);
        Assert.False(TradeRules.HasPokedex(entry.Sav));
        TestSaves.GivePokedex(entry.Sav);
        Assert.True(TradeRules.HasPokedex(entry.Sav));
        var result = GiftService.Redeem(entry.Sav, ev.Encounter);
        Assert.True(result.Ok, $"{ev.Name}: {result.Message}");
        entry = _saves.Roundtrip(entry);
        Assert.Equal(ev.Encounter.Species, new SlotRef(0, 0).Get(entry.Sav).Species);
    }

    [Theory]
    [InlineData(GameVersion.E, Species.Celebi)]   // outside the Hoenn Pokédex
    [InlineData(GameVersion.LG, Species.Jirachi)] // outside the Kanto Pokédex
    public void DistributionsDontNeedTheNationalDex(GameVersion version, Species species)
    {
        var entry = _saves.Create(version, $"{version}.sav");
        TestSaves.GivePokedex(entry.Sav);
        var ev = GiftService.BuiltInEvents(entry.Sav).First(e => e.Encounter.Species == (ushort)species);
        Assert.False(TradeRules.CheckReceive(TestSaves.Make(entry.Sav, species, 5), entry.Sav).Ok); // a trade would need it
        var result = GiftService.Redeem(entry.Sav, ev.Encounter);
        Assert.True(result.Ok, result.Message);
        Assert.Equal((ushort)species, new SlotRef(0, 0).Get(entry.Sav).Species);
    }

    [Theory]
    [InlineData(LanguageID.English)]
    [InlineData(LanguageID.Japanese)]
    [InlineData(LanguageID.German)]
    public void Gen1MewIsLegalForTheSavesLanguage(LanguageID language)
    {
        var sav = _saves.Create(GameVersion.RD, $"Red{language}.sav", language: language).Sav;
        for (int i = 0; i < 5; i++)
        {
            var mew = GiftService.Mew(sav);
            Assert.NotNull(mew);
            Assert.Equal((ushort)Species.Mew, mew.Species);
            Assert.True(Legality.IsLegal(mew, sav), Legality.FirstProblem(mew));
        }
    }

    [Theory]
    [InlineData(GameVersion.RD)]
    [InlineData(GameVersion.C)]
    [InlineData(GameVersion.E)]
    [InlineData(GameVersion.Pt)]
    [InlineData(GameVersion.W2)]
    public void DistributionsGoToThePcNeverTheParty(GameVersion version)
    {
        var entry = _saves.Create(version, $"{version}.sav");
        var sav = TestSaves.GivePokedex(entry.Sav);
        var ev = GiftService.BuiltInEvents(sav).First(e => e.Encounter is not MysteryGift { IsEntity: false });

        sav.SetPartySlotAtIndex(TestSaves.Make(sav, Species.Pikachu, 5), 0);
        var result = GiftService.Redeem(sav, ev.Encounter);
        Assert.True(result.Ok, result.Message);
        Assert.Equal(ev.Encounter.Species, new SlotRef(0, 0).Get(sav).Species);
        Assert.Equal(1, sav.PartyCount);
    }

    [Theory]
    [InlineData(GameVersion.GD, LanguageID.English)]
    [InlineData(GameVersion.C, LanguageID.English)]
    [InlineData(GameVersion.SI, LanguageID.Korean)]
    public void Gen2CelebiAndKoreanMewAreLegal(GameVersion version, LanguageID language)
    {
        var sav = _saves.Create(version, $"{version}{language}.sav", language: language).Sav;
        var celebi = GiftService.Celebi(sav);
        Assert.NotNull(celebi);
        Assert.True(Legality.IsLegal(celebi, sav), Legality.FirstProblem(celebi));
        if (language == LanguageID.Korean)
        {
            var mew = GiftService.Mew(sav);
            Assert.NotNull(mew);
            Assert.Equal((ushort)Species.Mew, mew.Species);
            Assert.True(Legality.IsLegal(mew, sav), Legality.FirstProblem(mew));
        }
    }

    [Fact]
    public void InjectMessagesAreShort()
    {
        var sav = _saves.Create(GameVersion.E, "Emerald.sav").Sav;
        var mew = TestSaves.Make(sav, Species.Mew, 10);
        Assert.Equal("Inject Mew (Lv. 10) into Emerald?", Names.InjectQuestion(mew, sav));
        Assert.Equal("MEW has arrived in your PC!", Names.Arrived(mew));

        mew.IsEgg = true;
        Assert.Equal("Inject Mew Egg into Emerald?", Names.InjectQuestion(mew, sav));
        Assert.Equal("The EGG has arrived in your PC!", Names.Arrived(mew));
    }
}
