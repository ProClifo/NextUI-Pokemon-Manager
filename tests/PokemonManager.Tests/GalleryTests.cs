using PKHeX.Core;
using PokemonManager.Core;
using Xunit;

namespace PokemonManager.Tests;

public sealed class GalleryTests : IDisposable
{
    private readonly TestSaves _saves = new();

    public void Dispose() => _saves.Dispose();

    [Theory]
    [InlineData("018 Pt - Item Member Card (ENG).wc4", 4, "Pt", "Item Member Card")]
    [InlineData("DPPtHGSS - Manaphy Egg.pgt", 4, "D,P,Pt,HG,SS", "Manaphy Egg")]
    [InlineData("0033 B [LW] - SPR2012 Zekrom (ENG).pgf", 5, "B", "SPR2012 Zekrom")]
    [InlineData("2046 BWB2W2 - Item Liberty Pass (ENG).pgf", 5, "B,W,B2,W2", "Item Liberty Pass")]
    [InlineData("08-A028 RS - Guitarist Dominic (USA).ect", 3, "R,S", "Guitarist Dominic (USA)")]
    [InlineData("FL - Item AuroraTicket (ENG) (UK).wc3", 3, "FR,LG", "Item AuroraTicket (UK)")]
    [InlineData("E - Item Old Sea Map (debug)(JPN).wc3", 3, "E", "Item Old Sea Map (debug)")]
    [InlineData("RSEFL - MYSTRY (118 of 430) Mew (BA7609E8) (ENG).pk3", 3, "R,S,E,FR,LG", "MYSTRY (118 of 430) Mew (BA7609E8)")]
    [InlineData("025 - ARENA PIKACHU - 2C70CB52AB54.pk3", 3, "", "ARENA PIKACHU - 2C70CB52AB54")]
    [InlineData("GSC - PCNYb 0224 Lovely Kiss Poliwag Egg (ENG).pk2", 2, "G,S,C", "PCNYb 0224 Lovely Kiss Poliwag Egg")]
    public void FileNamesGiveGamesAndTitle(string file, int gen, string games, string title)
    {
        var parsed = GalleryNames.Parse(file, gen);
        Assert.Equal(games, string.Join(',', parsed.Games));
        Assert.Equal(title, parsed.Title);
    }

    [Theory]
    [InlineData("Released/Gen 4/Wondercards/ENG/018 Pt - Item Member Card (ENG).wc4", "ENG")]
    [InlineData("Released/Gen 3/JPN/ポケパーク/Mew/RSEFL - x.pk3", "JPN")]
    [InlineData("Released/Gen 5/Dream World/Korean/PGL/BW - x.pk5", "KOR")]
    [InlineData("Released/Gen 1/Classic/International/Club Nintendo/RBY - x.pk1", "INT")]
    [InlineData("Unreleased/Gen 2/Debug GS Mons (JPN)/- ミュウ.pk2", "JPN")]
    [InlineData("Released/Gen 3/Pokemon Box/RSEFL - Surf Pichu Egg.pk3", null)]
    public void LanguageComesFromNameThenFolder(string path, string? language)
        => Assert.Equal(language, GalleryNames.LanguageFromPath(path));

    [Fact]
    public void CopiesOfOneDistributionGroupTogether()
    {
        Assert.Equal("MYSTRY Mew", GalleryNames.GroupKey("MYSTRY (118 of 430) Mew (BA7609E8)"));
        Assert.Equal("Movie Darkrai", GalleryNames.GroupKey("Movie Darkrai (NA)"));
        Assert.Equal("PCNYb Shiny Raikou", GalleryNames.GroupKey("PCNYb 0510 Shiny Raikou"));
        Assert.Equal("10 ANIV Celebi", GalleryNames.GroupKey("10 ANIV Celebi"));
        Assert.Equal("WSHMKR Jirachi (Salac Berry)", GalleryNames.GroupKey("WSHMKR Jirachi (Salac Berry)"));
    }

    private static GalleryEntry Entry(string path, int gen, string[] games, string? lang, GalleryKind kind = GalleryKind.Card, bool released = true)
        => new(path, gen, games, lang, released, kind, 0, GalleryFlags.None, Path.GetFileNameWithoutExtension(path));

    [Fact]
    public void ProfileFiltersByGameLanguageAndRelease()
    {
        var platinum = new GameProfile(4, ["Pt"], "ENG");
        Assert.True(platinum.Matches(Entry("Released/Gen 4/W/ENG/a.wc4", 4, ["D", "P", "Pt"], "ENG"), false, false));
        Assert.False(platinum.Matches(Entry("Released/Gen 4/W/ENG/b.wc4", 4, ["HG", "SS"], "ENG"), false, false));
        // Pokémon can be traded between games of a generation, so their tags don't limit them.
        Assert.True(platinum.Matches(Entry("Released/Gen 4/T/c.pk4", 4, ["HG", "SS"], "ENG", GalleryKind.Pokemon), false, false));
        Assert.False(platinum.Matches(Entry("Released/Gen 4/W/GER/d.wc4", 4, ["Pt"], "GER"), false, false));
        Assert.True(platinum.Matches(Entry("Released/Gen 4/W/GER/d.wc4", 4, ["Pt"], "GER"), allLanguages: true, false));
        Assert.True(platinum.Matches(Entry("Released/Gen 4/M/e.pgt", 4, [], null), false, false));
        Assert.False(platinum.Matches(Entry("Unreleased/Gen 4/X/f.pgt", 4, [], null, released: false), false, false));
        Assert.True(platinum.Matches(Entry("Unreleased/Gen 4/X/f.pgt", 4, [], null, released: false), false, unreleased: true));
        Assert.False(platinum.Matches(Entry("Released/Gen 5/W/ENG/g.pgf", 5, ["B", "W"], "ENG"), false, false));
    }

    [Fact]
    public void LanguageAndGameComeFromTheRomHeader()
    {
        var sav = _saves.Create(GameVersion.FR, "Pokemon.sav").Sav;
        var rom = Path.Combine(_saves.Dir, "Pokemon.gba");
        var data = new byte[0x200];
        "BPGD"u8.CopyTo(data.AsSpan(0xAC)); // LeafGreen, German
        File.WriteAllBytes(rom, data);

        var profile = GameProfile.For(sav, [rom]);
        Assert.Equal(["LG"], profile.Games);
        Assert.Equal("GER", profile.Language);

        var noRom = GameProfile.For(sav, []);
        Assert.Equal("ENG", noRom.Language); // the fixture's Pokémon are English
        Assert.Equal(3, noRom.Generation);
    }

    [Fact]
    public void GenOneAndTwoUseInternationalLanguage()
    {
        var crystal = _saves.Create(GameVersion.C, "Crystal.sav").Sav;
        var profile = GameProfile.For(crystal, []);
        Assert.Equal("INT", profile.Language);
        Assert.Equal(["C"], profile.Games);
    }

    [Fact]
    public void GsBallEventCanBeEnabledInCrystal()
    {
        var entry = _saves.Create(GameVersion.C, "Crystal.sav");
        var crystal = (SAV2)entry.Sav;
        Assert.False(crystal.IsEnabledGSBallMobileEvent);
        crystal.EnableGSBallMobileEvent();
        Assert.True(((SAV2)_saves.Roundtrip(entry).Sav).IsEnabledGSBallMobileEvent);
    }

    [Fact]
    public void TreeSkipsThroughSingleFolders()
    {
        var files = new List<GalleryEntry>
        {
            Entry("Released/Gen 3/ENG/Wondercards/E - Item AuroraTicket (ENG).wc3", 3, ["E"], "ENG"),
            Entry("Released/Gen 3/ENG/Aura Mew/RSEFL - Aura Mew (65C6) (ENG).pk3", 3, [], "ENG", GalleryKind.Pokemon),
            Entry("Unreleased/Gen 3/ENG/E - Item Old Sea Map (debug)(ENG).wc3", 3, ["E"], "ENG", released: false),
        };
        var root = GalleryTree.Open(files, "");
        Assert.Equal(["ENG", "Unreleased"], root.Folders);

        var released = GalleryTree.Open(files.Take(2).ToList(), "");
        Assert.Equal("ENG", released.Path); // the lone language folder is skipped
        Assert.Equal(["ENG/Aura Mew", "ENG/Wondercards"], released.Folders);
        Assert.Equal("Wondercards", GalleryTree.Name(released.Folders[1]));
    }

    [Fact]
    public void BuilderIndexesAndBundlesFiles()
    {
        var root = Path.Combine(_saves.Dir, "EventsGallery");
        void Write(string rel, byte[] data)
        {
            var path = Path.Combine(root, rel);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path, data);
        }

        static byte[] Bytes(PKM pk)
        {
            var bytes = new byte[pk.SIZE_PARTY];
            pk.WriteDecryptedDataParty(bytes);
            return bytes;
        }

        var sav = _saves.Create(GameVersion.E, "Emerald.sav").Sav;
        var jirachi = TestSaves.Make(sav, Species.Jirachi, 5);
        var pikachu = TestSaves.Make(sav, Species.Pikachu, 5);
        Write("Released/Gen 3/ENG/WSHMKR Jirachi/RSEFL - WISHMKR Jirachi (C579) (ENG).pk3", Bytes(jirachi));
        Write("Released/Gen 3/ENG/WSHMKR Jirachi/RSEFL - WISHMKR Jirachi (6E7E) (ENG).pk3", Bytes(jirachi));
        Write("Released/Gen 3/ENG/Toys R Us/RSEFL - TRU Pikachu (ENG).pk3", Bytes(pikachu));
        Write("Released/Gen 3/ENG/Wondercards/E - Item AuroraTicket (ENG) (UK).wc3", Gen3EventTests.MakeWonderCard("AURORA TICKET"));
        Write("Released/Gen 3/ENG/Wondercards/notes.txt", [1, 2, 3]);
        Write("Released/Gen 3/ENG/broken.pk3", [1, 2, 3]);
        Write("Released/Gen 4/hex extracted cards/ENG/x.pcd", new byte[856]);

        var zip = Path.Combine(_saves.Dir, "gallery.zip");
        var (added, skipped) = GalleryBuilder.Build(root, zip);
        Assert.Equal(4, added);
        Assert.Equal(1, skipped);

        var gallery = new GalleryArchive(zip);
        var profile = new GameProfile(3, ["E"], "ENG");
        var events = GalleryLists.Events(gallery, profile);
        Assert.Equal("Item AuroraTicket (UK)", Assert.Single(events).Title);
        Assert.NotNull(gallery.Load(events[0])?.Gen3);

        var distributions = GalleryLists.Distributions(gallery, profile);
        Assert.Equal("WISHMKR Jirachi", Assert.Single(distributions).Title);
        Assert.Equal((ushort)Species.Jirachi, gallery.Load(distributions[0])?.Pokemon?.Species);

        Assert.Empty(GalleryLists.Events(gallery, profile with { Language = "GER" }));
        Assert.Empty(GalleryLists.Events(gallery, profile with { Games = ["FR"] }));
    }
}
