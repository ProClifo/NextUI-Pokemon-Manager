using PKHeX.Core;

namespace PokemonManager.Core;

/// <summary>
/// A save file found on the SD card, along with the PKHeX object that parsed it.
/// </summary>
public sealed class SaveEntry(string path, SaveFile sav)
{
    public string Path { get; } = path;
    public SaveFile Sav { get; private set; } = sav;

    public string FileName => System.IO.Path.GetFileName(Path);
    public string Label => $"{Names.Game(Sav)} - {Sav.OT} ({FileName})";

    /// <summary>Re-reads the file from disk, discarding any unsaved in-memory changes.</summary>
    public bool Reload()
    {
        if (!SaveUtil.TryGetSaveFile(Path, out var fresh))
            return false;
        Sav = fresh;
        return true;
    }
}

/// <summary>
/// Finds, loads and writes save files. Every write is preceded by a timestamped backup.
/// </summary>
public sealed class SaveLibrary(string backupDir, int backupsToKeep = 999)
{
    // Extensions used by the emulators NextUI ships (gpSP/mGBA/Gambatte/DraStic) and common dumps.
    private static readonly HashSet<string> SaveExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".sav", ".srm", ".dsv", ".sa1", ".sa2", ".fla", ".sav1", ".dat", ".bin", ".gci", ".raw", ".main",
    };

    private const long MaxSaveSize = 8 * 1024 * 1024;

    public string BackupDir { get; } = backupDir;

    public static List<SaveEntry> Scan(IEnumerable<string> roots)
    {
        var result = new List<SaveEntry>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var root in roots)
        {
            if (!Directory.Exists(root))
                continue;
            IEnumerable<string> files;
            try
            {
                files = Directory.EnumerateFiles(root, "*", new EnumerationOptions
                {
                    RecurseSubdirectories = true,
                    IgnoreInaccessible = true,
                    AttributesToSkip = FileAttributes.System,
                });
            }
            catch (IOException) { continue; }

            foreach (var file in files)
            {
                if (!seen.Add(System.IO.Path.GetFullPath(file)))
                    continue;
                if (System.IO.Path.GetFileName(file).StartsWith('.'))
                    continue; // macOS resource forks and other dot files
                if (!SaveExtensions.Contains(System.IO.Path.GetExtension(file)))
                    continue;
                var info = new FileInfo(file);
                if (info.Length is 0 or > MaxSaveSize)
                    continue;
                if (SaveUtil.TryGetSaveFile(file, out var sav))
                    result.Add(new SaveEntry(file, sav));
            }
        }
        result.Sort((a, b) => string.Compare(a.Label, b.Label, StringComparison.OrdinalIgnoreCase));
        return result;
    }

    public static SaveEntry? Load(string path)
        => SaveUtil.TryGetSaveFile(path, out var sav) ? new SaveEntry(path, sav) : null;

    /// <summary>
    /// Backs up the file currently on disk, then writes the in-memory save over it.
    /// </summary>
    /// <returns>The path of the backup that was made.</returns>
    public string Write(SaveEntry entry)
    {
        var backup = Backup(entry.Path);
        var data = entry.Sav.Write();
        var tmp = entry.Path + ".pkmgr.tmp";
        File.WriteAllBytes(tmp, data.ToArray());
        File.Move(tmp, entry.Path, overwrite: true);
        return backup;
    }

    /// <summary>
    /// Copies the save into its own folder, Backups/&lt;save name&gt;/&lt;save name&gt;.&lt;time&gt;&lt;ext&gt;, keeping the newest
    /// <c>backupsToKeep</c>. A folder per save keeps each one small: FAT32 cards fit only about 10,000 long file
    /// names in one folder, and only this save's backups are listed to prune them.
    /// </summary>
    public string Backup(string path)
    {
        var name = System.IO.Path.GetFileNameWithoutExtension(path);
        var ext = System.IO.Path.GetExtension(path);
        var dir = System.IO.Path.Combine(BackupDir, name);
        Directory.CreateDirectory(dir);
        MoveFlatBackups(name, ext, dir);
        var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
        var dest = System.IO.Path.Combine(dir, $"{name}.{stamp}{ext}");
        for (int i = 1; File.Exists(dest); i++)
            dest = System.IO.Path.Combine(dir, $"{name}.{stamp}-{i}{ext}");
        File.Copy(path, dest);
        Prune(name, ext, dir);
        return dest;
    }

    // only this save's own backups: "X.sav" mustn't take "X.gba.<stamp>.sav"'s
    private static IEnumerable<string> OwnBackups(string dir, string name, string ext)
    {
        var own = new System.Text.RegularExpressions.Regex(
            $@"^{System.Text.RegularExpressions.Regex.Escape(name)}\.\d{{8}}-\d{{6}}(-\d+)?{System.Text.RegularExpressions.Regex.Escape(ext)}$");
        return Directory.GetFiles(dir, $"{name}.*{ext}").Where(f => own.IsMatch(System.IO.Path.GetFileName(f)));
    }

    /// <summary>Moves this save's backups from older versions, which kept them all in Backups itself, into its folder.</summary>
    private void MoveFlatBackups(string name, string ext, string dir)
    {
        foreach (var f in OwnBackups(BackupDir, name, ext))
        {
            try { File.Move(f, System.IO.Path.Combine(dir, System.IO.Path.GetFileName(f)), overwrite: false); }
            catch (IOException) { /* best effort */ }
        }
    }

    private void Prune(string name, string ext, string dir)
    {
        if (backupsToKeep <= 0)
            return;
        var old = OwnBackups(dir, name, ext)
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .Skip(backupsToKeep);
        foreach (var f in old)
        {
            try { File.Delete(f); }
            catch (IOException) { /* best effort */ }
        }
    }
}
