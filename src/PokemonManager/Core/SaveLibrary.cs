using PKHeX.Core;

namespace PokemonManager.Core;

/// <summary>
/// A save file found on the SD card, along with the PKHeX object that parsed it.
/// </summary>
public sealed class SaveEntry(string path, SaveFile sav, bool compressed = false)
{
    public string Path { get; } = path;
    public SaveFile Sav { get; private set; } = sav;

    /// <summary>RetroArch compressed it (<see cref="Rzip"/>): it's written back compressed.</summary>
    public bool Compressed { get; private set; } = compressed;

    public string FileName => System.IO.Path.GetFileName(Path);
    public string Label => $"{Names.Game(Sav)} - {Sav.OT} ({FileName})";

    /// <summary>Re-reads the file from disk, discarding any unsaved in-memory changes.</summary>
    public bool Reload()
    {
        if (!SaveLibrary.TryRead(Path, out var fresh, out var compressed))
            return false;
        Sav = fresh;
        Compressed = compressed;
        return true;
    }
}

/// <summary>
/// Finds, loads and writes save files. Every write is preceded by a timestamped backup.
/// </summary>
public sealed class SaveLibrary(string backupDir, int backupsToKeep = 32)
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
                if (TryRead(file, out var sav, out var compressed))
                    result.Add(new SaveEntry(file, sav, compressed));
            }
        }
        result.Sort((a, b) => string.Compare(a.Label, b.Label, StringComparison.OrdinalIgnoreCase));
        return result;
    }

    public static SaveEntry? Load(string path)
        => TryRead(path, out var sav, out var compressed) ? new SaveEntry(path, sav, compressed) : null;

    /// <summary>A save file as PKHeX reads it, RetroArch's compressed saves unpacked first.</summary>
    public static bool TryRead(string path, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out SaveFile? sav, out bool compressed)
    {
        sav = null;
        byte[] data;
        try
        {
            data = File.ReadAllBytes(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            compressed = false;
            return false;
        }
        compressed = Rzip.IsCompressed(data);
        if (compressed && Rzip.Decompress(data) is { } unpacked)
            data = unpacked;
        else if (compressed)
            return false;
        if (!SaveUtil.TryGetSaveFile(data, out sav, path))
            return false;
        // what PKHeX's SaveUtil.TryGetSaveFile(path) does after reading: Gen 1-3 saves don't store their language
        sav.Metadata.SetExtraInfo(path);
        if (sav.Generation <= 3)
            SaveLanguage.TryRevise(sav);
        return true;
    }

    /// <summary>
    /// Backs up the file currently on disk, then writes the in-memory save over it.
    /// </summary>
    /// <returns>The path of the backup that was made.</returns>
    public string Write(SaveEntry entry)
    {
        var backup = Backup(entry.Path, FolderName(entry.Sav));
        var data = entry.Sav.Write();
        var tmp = entry.Path + ".pkmgr.tmp";
        // as it was: RetroArch reads either, but whoever turned compression on gets their saves compressed
        File.WriteAllBytes(tmp, entry.Compressed ? Rzip.Compress(data.Span) : data.ToArray());
        File.Move(tmp, entry.Path, overwrite: true);
        return backup;
    }

    /// <summary>
    /// Copies the save into its own folder in Backups, named after the game, trainer and trainer ID
    /// ("Emerald - MAY - 12345"; <paramref name="folder"/>, else the file's name), keeping the newest
    /// <c>backupsToKeep</c> of this file's backups. A folder per save keeps each one small: FAT32 cards fit only
    /// about 10,000 long file names in one folder, and only this folder is listed to prune them.
    /// </summary>
    public string Backup(string path, string? folder = null)
    {
        var name = System.IO.Path.GetFileNameWithoutExtension(path);
        var ext = System.IO.Path.GetExtension(path);
        var dir = System.IO.Path.Combine(BackupDir, folder ?? name);
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

    /// <summary>"Game - TRAINER - 12345", made safe for a FAT32/exFAT folder name.</summary>
    public static string FolderName(SaveFile sav)
    {
        var parts = new[] { Names.Game(sav), sav.OT.Trim(), $"{sav.DisplayTID:D5}" }.Where(p => p.Length > 0);
        var name = string.Join(" - ", parts);
        var invalid = new HashSet<char>(System.IO.Path.GetInvalidFileNameChars()) { '"', '*', ':', '<', '>', '?', '\\', '|', '/' };
        name = new string(name.Select(c => invalid.Contains(c) || char.IsControl(c) ? '_' : c).ToArray()).TrimEnd('.', ' ');
        return name.Length == 0 ? "Save" : name;
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
