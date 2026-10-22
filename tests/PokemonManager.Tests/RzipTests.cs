using PKHeX.Core;
using PokemonManager.Core;
using Xunit;

namespace PokemonManager.Tests;

public class RzipTests
{
    [Fact]
    public void ACompressedSaveIsReadAndWrittenBackCompressed()
    {
        using var saves = new TestSaves();
        var plain = saves.Create(GameVersion.E, "Pokemon - Emerald Version (USA, Europe).sav", "MAY");
        var path = Path.ChangeExtension(plain.Path, ".srm");
        File.WriteAllBytes(path, Rzip.Compress(File.ReadAllBytes(plain.Path)));

        var entry = SaveLibrary.Load(path);
        Assert.NotNull(entry);
        Assert.True(entry.Compressed);
        Assert.Equal("MAY", entry.Sav.OT);

        entry.Sav.SetBoxSlotAtIndex(TestSaves.Make(entry.Sav, Species.Mudkip), 0);
        saves.Library.Write(entry);

        Assert.True(Rzip.IsCompressed(File.ReadAllBytes(path)));
        var again = SaveLibrary.Load(path);
        Assert.NotNull(again);
        Assert.True(again.Compressed);
        Assert.Equal((ushort)Species.Mudkip, again.Sav.GetBoxSlotAtIndex(0).Species);
    }

    [Fact]
    public void LargeSavesAreSplitIntoChunksAndPlainSavesAreLeftAlone()
    {
        var save = new byte[300 * 1024];
        new Random(1).NextBytes(save);
        Assert.Equal(save, Rzip.Decompress(Rzip.Compress(save)));
        Assert.False(Rzip.IsCompressed(save));
        Assert.Null(Rzip.Decompress(save));
    }

    [Fact]
    public void ADamagedCompressedSaveIsNotASave()
    {
        var data = Rzip.Compress(new byte[128 * 1024]);
        Assert.Null(Rzip.Decompress(data.AsSpan(0, data.Length - 5)));
    }
}
