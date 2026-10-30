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

    [Fact]
    public void UnrecognisedPokemonSavesAreLoggedAndOtherGamesAreNot()
    {
        var dir = Directory.CreateTempSubdirectory().FullName;
        var log = new StringWriter();
        var stderr = Console.Error;
        try
        {
            File.WriteAllBytes(Path.Combine(dir, "Pokemon - Ruby Version (USA, Europe).srm"), new byte[139264]);
            File.WriteAllBytes(Path.Combine(dir, "Pokemon - Sapphire Version (USA, Europe).srm"), Rzip.Compress(new byte[1000]));
            File.WriteAllBytes(Path.Combine(dir, "Metroid Fusion (USA).srm"), new byte[8192]);
            Console.SetError(log);
            Assert.Empty(SaveLibrary.Scan([dir]));
        }
        finally
        {
            Console.SetError(stderr);
            Directory.Delete(dir, true);
        }
        var text = log.ToString();
        Assert.Contains("Pokemon - Ruby Version (USA, Europe).srm (139264 bytes)", text);
        Assert.Contains("RetroArch compressed, 1000 bytes unpacked", text);
        Assert.DoesNotContain("Metroid", text);
    }
}
