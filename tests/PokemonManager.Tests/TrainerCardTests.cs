using PKHeX.Core;
using PokemonManager.Core;
using Xunit;

namespace PokemonManager.Tests;

public sealed class TrainerCardTests : IDisposable
{
    private readonly TestSaves _saves = new();
    public void Dispose() => _saves.Dispose();

    [Fact]
    public void Gen3CardHasTheRecordsAndStars()
    {
        var sav = (SAV3)_saves.Create(GameVersion.E, "E.sav").Sav;
        sav.SetRecord(10, 1); // entered the Hall of Fame
        sav.SetRecord(1, (2u << 16) | (3u << 8) | 4u);
        sav.SetRecord(21, 7); // trades
        var card = TrainerCard.For(sav, "skin", null);
        Assert.Equal(new[] { 2, 3, 4 }, card["hof"]!.AsArray().Select(n => n!.GetValue<int>()));
        Assert.Equal(7u, card["trades"]!.GetValue<uint>());
        Assert.Equal(1, card["stars"]!.GetValue<int>());
    }

    [Fact]
    public void NoHallOfFameNoStar()
    {
        var card = TrainerCard.For(_saves.Create(GameVersion.FR, "FR.sav").Sav, "skin", null);
        Assert.Null(card["hof"]);
        Assert.Equal(0, card["stars"]!.GetValue<int>());
    }

    [Fact]
    public void LaterGamesHaveNoCard()
    {
        var dir = Directory.CreateTempSubdirectory();
        try
        {
            File.WriteAllText(Path.Combine(dir.FullName, "layout.json"), "{\"card\": true}");
            Assert.True(TrainerCard.Available(_saves.Create(GameVersion.E, "E.sav").Sav, dir.FullName));
            Assert.False(TrainerCard.Available(_saves.Create(GameVersion.Pt, "Pt.sav").Sav, dir.FullName));
        }
        finally
        {
            dir.Delete(true);
        }
    }
}
