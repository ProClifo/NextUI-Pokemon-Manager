using PKHeX.Core;
using PokemonManager.Core;
using Xunit;

namespace PokemonManager.Tests;

public sealed class EggNameTests
{
    [Fact]
    public void Gen3EggsAreShownInTheGamesLanguage()
    {
        // Gen 3 eggs are stored as Japanese "タマゴ" in every language's games
        var saves = new TestSaves();
        var e = saves.Create(GameVersion.E, "e.sav");
        var pk = (PK3)TestSaves.Make(e.Sav, Species.Togepi, 5);
        pk.IsEgg = true;
        pk.Language = (int)LanguageID.Japanese;
        pk.Nickname = "タマゴ";
        new SlotRef(0, 0).Set(e.Sav, pk);
        var scene = new BoxScene(Path.GetTempPath()).Build(e.Sav, "", new SlotRef(0, 0));
        var slot = scene["boxes"]![1]!["slots"]![0]!;
        Assert.Equal("EGG", (string?)slot["name"]);
        Assert.Equal("EGG", (string?)slot["summary"]!["nickname"]);
        var german = new BoxScene(Path.GetTempPath()).Build(e.Sav, "", new SlotRef(0, 0), language: "GER");
        Assert.Equal("EI", (string?)german["boxes"]![1]!["slots"]![0]!["name"]);
    }
}
