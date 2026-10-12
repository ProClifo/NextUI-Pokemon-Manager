using PokemonManager.Core;
using Xunit;

namespace PokemonManager.Tests;

public sealed class OfficialSavesTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory().FullName;
    public void Dispose() => Directory.Delete(_dir, true);

    [Fact]
    public void MovedSavesAreRememberedRelativeToTheSdCard()
    {
        var file = Path.Combine(_dir, "official-saves.txt");
        new OfficialSaves(file, "/mnt/SDCARD").Add("/mnt/SDCARD/Saves/NDS/Black.sav");
        var saves = new OfficialSaves(file, "/media/card");
        Assert.True(saves.Contains("/media/card/Saves/NDS/Black.sav"));
        Assert.False(saves.Contains("/media/card/Saves/NDS/White.sav"));
    }
}
