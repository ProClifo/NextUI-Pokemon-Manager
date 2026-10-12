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
        int deleted = 0;
        foreach (var file in SelectedFiles(sdRoot, romsRoot, romPath).Where(File.Exists))
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

    /// <summary>Whether launching the ROM would resume a save state (its selected slot holds one).</summary>
    public static bool HasSelected(string sdRoot, string romsRoot, string romPath)
        => SelectedFiles(sdRoot, romsRoot, romPath).Skip(2).Any(File.Exists); // past the slot file and preview

    /// <summary>The selected slot's file, its preview, then the state files it could be.</summary>
    private static List<string> SelectedFiles(string sdRoot, string romsRoot, string romPath)
    {
        var emu = EmuName(romsRoot, romPath);
        if (emu is null)
            return [];
        var shared = Path.Combine(sdRoot, ".userdata", "shared");
        var rom = Path.GetFileName(romPath);
        var slotFile = Path.Combine(shared, ".minui", emu, rom + ".txt");
        if (!File.Exists(slotFile))
            return []; // nothing to resume
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
        return doomed;
    }

    /// <summary>
    /// The DS pak's (NDS.pak, DraStic) auto save state, which DraStic loads when the game starts: in
    /// .userdata/shared/NDS-advanced-drastic, named "&lt;game&gt;_&lt;slot&gt;.dss" after the save ("&lt;game&gt;.dsv"), the
    /// slot being drastic.cfg's autosavestate_slot (10 unless changed). Returns how many files were deleted.
    /// </summary>
    public static int DeleteDraSticAuto(string sdRoot, string savePath, string? platform)
    {
        var file = DraSticAuto(sdRoot, savePath, platform);
        if (file is null || !File.Exists(file))
            return 0;
        try
        {
            File.Delete(file);
            return 1;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine($"Couldn't delete {file}: {ex.Message}");
            return 0;
        }
    }

    /// <summary>Whether DraStic would resume the game from its auto save state.</summary>
    public static bool HasDraSticAuto(string sdRoot, string savePath, string? platform)
        => DraSticAuto(sdRoot, savePath, platform) is { } file && File.Exists(file);

    private static string? DraSticAuto(string sdRoot, string savePath, string? platform)
    {
        if (!savePath.EndsWith(".dsv", StringComparison.OrdinalIgnoreCase) && !savePath.EndsWith(".sav", StringComparison.OrdinalIgnoreCase))
            return null;
        int slot = 10;
        if (platform is not null)
        {
            var cfg = Path.Combine(sdRoot, "Emus", platform, "NDS.pak", "drastic", "config", "drastic.cfg");
            try
            {
                if (File.Exists(cfg))
                {
                    foreach (var line in File.ReadLines(cfg))
                    {
                        var parts = line.Split('=', 2);
                        if (parts.Length == 2 && parts[0].Trim() == "autosavestate_slot" && int.TryParse(parts[1].Trim(), out var s))
                            slot = s;
                    }
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }
        }
        var game = Path.GetFileNameWithoutExtension(savePath);
        return Path.Combine(sdRoot, ".userdata", "shared", "NDS-advanced-drastic", $"{game}_{slot}.dss");
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
