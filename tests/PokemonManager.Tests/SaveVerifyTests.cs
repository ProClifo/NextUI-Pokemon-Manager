using PKHeX.Core;
using PokemonManager.Core;
using Xunit;

namespace PokemonManager.Tests;

public sealed class SaveVerifyTests : IDisposable
{
    private readonly TestSaves _saves = new();

    public void Dispose()
    {
        SaveLibrary.AfterWriteForTests = null;
        _saves.Dispose();
    }

    [Fact]
    public void ASaveThatDoesntReadBackIsPutBackFromItsBackup()
    {
        var entry = _saves.Create(GameVersion.E, "Pokemon - Emerald Version (USA, Europe).sav", "MAY");
        var before = File.ReadAllBytes(entry.Path);
        entry.Sav.SetBoxSlotAtIndex(TestSaves.Make(entry.Sav, Species.Mudkip), 0);

        // the card hands back something else than was written
        SaveLibrary.AfterWriteForTests = path =>
        {
            var data = File.ReadAllBytes(path);
            data[0x1000] ^= 0xFF;
            File.WriteAllBytes(path, data);
        };
        var ex = Assert.Throws<IOException>(() => _saves.Library.Write(entry));
        Assert.Contains("backup was put back", ex.Message);
        Assert.Equal(before, File.ReadAllBytes(entry.Path));
    }

    [Fact]
    public void ASaveThatReadsBackIsKept()
    {
        var entry = _saves.Create(GameVersion.FR, "Pokemon - FireRed Version (USA).sav", "RED");
        entry.Sav.SetBoxSlotAtIndex(TestSaves.Make(entry.Sav, Species.Charmander), 0);
        _saves.Library.Write(entry);
        var again = SaveLibrary.Load(entry.Path);
        Assert.NotNull(again);
        Assert.Equal((ushort)Species.Charmander, again.Sav.GetBoxSlotAtIndex(0).Species);
    }
}
