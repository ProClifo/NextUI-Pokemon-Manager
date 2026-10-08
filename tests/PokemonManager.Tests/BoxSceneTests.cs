using System.Text.Json.Nodes;
using PKHeX.Core;
using PokemonManager.Core;
using Xunit;

namespace PokemonManager.Tests;

public sealed class BoxSceneTests : IDisposable
{
    private readonly TestSaves _saves = new();
    private readonly BoxScene _scene = new("/assets");

    public void Dispose() => _saves.Dispose();

    [Fact]
    public void ViewerBoxZeroIsTheParty()
    {
        Assert.Equal((0, 2), BoxScene.ToViewer(SlotRef.Party(2)));
        Assert.Equal((4, 7), BoxScene.ToViewer(new SlotRef(3, 7)));
        Assert.Equal(SlotRef.Party(5), BoxScene.FromViewer(0, 5));
        Assert.Equal(new SlotRef(13, 29), BoxScene.FromViewer(14, 29));
    }

    [Fact]
    public void SceneHasPartyAndEveryBoxWithItsWallpaper()
    {
        var sav = (SAV3)_saves.Create(GameVersion.E, "Emerald.sav").Sav;
        sav.SetPartySlotAtIndex(TestSaves.Make(sav, Species.Mudkip, 5), 0);
        new SlotRef(2, 4).Set(sav, TestSaves.Make(sav, Species.Pikachu, 25, heldItem: 0));
        ((IBoxDetailWallpaper)sav).SetBoxWallpaper(2, 11); // Sky

        var json = _scene.Build(sav, "Emerald - ASH", new SlotRef(2, 4));
        var boxes = json["boxes"]!.AsArray();

        Assert.Equal(1 + sav.BoxCount, boxes.Count);
        Assert.Equal("PARTY", (string?)boxes[0]!["name"]);
        Assert.Equal(6, boxes[0]!["slots"]!.AsArray().Count);
        Assert.Equal(3, (int)json["box"]!);
        Assert.Equal(4, (int)json["slot"]!);
        Assert.EndsWith("wallpapers/11.png", (string?)boxes[3]!["wallpaper"]);

        var pikachu = boxes[3]!["slots"]![4]!.AsObject();
        Assert.Equal("PIKACHU", (string?)pikachu["name"]);
        Assert.EndsWith("icons/25.png", (string?)pikachu["icon"]);
        Assert.Contains("Lv.25", pikachu["lines"]!.AsArray().Select(n => (string?)n));
        Assert.Empty(boxes[3]!["slots"]![5]!.AsObject()); // empty slot
    }

    [Fact]
    public void ArtFollowsFormsShininessAndEggs()
    {
        var sav = _saves.Create(GameVersion.E, "Emerald.sav").Sav;

        var unown = TestSaves.Make(sav, Species.Unown);
        unown.Form = 7;
        Assert.EndsWith("icons/201-7.png", _scene.IconPath(unown));
        Assert.EndsWith("sprites/201-7.png", _scene.SpritePath(unown));

        var shiny = TestSaves.Make(sav, Species.Rayquaza, 70);
        shiny.SetIsShiny(true);
        Assert.EndsWith("sprites/384-shiny.png", _scene.SpritePath(shiny));

        var egg = TestSaves.Make(sav, Species.Togepi, 5);
        egg.IsEgg = true;
        Assert.EndsWith("icons/egg.png", _scene.IconPath(egg));

        var pt = _saves.Create(GameVersion.Pt, "Platinum.sav").Sav;
        var lucario = TestSaves.Make(pt, Species.Lucario);
        Assert.EndsWith("icons/unknown.png", _scene.IconPath(lucario));
        Assert.Equal("", _scene.SpritePath(lucario));
    }

    [Fact]
    public void OtherGenerationsCycleThroughTheWallpapers()
    {
        var sav = _saves.Create(GameVersion.Pt, "Platinum.sav").Sav;
        var boxes = _scene.Build(sav, "Platinum", new SlotRef(0, 0))["boxes"]!.AsArray();
        Assert.EndsWith("wallpapers/00.png", (string?)boxes[1]!["wallpaper"]);
        Assert.EndsWith("wallpapers/01.png", (string?)boxes[2]!["wallpaper"]);
    }
}
