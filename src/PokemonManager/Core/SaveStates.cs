namespace PokemonManager.Core;

/// <summary>
/// NextUI's save states for a ROM (see minarch's State_getPath and nextui.c's readyResumePath). Resuming a game
/// loads the state in its last-used slot, which would undo any change made to the save file since, so the
/// "Save State Deletion" setting removes that state, its preview and the slot file that offers to resume.
/// </summary>
public static class SaveStates
{
    private const int AutoResumeSlot = 9;

    /// <summary>
    /// Deletes the state in the ROM's selected slot, its preview, and the slot file (so the game starts from
    /// its save). Returns how many files were deleted.
    /// </summary>
    public static int DeleteSelected(string sdRoot, string romsRoot, string romPath)
    {
        var emu = EmuName(romsRoot, romPath);
        if (emu is null)
            return 0;
        var shared = Path.Combine(sdRoot, ".userdata", "shared");
        var rom = Path.GetFileName(romPath);
        var slotFile = Path.Combine(shared, ".minui", emu, rom + ".txt");
        if (!File.Exists(slotFile))
            return 0; // nothing to resume
        int slot = int.TryParse(File.ReadAllText(slotFile).Trim(), out var s) ? s : 0;

        var doomed = new List<string> { slotFile, Path.Combine(shared, ".minui", emu, $"{rom}.{slot}.bmp") };
        // States live in <EMU>-<core>, under one of the state-format names.
        var stem = Path.GetFileNameWithoutExtension(rom);
        var names = new List<string> { $"{rom}.st{slot}" };
        if (slot == AutoResumeSlot)
            names.Add($"{stem}.state.auto");
        else
            names.AddRange([$"{stem}.state.{slot}", slot == 0 ? $"{stem}.state" : $"{stem}.state{slot}"]);
        if (Directory.Exists(shared))
        {
            foreach (var dir in Directory.EnumerateDirectories(shared, $"{emu}-*"))
                doomed.AddRange(names.Select(n => Path.Combine(dir, n)));
        }

        int deleted = 0;
        foreach (var file in doomed.Where(File.Exists))
        {
            try
            {
                File.Delete(file);
                deleted++;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Console.Error.WriteLine($"Couldn't delete {file}: {ex.Message}");
            }
        }
        return deleted;
    }

    /// <summary>NextUI's emulator tag: the "(GBA)" part of the ROM's top folder under Roms.</summary>
    public static string? EmuName(string romsRoot, string romPath)
    {
        var relative = Path.GetRelativePath(romsRoot, romPath);
        if (relative.StartsWith("..", StringComparison.Ordinal))
            return null;
        var folder = relative.Split(Path.DirectorySeparatorChar)[0];
        int open = folder.LastIndexOf('('), close = folder.LastIndexOf(')');
        return open >= 0 && close > open ? folder[(open + 1)..close] : folder;
    }
}
