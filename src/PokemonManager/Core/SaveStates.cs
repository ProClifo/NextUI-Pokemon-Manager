namespace PokemonManager.Core;

/// <summary>
/// NextUI's save states for a ROM (see minarch's State_getPath and nextui.c's readyResumePath). Resuming a game
/// loads the state in its last-used slot, which would undo any change made to the save file since, so the
/// "Save State Backup" setting moves that state, its preview and the slot file that offers to resume into the
/// save's backup folder, named like the save's backup from the same change, where they can be copied back by hand.
/// </summary>
public static class SaveStates
{
    private const int AutoResumeSlot = 9;

    /// <summary>How many backups of each state file are kept: states are bigger than saves (a DS state ~10 MB).</summary>
    public const int BackupsToKeep = 10;

    /// <summary>
    /// Where moved-away states go, named like the save's backup made with the same change: "&lt;Name&gt;.&lt;Stamp&gt;" plus
    /// the state's own ending ("Pokemon Emerald.20261015-211500.sav" → "Pokemon Emerald.20261015-211500.st2").
    /// </summary>
    public sealed record Backup(string Dir, string Name, string Stamp)
    {
        /// <summary>From the path of the save's backup ("…/Pokemon Emerald.20261015-211500.sav").</summary>
        public static Backup Beside(string saveBackup)
        {
            var stem = Path.GetFileNameWithoutExtension(saveBackup);
            int dot = stem.LastIndexOf('.');
            return new Backup(Path.GetDirectoryName(saveBackup)!, dot > 0 ? stem[..dot] : stem,
                dot > 0 ? stem[(dot + 1)..] : DateTime.Now.ToString("yyyyMMdd-HHmmss"));
        }
    }

    /// <summary>
    /// Moves the state in the ROM's selected slot, its preview, and the slot file (so the game starts from its
    /// save) into the save's backups. Returns how many files were moved.
    /// </summary>
    public static int MoveSelected(string sdRoot, string romsRoot, string romPath, Backup backup)
    {
        var rom = Path.GetFileName(romPath);
        string[] prefixes = [rom, Path.GetFileNameWithoutExtension(rom)];
        return SelectedFiles(sdRoot, romsRoot, romPath).Where(File.Exists).Count(f => Stash(f, Ending(f, prefixes), backup));
    }

    /// <summary>The file name past the ROM's name: ".st2", ".2.bmp", ".txt", ".state.auto".</summary>
    private static string Ending(string file, string[] prefixes)
    {
        var name = Path.GetFileName(file);
        foreach (var prefix in prefixes)
        {
            if (name.StartsWith(prefix + ".", StringComparison.Ordinal))
                return name[prefix.Length..];
        }
        return Path.GetExtension(name);
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

        var files = new List<string> { slotFile, Path.Combine(shared, ".minui", emu, $"{rom}.{slot}.bmp") };
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
                files.AddRange(names.Select(n => Path.Combine(dir, n)));
        }
        return files;
    }

    /// <summary>
    /// The DS pak's (NDS.pak, DraStic) auto save state, which DraStic loads when the game starts: in
    /// .userdata/shared/NDS-advanced-drastic, named "&lt;game&gt;_&lt;slot&gt;.dss" after the save ("&lt;game&gt;.dsv"), the
    /// slot being drastic.cfg's autosavestate_slot (10 unless changed). Moves it into <paramref name="backupDir"/>;
    /// returns how many files were moved.
    /// </summary>
    public static int MoveDraSticAuto(string sdRoot, string savePath, string? platform, Backup backup)
        => DraSticAuto(sdRoot, savePath, platform) is { } file && File.Exists(file) && Stash(file, ".dss", backup) ? 1 : 0;

    /// <summary>
    /// Moves a state file into the save's backup folder as "&lt;Name&gt;.&lt;Stamp&gt;&lt;ending&gt;", keeping the newest
    /// <see cref="BackupsToKeep"/> with that ending. If it can't be moved it's deleted, so the game still can't
    /// resume it.
    /// </summary>
    private static bool Stash(string file, string ending, Backup backup)
    {
        try
        {
            Directory.CreateDirectory(backup.Dir);
            var dest = Path.Combine(backup.Dir, $"{backup.Name}.{backup.Stamp}{ending}");
            for (int i = 1; File.Exists(dest); i++)
                dest = Path.Combine(backup.Dir, $"{backup.Name}.{backup.Stamp}-{i}{ending}");
            File.Move(file, dest);
            Prune(backup.Dir, backup.Name, ending);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine($"Couldn't back up {file}: {ex.Message}");
        }
        try
        {
            File.Delete(file);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine($"Couldn't delete {file}: {ex.Message}");
            return false;
        }
    }

    private static void Prune(string dir, string name, string ext)
    {
        var own = new System.Text.RegularExpressions.Regex(
            $@"^{System.Text.RegularExpressions.Regex.Escape(name)}\.\d{{8}}-\d{{6}}(-\d+)?{System.Text.RegularExpressions.Regex.Escape(ext)}$");
        var old = Directory.GetFiles(dir, $"{name}.*{ext}")
            .Where(f => own.IsMatch(Path.GetFileName(f)))
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .ThenByDescending(f => f, StringComparer.Ordinal)
            .Skip(BackupsToKeep);
        foreach (var f in old)
        {
            try { File.Delete(f); }
            catch (IOException) { /* best effort */ }
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
