using System.Buffers.Binary;
using PKHeX.Core;
using PokemonManager.Core;
using PokemonManager.Gen3;
using Xunit;

namespace PokemonManager.Tests;

/// <summary>
/// Builds synthetic event files in the wc-tool/WC3-plugin layouts and checks that injection lands where
/// PKHeX's own Gen 3 structures (and the decomp flag numbers) expect it.
/// </summary>
public sealed class Gen3EventTests : IDisposable
{
    // Flag numbers from pret's decompilations.
    private const int FlagMysteryGiftEmerald = 0x8DB;   // FLAG_SYS_MYSTERY_GIFT_ENABLE
    private const int FlagMysteryEventEmerald = 0x8AC;  // FLAG_SYS_MYSTERY_EVENT_ENABLE
    private const int FlagMysteryGiftFRLG = 0x839;      // FLAG_SYS_MYSTERY_GIFT_ENABLED
    private const int FlagMysteryEventRS = 0x84C;       // FLAG_SYS_EXDATA_ENABLE

    private readonly TestSaves _saves = new();

    public void Dispose() => _saves.Dispose();

    private static byte[] MakeScript(bool rubySapphire)
    {
        var script = new byte[Gen3Events.ScriptSize];
        script[4] = 0x33;   // RAM_SCRIPT_MAGIC
        script[5] = 0xFF;   // MAP_UNDEFINED group
        script[6] = 0xFF;   // MAP_UNDEFINED num
        script[7] = 0xFF;   // no object
        script[8] = 0x02;   // "end"
        uint chk = rubySapphire ? Gen3Events.ByteSum(script.AsSpan(4)) : Gen3Events.Crc16(script.AsSpan(4));
        BinaryPrimitives.WriteUInt32LittleEndian(script, chk);
        return script;
    }

    private static byte[] MakeWonderCard(string title, bool japanese = false)
    {
        int cardSize = japanese ? Gen3Events.CardSizeJP : Gen3Events.CardSize;
        var card = new WonderCard3(new byte[cardSize]) { CardID = 1000, Title = title };
        card.FixChecksum();

        var file = new byte[japanese ? Gen3Events.WC3SizeJP : Gen3Events.WC3Size];
        card.Data.CopyTo(file);
        BinaryPrimitives.WriteUInt16LittleEndian(file.AsSpan(cardSize + 0xA), (ushort)Species.Jirachi);
        MakeScript(false).CopyTo(file.AsSpan(file.Length - Gen3Events.ScriptSize));
        return file;
    }

    private Gen3EventFile WriteFile(string name, byte[] data)
    {
        var path = Path.Combine(_saves.Dir, name);
        File.WriteAllBytes(path, data);
        return Gen3Events.Parse(path) ?? throw new InvalidOperationException("not parsed");
    }

    [Fact]
    public void CrcMatchesPkhex()
    {
        var script = new MysteryEvent3(MakeScript(false));
        Assert.True(script.IsChecksumValid());
    }

    [Theory]
    [InlineData(GameVersion.E, FlagMysteryGiftEmerald)]
    [InlineData(GameVersion.FR, FlagMysteryGiftFRLG)]
    public void WonderCardLandsInTheRightPlace(GameVersion version, int giftFlag)
    {
        var entry = _saves.Create(version, $"{version}.sav");
        var sav = (SAV3)entry.Sav;
        Assert.False(sav.GetEventFlag(giftFlag));

        var file = WriteFile("aurora.wc3", MakeWonderCard("AURORA TEST"));
        Assert.Equal(Gen3EventKind.WonderCard, file.Kind);
        Assert.Contains("AURORA TEST", file.DisplayName);

        var result = Gen3Events.Inject(sav, file);
        Assert.True(result.Ok, result.Message);

        entry = _saves.Roundtrip(entry);
        sav = (SAV3)entry.Sav;
        var card = sav.LargeBlock switch
        {
            SaveBlock3LargeE e => e.GetWonderCard(false),
            SaveBlock3LargeFRLG f => f.GetWonderCard(false),
            _ => throw new InvalidOperationException(),
        };
        Assert.True(card.IsChecksumValid());
        Assert.Equal("AURORA TEST", card.Title);
        Assert.Equal(1000, card.CardID);
        Assert.True(sav.GetEventFlag(giftFlag), "Mystery Gift should be unlocked");
        Assert.True(sav.LargeBlock.MysteryData.IsChecksumValid(), "event script should be in the RAM script slot");
        Assert.Contains("AURORA TEST", Gen3Events.Status(sav));
    }

    [Fact]
    public void WonderCardRejectedByRubySapphireAndWrongRegion()
    {
        var rs = (SAV3)_saves.Create(GameVersion.R, "Ruby.sav").Sav;
        var file = WriteFile("card.wc3", MakeWonderCard("TEST"));
        Assert.False(Gen3Events.IsApplicable(rs, file));
        Assert.False(Gen3Events.Inject(rs, file).Ok);

        var jp = (SAV3)_saves.Create(GameVersion.E, "EmeraldJP.sav", "サトシ", LanguageID.Japanese).Sav;
        Assert.True(jp.Japanese);
        Assert.False(Gen3Events.Inject(jp, file).Ok);
        var jpFile = WriteFile("card-jp.wc3", MakeWonderCard("テスト", japanese: true));
        Assert.True(jpFile.Japanese);
        Assert.True(Gen3Events.Inject(jp, jpFile).Ok);
    }

    [Fact]
    public void StaleChecksumsAreRecalculatedLikeThePlugin()
    {
        var entry = _saves.Create(GameVersion.E, "Emerald.sav");
        var data = MakeWonderCard("EDITED");
        data[0x20] ^= 0x01; // hand-edited text, old CRC
        data[^1] ^= 0x01;   // and an edited script

        var result = Gen3Events.Inject((SAV3)entry.Sav, WriteFile("edited.wc3", data));
        Assert.True(result.Ok, result.Message);
        Assert.Contains("Recalculated", result.Message);

        entry = _saves.Roundtrip(entry);
        var sav = (SAV3)entry.Sav;
        Assert.True(((SaveBlock3LargeE)sav.LargeBlock).GetWonderCard(false).IsChecksumValid());
        Assert.True(sav.LargeBlock.MysteryData.IsChecksumValid());
    }

    [Fact]
    public void WonderCardWithoutFlagIsRefused()
    {
        var sav = (SAV3)_saves.Create(GameVersion.E, "Emerald.sav").Sav;
        var data = MakeWonderCard("NO FLAG");
        data[4] = data[5] = 0;
        Assert.False(Gen3Events.Inject(sav, WriteFile("noflag.wc3", data)).Ok);
    }

    [Fact]
    public void LinkStatCardKeepsItsMetadata()
    {
        var sav = (SAV3)_saves.Create(GameVersion.FR, "FireRed.sav").Sav;
        var data = MakeWonderCard("LINK CARD");
        var card = new WonderCard3(data.AsMemory(0, Gen3Events.CardSize)) { Type = 2 };
        card.FixChecksum();
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(Gen3Events.CardSize + 4), 12); // wins

        Assert.True(Gen3Events.Inject(sav, WriteFile("link.wc3", data)).Ok);
        Assert.Equal(12, ((SaveBlock3LargeFRLG)sav.LargeBlock).GetWonderCardExtra(false).Wins);
    }

    [Fact]
    public void WonderNewsInjects()
    {
        var entry = _saves.Create(GameVersion.E, "Emerald.sav");
        var news = new WonderNews3(new byte[Gen3Events.WN3Size]) { NewsID = 7, Title = "NEWS" };
        news.FixChecksum();
        var result = Gen3Events.Inject((SAV3)entry.Sav, WriteFile("news.wn3", news.Data.ToArray()));
        Assert.True(result.Ok, result.Message);

        entry = _saves.Roundtrip(entry);
        var saved = ((SaveBlock3LargeE)((SAV3)entry.Sav).LargeBlock).GetWonderNews(false);
        Assert.Equal("NEWS", saved.Title);
        Assert.True(saved.IsChecksumValid());
    }

    [Fact]
    public void RubySapphireMysteryEventSetsTheEventFlag()
    {
        var entry = _saves.Create(GameVersion.R, "Ruby.sav");
        var me3 = new byte[Gen3Events.ME3Size];
        MakeScript(rubySapphire: true).CopyTo(me3, 0);

        var file = WriteFile("eon.me3", me3);
        var result = Gen3Events.Inject((SAV3)entry.Sav, file);
        Assert.True(result.Ok, result.Message);

        entry = _saves.Roundtrip(entry);
        var sav = (SAV3)entry.Sav;
        Assert.True(sav.GetEventFlag(FlagMysteryEventRS));
        Assert.Equal(0x33, sav.LargeBlock.MysteryData.Data[4]);

        // The same file is refused by Emerald (different checksum algorithm)...
        var em = (SAV3)_saves.Create(GameVersion.E, "Emerald.sav").Sav;
        Assert.False(Gen3Events.Inject(em, file).Ok);
        // ...and FireRed has no Mystery Events at all.
        var fr = (SAV3)_saves.Create(GameVersion.FR, "FireRed.sav").Sav;
        Assert.False(Gen3Events.Inject(fr, file).Ok);
    }

    [Fact]
    public void MysteryEventSharesItsRecordMixingItemAndClearsTheWonderCard()
    {
        var entry = _saves.Create(GameVersion.E, "Emerald.sav");
        var sav = (SAV3)entry.Sav;
        Assert.True(Gen3Events.Inject(sav, WriteFile("card.wc3", MakeWonderCard("OLD CARD"))).Ok);

        var me3 = new byte[Gen3Events.ME3Size];
        MakeScript(rubySapphire: false).CopyTo(me3, 0);
        var gift = new RecordMixing3Gift(me3.AsMemory(Gen3Events.ScriptSize)) { Max = 1, Count = 0x97, Item = 275 };
        // left with a stale checksum on purpose

        var result = Gen3Events.Inject(sav, WriteFile("eon.me3", me3));
        Assert.True(result.Ok, result.Message);
        Assert.Contains("Eon Ticket", result.Message);

        entry = _saves.Roundtrip(entry);
        var block = (SaveBlock3LargeE)((SAV3)entry.Sav).LargeBlock;
        Assert.Equal(275, block.RecordMixingGift.Item);
        Assert.True(block.RecordMixingGift.IsChecksumValid());
        Assert.Equal(0, block.GetWonderCard(false).CardID);
    }

    [Fact]
    public void ScriptOnlyMysteryEventIsAccepted()
    {
        var sav = (SAV3)_saves.Create(GameVersion.S, "Sapphire.sav").Sav;
        var file = WriteFile("script.me3", MakeScript(rubySapphire: true));
        Assert.Equal(Gen3EventKind.MysteryEvent, file.Kind);
        Assert.True(Gen3Events.Inject(sav, file).Ok);
        Assert.Equal(0x33, sav.LargeBlock.MysteryData.Data[4]);
    }

    [Fact]
    public void EmeraldMysteryEventNeverSetsTheFlagOnInternationalSaves()
    {
        var me3 = new byte[Gen3Events.ME3Size];
        MakeScript(rubySapphire: false).CopyTo(me3, 0);
        var file = WriteFile("event.me3", me3);

        var usa = (SAV3)_saves.Create(GameVersion.E, "Emerald.sav").Sav;
        Assert.True(Gen3Events.Inject(usa, file).Ok);
        Assert.False(usa.GetEventFlag(FlagMysteryEventEmerald));

        var jpn = (SAV3)_saves.Create(GameVersion.E, "EmeraldJP.sav", "サトシ", LanguageID.Japanese).Sav;
        Assert.True(Gen3Events.Inject(jpn, file).Ok);
        Assert.True(jpn.GetEventFlag(FlagMysteryEventEmerald));
    }

    [Theory]
    [InlineData(GameVersion.R, 0x498)]
    [InlineData(GameVersion.E, 0xBEC)]
    [InlineData(GameVersion.FR, 0x4A0)]
    public void ECardTrainerInjects(GameVersion version, int offset)
    {
        var entry = _saves.Create(version, $"{version}.sav");
        var ect = new byte[Gen3Events.ECTSize];
        for (int i = 0; i < ect.Length - 4; i++)
            ect[i] = (byte)(i * 7);
        BinaryPrimitives.WriteUInt32LittleEndian(ect.AsSpan(^4), Gen3Events.WordSum(ect.AsSpan(0, ect.Length - 4)));

        Assert.True(Gen3Events.Inject((SAV3)entry.Sav, WriteFile("trainer.ect", ect)).Ok);
        entry = _saves.Roundtrip(entry);
        Assert.Equal(ect, ((SAV3)entry.Sav).Small.Slice(offset, ect.Length).ToArray());
    }

    [Fact]
    public void ECardBerryShowsUpAsPkhexEnigmaBerry()
    {
        var entry = _saves.Create(GameVersion.S, "Sapphire.sav");
        Assert.True(((SAV3)entry.Sav).IsEBerryEngima);

        var berry = new byte[Gen3Events.ECBSizeRS];
        StringConverter3.SetString(berry.AsSpan(0, 7), "PUMKIN", 6, false, StringConverterOption.ClearFF);
        berry[0xA] = 3; // max yield
        BinaryPrimitives.WriteUInt32LittleEndian(berry.AsSpan(^4), Gen3Events.BerryChecksum(berry.AsSpan(0, berry.Length - 4)));

        var file = WriteFile("pumkin.ecb", berry);
        Assert.Contains("PUMKIN", file.DisplayName);
        Assert.True(Gen3Events.Inject((SAV3)entry.Sav, file).Ok);

        entry = _saves.Roundtrip(entry);
        var sav = (SAV3)entry.Sav;
        Assert.False(sav.IsEBerryEngima);
        Assert.Equal("PUMKIN", sav.EBerryName);
        Assert.Equal(1, sav.GetWork(0x2D)); // VAR_ENIGMA_BERRY_AVAILABLE (0x402D)

        var em = (SAV3)_saves.Create(GameVersion.E, "Emerald.sav").Sav;
        Assert.False(Gen3Events.IsApplicable(em, file));
        Assert.False(Gen3Events.Inject(em, file).Ok);
    }

    [Theory]
    [InlineData(GameVersion.E, 0x2D)]
    [InlineData(GameVersion.LG, 0x33)]
    public void SmallFormatBerryForEmeraldAndFireRedLeafGreen(GameVersion version, int availableVar)
    {
        var entry = _saves.Create(version, $"{version}.sav");
        var berry = new byte[Gen3Events.ECBSizeFRLGE];
        StringConverter3.SetString(berry.AsSpan(0, 7), "DRASH", 6, false, StringConverterOption.ClearFF);
        berry[0xA] = 2;
        BinaryPrimitives.WriteUInt32LittleEndian(berry.AsSpan(48), Gen3Events.ByteSum(berry.AsSpan(0, 48)));

        var file = WriteFile("drash.ecb", berry);
        Assert.True(Gen3Events.IsApplicable((SAV3)entry.Sav, file));
        Assert.True(Gen3Events.Inject((SAV3)entry.Sav, file).Ok);

        entry = _saves.Roundtrip(entry);
        var sav = (SAV3)entry.Sav;
        Assert.Equal("DRASH", sav.EBerryName);
        Assert.Equal(1, sav.GetWork(availableVar));

        var rs = (SAV3)_saves.Create(GameVersion.R, "Ruby.sav").Sav;
        Assert.False(Gen3Events.Inject(rs, file).Ok);
    }

    [Fact]
    public void GiftFolderListingFiltersByGame()
    {
        var giftDir = Path.Combine(_saves.Dir, "Gifts");
        Directory.CreateDirectory(giftDir);
        File.WriteAllBytes(Path.Combine(giftDir, "card.wc3"), MakeWonderCard("CARD"));
        File.WriteAllBytes(Path.Combine(giftDir, "junk.wc3"), new byte[100]);
        var me3 = new byte[Gen3Events.ME3Size];
        MakeScript(true).CopyTo(me3, 0);
        File.WriteAllBytes(Path.Combine(giftDir, "event.me3"), me3);

        var em = _saves.Create(GameVersion.E, "Emerald.sav").Sav;
        var rs = _saves.Create(GameVersion.R, "Ruby.sav").Sav;
        var pt = _saves.Create(GameVersion.Pt, "Platinum.sav").Sav;

        Assert.Equal(2, GiftService.ListFiles(giftDir, em).Count); // card + me3 (checked on inject)
        Assert.Single(GiftService.ListFiles(giftDir, rs));         // me3 only
        Assert.Empty(GiftService.ListFiles(giftDir, pt));
    }
}
