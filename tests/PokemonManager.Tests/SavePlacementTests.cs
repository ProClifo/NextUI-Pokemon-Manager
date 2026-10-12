using PokemonManager.Core;
using Xunit;

namespace PokemonManager.Tests;

public sealed class SavePlacementTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory().FullName;
    public void Dispose() => Directory.Delete(_dir, true);

    [Fact]
    public void MovesAreRememberedRelativeToTheSdCard()
    {
        var file = Path.Combine(_dir, "save-lists.txt");
        var lists = new SavePlacement(file, null, "/mnt/SDCARD");
        lists.Set("/mnt/SDCARD/Saves/NDS/Black.sav", true);
        lists.Set("/mnt/SDCARD/Saves/GBA/Emerald.sav", false);
        var again = new SavePlacement(file, null, "/media/card");
        Assert.True(again.OnMainList("/media/card/Saves/NDS/Black.sav"));
        Assert.False(again.OnMainList("/media/card/Saves/GBA/Emerald.sav"));
        Assert.Null(again.OnMainList("/media/card/Saves/NDS/White.sav"));
    }

    [Fact]
    public void SavesMovedToTheMainListBeforeAreKept()
    {
        var legacy = Path.Combine(_dir, "official-saves.txt");
        File.WriteAllText(legacy, "Saves/NDS/Black.sav\n");
        var lists = new SavePlacement(Path.Combine(_dir, "save-lists.txt"), legacy, "/mnt/SDCARD");
        Assert.True(lists.OnMainList("/mnt/SDCARD/Saves/NDS/Black.sav"));
        lists.Set("/mnt/SDCARD/Saves/NDS/Black.sav", false);
        Assert.False(new SavePlacement(Path.Combine(_dir, "save-lists.txt"), legacy, "/mnt/SDCARD").OnMainList("/mnt/SDCARD/Saves/NDS/Black.sav"));
    }
}
