using System.IO.Compression;
using System.Text;
using PKHeX.Core;
using PokemonManager.Core;
using Xunit;

namespace PokemonManager.Tests;

public sealed class GameBackgroundTests : IDisposable
{
    private readonly TestSaves _saves = new();
    private readonly string _art;
    private readonly GameBackgrounds _backgrounds;

    public GameBackgroundTests()
    {
        _art = Path.Combine(_saves.Dir, "backgrounds");
        foreach (var game in new[] { "ruby", "sapphire", "emerald", "firered", "leafgreen" })
        {
            Directory.CreateDirectory(Path.Combine(_art, game));
            File.WriteAllBytes(Path.Combine(_art, game, "original.png"), []);
            File.WriteAllBytes(Path.Combine(_art, game, "720x480.png"), []);
        }
        _backgrounds = new GameBackgrounds(_art);
    }

    public void Dispose() => _saves.Dispose();

    private string Rom(string name, string code)
    {
        var rom = new byte[0x200];
        Encoding.ASCII.GetBytes(code).CopyTo(rom, 0xAC);
        var path = Path.Combine(_saves.Dir, name);
        File.WriteAllBytes(path, rom);
        return path;
    }

    [Theory]
    [InlineData("BPRE", "firered")]
    [InlineData("BPGE", "leafgreen")]
    [InlineData("BPRP", "firered")]
    [InlineData("BPEJ", "emerald")]
    [InlineData("AXVE", "ruby")]
    [InlineData("AXPD", "sapphire")]
    [InlineData("BPEE", "emerald")]
    [InlineData("AGBJ", null)]
    public void GameCodeDecidesTheGame(string code, string? game)
        => Assert.Equal(game, GameBackgrounds.GameFromGbaCode(code));

    [Fact]
    public void HeaderIsReadFromGbaAndZippedRoms()
    {
        Assert.Equal("leafgreen", GameBackgrounds.GameFromRom(Rom("Totally Renamed.gba", "BPGE")));

        var zipPath = Path.Combine(_saves.Dir, "Pokemon.zip");
        using (var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        {
            zip.CreateEntry("readme.txt");
            using var s = zip.CreateEntry("Pokemon.gba").Open();
            s.Write(File.ReadAllBytes(Rom("tmp.gba", "AXPE")));
        }
        Assert.Equal("sapphire", GameBackgrounds.GameFromRom(zipPath));
        Assert.Null(GameBackgrounds.GameFromRom(Path.Combine(_saves.Dir, "missing.gba")));
    }

    [Fact]
    public void RomHeaderWinsOverFileNames()
    {
        // A FireRed ROM someone named "LeafGreen": the header is what counts.
        var sav = _saves.Create(GameVersion.FR, "Pokemon LeafGreen.sav").Sav;
        var rom = Rom("Pokemon LeafGreen.gba", "BPRE");
        Assert.EndsWith(Path.Combine("firered", "720x480.png"), _backgrounds.Find(sav, "Pokemon LeafGreen.gba.sav", [rom], (720, 480)));
    }

    [Fact]
    public void FallsBackToTheSaveThenTheName()
    {
        var emerald = _saves.Create(GameVersion.E, "x.sav").Sav;
        Assert.EndsWith(Path.Combine("emerald", "original.png"), _backgrounds.Find(emerald, "x.sav", [], (1280, 720)));

        var rs = _saves.Create(GameVersion.R, "y.sav").Sav;
        Assert.EndsWith(Path.Combine("ruby", "original.png"), _backgrounds.Find(rs, "Pokemon - Ruby Version (USA, Europe).gba.sav", [], null));
        Assert.Equal("firered", GameBackgrounds.GameFromName("Pokemon_Fire_Red.sav"));

        var platinum = _saves.Create(GameVersion.Pt, "Platinum.sav").Sav;
        Assert.Null(_backgrounds.Find(platinum, "Platinum.sav", [], (720, 480)));
    }

    [Theory]
    [InlineData("h700", "rgsp", 720, 480)]
    [InlineData("h700", "rg34xxsp", 720, 480)]
    [InlineData("h700", "rgcubexx", 720, 720)]
    [InlineData("h700", "rg35xxplus", 640, 480)]
    [InlineData("tg5040", "brick", 1024, 768)]
    [InlineData("tg5040", "smartpro", 1280, 720)]
    [InlineData("tg5050", null, 1280, 720)]
    [InlineData("my355", null, 640, 480)]
    public void ScreenSizeMatchesNextUi(string platform, string? device, int width, int height)
        => Assert.Equal((width, height), GameBackgrounds.ScreenSize(platform, device));
}
