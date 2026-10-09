using PKHeX.Core;
using PokemonManager.Core;
using Xunit;

namespace PokemonManager.Tests;

public sealed class GameFontTests : IDisposable
{
    private readonly TestSaves _saves = new();
    private readonly string _dir;

    public GameFontTests()
    {
        _dir = Path.Combine(_saves.Dir, "fonts");
        Directory.CreateDirectory(_dir);
        var entries = new List<string>();
        foreach (var name in new[] { "gen1", "gen2-gs", "gen2-c", "gen3-rse", "gen3-frlg", "gen4" })
        {
            File.WriteAllText(Path.Combine(_dir, $"{name}.chars"), "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789 é-()");
            var scales = new List<string>();
            foreach (var scale in new[] { 2, 3 })
            {
                foreach (var role in new[] { "list", "title", "message" })
                    File.WriteAllBytes(Path.Combine(_dir, $"{name}-{scale}x-{role}.ttf"), []);
                scales.Add($"\"{scale}x\": {{\"list\": \"{name}-{scale}x-list.ttf\", \"title\": \"{name}-{scale}x-title.ttf\", \"message\": \"{name}-{scale}x-message.ttf\", \"message_size\": {(scale == 2 ? 24 : 18)}}}");
            }
            entries.Add($"\"{name}\": {{\"chars\": \"{name}.chars\", {string.Join(", ", scales)}}}");
        }
        File.WriteAllText(Path.Combine(_dir, "fonts.json"), "{" + string.Join(", ", entries) + "}");
    }

    public void Dispose() => _saves.Dispose();

    [Theory]
    [InlineData(GameVersion.RD, "gen1")]
    [InlineData(GameVersion.GD, "gen2-gs")]
    [InlineData(GameVersion.C, "gen2-c")]
    [InlineData(GameVersion.E, "gen3-rse")]
    [InlineData(GameVersion.R, "gen3-rse")]
    [InlineData(GameVersion.FR, "gen3-frlg")]
    [InlineData(GameVersion.Pt, "gen4")]
    [InlineData(GameVersion.HG, "gen4")]
    public void EachGameGetsItsFont(GameVersion version, string font)
    {
        var sav = _saves.Create(version, $"{version}.sav").Sav;
        var picked = new GameFonts(_dir, 3).For(sav, "ENG");
        Assert.NotNull(picked);
        Assert.EndsWith($"{font}-3x-list.ttf", picked!.List);
        Assert.Equal(18, picked.MessageSize);
    }

    [Fact]
    public void NoFontForGen5OrJapaneseGames()
    {
        var fonts = new GameFonts(_dir, 2);
        Assert.Null(fonts.For(_saves.Create(GameVersion.B2, "B2.sav").Sav, "ENG"));
        Assert.Null(fonts.For(_saves.Create(GameVersion.E, "E.sav").Sav, "JPN"));
    }

    [Fact]
    public void TextTheFontCantShowFallsBack()
    {
        var font = new GameFonts(_dir, 2).For(_saves.Create(GameVersion.E, "E.sav").Sav, "ENG")!;
        Assert.True(font.Covers(["Pokémon Manager", "Gallery (12)", "line\nbreak"]));
        Assert.False(font.Covers(["アゲト Celebi"]));
        Assert.False(font.Covers(["Use \"this\"?"]));
    }

    [Theory]
    [InlineData("tg5040", "brick", 3)]
    [InlineData("tg5040", "smartpro", 2)]
    [InlineData("h700", "rgsp", 2)]
    [InlineData(null, null, 2)]
    public void UiScaleMatchesNextUi(string? platform, string? device, int scale)
        => Assert.Equal(scale, GameFonts.Scale(platform, device));
}
