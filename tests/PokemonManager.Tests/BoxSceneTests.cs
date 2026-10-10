using System.Text.Json.Nodes;
using PKHeX.Core;
using PokemonManager.Core;
using Xunit;

namespace PokemonManager.Tests;

public sealed class BoxSceneTests : IDisposable
{
    private readonly TestSaves _saves = new();
    private readonly string _assets = Directory.CreateTempSubdirectory("pkmgr-box").FullName;
    private readonly BoxScene _scene;

    public BoxSceneTests()
    {
        // Small stand-ins for the art sets scripts/build-box-art.py writes.
        Index("e", """{"icons":{"25":[0,0],"201-7":[32,0],"egg":[64,0],"384":[96,0]},"sprites":{"25":[0,0,40,48],"201-7":[40,0,30,30],"384":[70,0,64,64],"384-shiny":[134,0,64,64],"egg":[200,0,24,24]}}""");
        Index("rb", """{"icons":{"25":[0,0]},"sprites":{"25":[0,0,40,40]}}""");
        Index("frlg", """{"icons":{"386":[0,0]},"sprites":{"386":[0,0,60,60],"386-leafgreen":[60,0,60,60]}}""");
        Index("pt", """{"icons":{"448":[0,64]},"sprites":{"448":[0,0,80,80],"25-f":[80,0,40,40],"25":[120,0,40,40]}}""");
        _scene = new BoxScene(_assets);
    }

    private void Index(string set, string json)
    {
        Directory.CreateDirectory(Path.Combine(_assets, "art", set));
        File.WriteAllText(Path.Combine(_assets, "art", set, "index.json"), json);
    }

    public void Dispose()
    {
        _saves.Dispose();
        Directory.Delete(_assets, true);
    }

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
        // Serialising is what failed on the device: the trimmed build has no reflection-based JSON.
        Assert.Contains("PIKACHU", json.ToJsonString());
        var boxes = json["boxes"]!.AsArray();

        Assert.Equal(1 + sav.BoxCount, boxes.Count);
        Assert.Equal("PARTY", (string?)boxes[0]!["name"]);
        Assert.Equal(6, boxes[0]!["slots"]!.AsArray().Count);
        Assert.Equal(3, (int)json["box"]!);
        Assert.Equal(4, (int)json["slot"]!);
        Assert.EndsWith("wallpapers/11.png", (string?)boxes[3]!["wallpaper"]);

        var pikachu = boxes[3]!["slots"]![4]!.AsObject();
        Assert.Equal("PIKACHU", (string?)pikachu["name"]);
        Assert.EndsWith("art/e/icons.png", (string?)pikachu["icon"]);
        Assert.Equal("[0,0,32,64]", pikachu["icon_rect"]!.ToJsonString());
        Assert.EndsWith("art/e/sprites.png", (string?)pikachu["sprite"]);
        Assert.Equal("[0,0,40,48]", pikachu["sprite_rect"]!.ToJsonString());
        Assert.Null(json["font"]);
        Assert.Contains("Lv.25", pikachu["lines"]!.AsArray().Select(n => (string?)n));
        Assert.Empty(boxes[3]!["slots"]![5]!.AsObject()); // empty slot
    }

    [Fact]
    public void ArtFollowsFormsShininessAndEggs()
    {
        var sav = _saves.Create(GameVersion.E, "Emerald.sav").Sav;
        var sets = BoxScene.ArtSets(sav);

        var unown = TestSaves.Make(sav, Species.Unown);
        unown.Form = 7;
        Assert.Equal(32, _scene.Icon(unown, sets)!.X);
        Assert.Equal(40, _scene.Sprite(unown, sets)!.X);

        var shiny = TestSaves.Make(sav, Species.Rayquaza, 70);
        shiny.SetIsShiny(true);
        Assert.Equal(134, _scene.Sprite(shiny, sets)!.X);

        var egg = TestSaves.Make(sav, Species.Togepi, 5);
        egg.IsEgg = true;
        Assert.Equal(64, _scene.Icon(egg, sets)!.X);
        Assert.Equal(200, _scene.Sprite(egg, sets)!.X);
    }

    [Fact]
    public void EachGameUsesItsOwnArtThenFallsBack()
    {
        Assert.Equal(["rb", "e", "pt"], BoxScene.ArtSets(_saves.Create(GameVersion.RD, "Red.sav").Sav));
        Assert.Equal(["y", "e", "pt"], BoxScene.ArtSets(_saves.Create(GameVersion.YW, "Yellow.sav").Sav));
        Assert.Equal(["c", "e", "pt"], BoxScene.ArtSets(_saves.Create(GameVersion.C, "Crystal.sav").Sav));
        Assert.Equal(["rs", "e", "pt"], BoxScene.ArtSets(_saves.Create(GameVersion.R, "Ruby.sav").Sav));
        Assert.Equal(["frlg", "e", "pt"], BoxScene.ArtSets(_saves.Create(GameVersion.FR, "FireRed.sav").Sav));
        Assert.Equal(["pt", "e"], BoxScene.ArtSets(_saves.Create(GameVersion.HG, "HeartGold.sav").Sav));

        var red = _saves.Create(GameVersion.RD, "Red.sav").Sav;
        var pikachu = TestSaves.Make(red, Species.Pikachu, 5);
        Assert.EndsWith("art/rb/sprites.png", _scene.Sprite(pikachu, BoxScene.ArtSets(red))!.Sheet);

        var lg = _saves.Create(GameVersion.LG, "LeafGreen.sav").Sav;
        var deoxys = TestSaves.Make(lg, Species.Deoxys, 30);
        Assert.Equal(60, _scene.Sprite(deoxys, BoxScene.ArtSets(lg), leafGreen: true)!.X);
        Assert.Equal(0, _scene.Sprite(deoxys, BoxScene.ArtSets(lg))!.X);

        var pt = _saves.Create(GameVersion.Pt, "Platinum.sav").Sav;
        var lucario = TestSaves.Make(pt, Species.Lucario);
        Assert.EndsWith("art/pt/icons.png", _scene.Icon(lucario, BoxScene.ArtSets(pt))!.Sheet);
        var female = TestSaves.Make(pt, Species.Pikachu, 5);
        female.Gender = 1;
        Assert.Equal(80, _scene.Sprite(female, BoxScene.ArtSets(pt))!.X);

        // Species no set has get the "?" icon and no sprite.
        var bw = _saves.Create(GameVersion.W, "White.sav").Sav;
        var snivy = TestSaves.Make(bw, Species.Snivy, 5);
        Assert.Null(_scene.Icon(snivy, BoxScene.ArtSets(bw)));
        Assert.Null(_scene.Sprite(snivy, BoxScene.ArtSets(bw)));
    }

    [Fact]
    public void SceneUsesTheOgFontUnlessItLacksTheText()
    {
        var dir = Directory.CreateTempSubdirectory("fonts").FullName;
        var og = new UiFont(UiFontTests.WriteFont(dir, "font2.ttf", ('\u0000', '\u00FF')));
        var next = new UiFont(UiFontTests.WriteFont(dir, "font1.ttf", ('\u0000', '\uFFFD')));
        var sav = _saves.Create(GameVersion.E, "Emerald.sav").Sav;
        Assert.Equal(og.Path, (string?)_scene.Build(sav, "Emerald", new SlotRef(0, 0), og, next)["font"]);

        sav.SetBoxSlotAtIndex(TestSaves.Make(sav, Species.NidoranM, 5), 0, 0); // NIDORAN♂: OG has no ♂
        Assert.Equal(next.Path, (string?)_scene.Build(sav, "Emerald", new SlotRef(0, 0), og, next)["font"]);
        Directory.Delete(dir, true);
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
