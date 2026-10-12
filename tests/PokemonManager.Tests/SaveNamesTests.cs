using PokemonManager.Core;
using Xunit;

namespace PokemonManager.Tests;

public sealed class SaveNamesTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory().FullName;
    public void Dispose() => Directory.Delete(_dir, true);

    [Fact]
    public void NamesAreKeptBetweenRunsAndAnEmptyNameRemovesOne()
    {
        var file = Path.Combine(_dir, "save-names.txt");
        new SaveNames(file).Set("/mnt/SDCARD/Saves/GBA/Emerald Kaizo.sav", "Kaizo\tEmerald");
        var names = new SaveNames(file);
        Assert.Equal("Kaizo Emerald", names.Get("/mnt/SDCARD/Saves/GBA/Emerald Kaizo.sav"));
        names.Set("/mnt/SDCARD/Saves/GBA/Emerald Kaizo.sav", "  ");
        Assert.Null(new SaveNames(file).Get("/mnt/SDCARD/Saves/GBA/Emerald Kaizo.sav"));
    }
}
