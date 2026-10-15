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
    public void MovesTheSelectedSlotsStateAndTheResumeSlotToBackups()
    {
        var roms = Path.Combine(_sd, "Roms");
        var rom = Touch("Roms", "Game Boy Advance (GBA)", "Pokemon Emerald.gba");
        var slotFile = Touch(".userdata", "shared", ".minui", "GBA", "Pokemon Emerald.gba.txt");
        File.WriteAllText(slotFile, "2");
        var state = Touch(".userdata", "shared", "GBA-gpsp", "Pokemon Emerald.gba.st2");
        var preview = Touch(".userdata", "shared", ".minui", "GBA", "Pokemon Emerald.gba.2.bmp");
        var other = Touch(".userdata", "shared", "GBA-gpsp", "Pokemon Emerald.gba.st3");

        Assert.Equal("GBA", SaveStates.EmuName(roms, rom));
        var backups = Path.Combine(_sd, "PokemonManager", "Backups", "Emerald - MAY - 12345");
        var backup = SaveStates.Backup.Beside(Path.Combine(backups, "Pokemon Emerald.20261015-211500.sav"));
        Assert.Equal(3, SaveStates.MoveSelected(_sd, roms, rom, backup));
        Assert.False(File.Exists(slotFile)); // NextUI no longer offers to resume
        Assert.False(File.Exists(state));
        Assert.False(File.Exists(preview));
        Assert.True(File.Exists(other)); // other slots are kept
        // all three are in the save's backup folder, named like its .sav backup from the same change
        var moved = Directory.GetFiles(backups).Select(f => Path.GetFileName(f)).Order().ToArray();
        Assert.Equal(["Pokemon Emerald.20261015-211500.2.bmp", "Pokemon Emerald.20261015-211500.st2",
            "Pokemon Emerald.20261015-211500.txt"], moved);
    }

    [Fact]
    public void NothingToDoWithoutAResumeSlot()
    {
        var roms = Path.Combine(_sd, "Roms");
        var rom = Touch("Roms", "Game Boy Color (GBC)", "Pokemon Crystal.gbc");
        var state = Touch(".userdata", "shared", "GBC-gambatte", "Pokemon Crystal.gbc.st0");
        Assert.Equal(0, SaveStates.MoveSelected(_sd, roms, rom, new SaveStates.Backup(Path.Combine(_sd, "Backups"), "Pokemon Crystal", "20261015-211500")));
        Assert.True(File.Exists(state));
    }

    [Fact]
    public void KnowsWhetherTheGameWouldResumeAState()
    {
        var roms = Path.Combine(_sd, "Roms");
        var rom = Touch("Roms", "Game Boy Advance (GBA)", "Pokemon Sapphire.gba");
        Assert.False(SaveStates.HasSelected(_sd, roms, rom));
        var slotFile = Touch(".userdata", "shared", ".minui", "GBA", "Pokemon Sapphire.gba.txt");
        File.WriteAllText(slotFile, "9");
        Assert.False(SaveStates.HasSelected(_sd, roms, rom)); // a slot but no state in it
        Touch(".userdata", "shared", "GBA-gpsp", "Pokemon Sapphire.gba.st9");
        Assert.True(SaveStates.HasSelected(_sd, roms, rom));
    }

    [Fact]
    public void MovesDraSticsAutoSaveStateToBackups()
    {
        var save = Touch("Saves", "NDS", "Pokemon Platinum.dsv");
        var auto = Touch(".userdata", "shared", "NDS-advanced-drastic", "Pokemon Platinum_10.dss");
        var manual = Touch(".userdata", "shared", "NDS-advanced-drastic", "Pokemon Platinum_0.dss");
        Assert.True(SaveStates.HasDraSticAuto(_sd, save, "tg5040"));
        var backups = Path.Combine(_sd, "Backups");
        var backup = new SaveStates.Backup(backups, "Pokemon Platinum", "20261015-211500");
        Assert.Equal(1, SaveStates.MoveDraSticAuto(_sd, save, "tg5040", backup));
        Assert.False(File.Exists(auto));
        Assert.True(File.Exists(manual));
        Assert.True(File.Exists(Path.Combine(backups, "Pokemon Platinum.20261015-211500.dss")));

        // a different auto slot in the pak's drastic.cfg
        var cfg = Touch("Emus", "tg5040", "NDS.pak", "drastic", "config", "drastic.cfg");
        File.WriteAllText(cfg, "enable_autosavestate = 1\nautosavestate_slot = 0\n");
        Assert.Equal(1, SaveStates.MoveDraSticAuto(_sd, save, "tg5040", backup));
        Assert.False(File.Exists(manual));
        Assert.True(File.Exists(Path.Combine(backups, "Pokemon Platinum.20261015-211500-1.dss")));
    }

    [Fact]
    public void KeepsTheNewestStateBackups()
    {
        var roms = Path.Combine(_sd, "Roms");
        var rom = Touch("Roms", "Game Boy Color (GBC)", "Pokemon Gold.gbc");
        var backups = Path.Combine(_sd, "Backups");
        for (int i = 0; i < SaveStates.BackupsToKeep + 3; i++)
        {
            File.WriteAllText(Touch(".userdata", "shared", ".minui", "GBC", "Pokemon Gold.gbc.txt"), "0");
            Touch(".userdata", "shared", "GBC-gambatte", "Pokemon Gold.gbc.st0");
            SaveStates.MoveSelected(_sd, roms, rom, new SaveStates.Backup(backups, "Pokemon Gold", $"20261015-2115{i:00}"));
        }
        Assert.Equal(SaveStates.BackupsToKeep, Directory.GetFiles(backups, "Pokemon Gold.*.st0").Length);
    }
}
