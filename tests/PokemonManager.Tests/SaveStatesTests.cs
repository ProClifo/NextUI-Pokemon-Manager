using PokemonManager.Core;
using Xunit;

namespace PokemonManager.Tests;

public sealed class SaveStatesTests : IDisposable
{
    private readonly string _sd = Directory.CreateTempSubdirectory("sd").FullName;
    public void Dispose() => Directory.Delete(_sd, true);

    private string Touch(params string[] parts)
    {
        var path = Path.Combine([_sd, .. parts]);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "x");
        return path;
    }

    [Fact]
    public void DeletesTheSelectedSlotsStateAndTheResumeSlot()
    {
        var roms = Path.Combine(_sd, "Roms");
        var rom = Touch("Roms", "Game Boy Advance (GBA)", "Pokemon Emerald.gba");
        var slotFile = Touch(".userdata", "shared", ".minui", "GBA", "Pokemon Emerald.gba.txt");
        File.WriteAllText(slotFile, "2");
        var state = Touch(".userdata", "shared", "GBA-gpsp", "Pokemon Emerald.gba.st2");
        var preview = Touch(".userdata", "shared", ".minui", "GBA", "Pokemon Emerald.gba.2.bmp");
        var other = Touch(".userdata", "shared", "GBA-gpsp", "Pokemon Emerald.gba.st3");

        Assert.Equal("GBA", SaveStates.EmuName(roms, rom));
        Assert.Equal(3, SaveStates.DeleteSelected(_sd, roms, rom));
        Assert.False(File.Exists(slotFile)); // NextUI no longer offers to resume
        Assert.False(File.Exists(state));
        Assert.False(File.Exists(preview));
        Assert.True(File.Exists(other)); // other slots are kept
    }

    [Fact]
    public void NothingToDoWithoutAResumeSlot()
    {
        var roms = Path.Combine(_sd, "Roms");
        var rom = Touch("Roms", "Game Boy Color (GBC)", "Pokemon Crystal.gbc");
        var state = Touch(".userdata", "shared", "GBC-gambatte", "Pokemon Crystal.gbc.st0");
        Assert.Equal(0, SaveStates.DeleteSelected(_sd, roms, rom));
        Assert.True(File.Exists(state));
    }
}
