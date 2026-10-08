using System.IO.Compression;
using System.IO.Hashing;

namespace PokemonManager.Core;

public enum RomStatus
{
    /// <summary>The save's ROM is a retail dump of a main-series game.</summary>
    Official,
    /// <summary>The save's ROM was found but isn't a known retail dump (ROM hack, translation, bad dump...).</summary>
    Unofficial,
    /// <summary>No ROM with a matching name was found.</summary>
    RomNotFound,
}

public sealed record RomCheck(RomStatus Status, string? RomPath, string? DumpName)
{
    public string Describe() => Status switch
    {
        RomStatus.Official => $"official ROM: {DumpName}",
        RomStatus.Unofficial => $"ROM isn't an official release ({Path.GetFileName(RomPath)})",
        _ => "no matching ROM found in Roms",
    };
}

/// <summary>
/// Decides whether a save belongs to an unmodified ("vanilla") Pokémon ROM by finding the ROM it was
/// made with and comparing its CRC32 against the No-Intro retail dumps in <see cref="Dumps"/>.
/// Results are cached by path, size and modification time, so each ROM is only hashed once.
/// </summary>
internal static partial class VanillaRoms
{
    private static readonly string[] RomExtensions = [".gb", ".gbc", ".gba", ".nds", ".zip"];
    // Lazy: field initialisers in the generated partial file may run after these would.
    private static readonly Lazy<Dictionary<uint, string>> ByCrcLazy = new(() => Dumps.GroupBy(d => d.Crc).ToDictionary(g => g.Key, g => g.First().Name));
    private static readonly Lazy<long[]> DsSizesLazy = new(() => Dumps.Where(d => d.Size >= 32 * 1024 * 1024).Select(d => d.Size).Distinct().Order().ToArray());
    private static Dictionary<uint, string> ByCrc => ByCrcLazy.Value;
    private static long[] DsSizes => DsSizesLazy.Value;

    public static int Count => Dumps.Length;

    public static bool IsOfficialCrc(uint crc, out string name) => ByCrc.TryGetValue(crc, out name!);

    /// <summary>
    /// Index of every ROM under the Roms folder, by file name with and without extension. NextUI names a
    /// save "&lt;rom file name&gt;.sav" (or "&lt;rom name&gt;.sav"/".srm" when the save format is changed), and for
    /// zipped ROMs it may use the name of the file inside the zip.
    /// </summary>
    public sealed class Index
    {
        private readonly Dictionary<string, List<string>> _byName = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, (uint Crc, string Name)?> _cache = new(StringComparer.Ordinal);
        private readonly string? _cacheFile;

        public Index(IEnumerable<string> romRoots, string? cacheFile)
        {
            _cacheFile = cacheFile;
            LoadCache();
            foreach (var root in romRoots.Where(Directory.Exists))
            {
                var files = Directory.EnumerateFiles(root, "*", new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true });
                foreach (var file in files)
                {
                    var name = Path.GetFileName(file);
                    if (name.StartsWith('.') || !RomExtensions.Contains(Path.GetExtension(file).ToLowerInvariant()))
                        continue;
                    Add(name, file);
                    Add(Path.GetFileNameWithoutExtension(name), file);
                    if (name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                    {
                        foreach (var inner in ZipRomNames(file))
                        {
                            Add(inner, file);
                            Add(Path.GetFileNameWithoutExtension(inner), file);
                        }
                    }
                }
            }
        }

        private void Add(string key, string path)
        {
            if (!_byName.TryGetValue(key, out var list))
                _byName[key] = list = [];
            if (!list.Contains(path))
                list.Add(path);
        }

        /// <summary>ROMs whose file name (or zipped file name) matches the save's.</summary>
        public IReadOnlyList<string> FindRoms(string savePath)
            => _byName.GetValueOrDefault(Path.GetFileNameWithoutExtension(savePath)) ?? [];

        public RomCheck Check(string savePath)
        {
            var candidates = FindRoms(savePath);
            if (candidates.Count == 0)
                return new RomCheck(RomStatus.RomNotFound, null, null);

            foreach (var rom in candidates)
            {
                if (Identify(rom) is { } match)
                    return new RomCheck(RomStatus.Official, rom, match);
            }
            return new RomCheck(RomStatus.Unofficial, candidates[0], null);
        }

        private string? Identify(string rom)
        {
            var info = new FileInfo(rom);
            var key = $"{rom}|{info.Length}|{info.LastWriteTimeUtc.Ticks}";
            if (!_cache.TryGetValue(key, out var hit))
            {
                hit = Hash(rom);
                _cache[key] = hit;
                SaveCache();
            }
            return hit?.Name;
        }

        private static (uint, string)? Hash(string rom)
        {
            try
            {
                if (rom.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                {
                    using var zip = ZipFile.OpenRead(rom);
                    foreach (var entry in zip.Entries)
                    {
                        if (!IsRomName(entry.Name))
                            continue;
                        // The zip directory already stores the CRC; only decompress for trimmed DS ROMs.
                        if (IsOfficialCrc(entry.Crc32, out var name))
                            return (entry.Crc32, name);
                        if (entry.Name.EndsWith(".nds", StringComparison.OrdinalIgnoreCase))
                        {
                            using var stream = entry.Open();
                            if (HashStream(stream, entry.Length, padDs: true) is { } padded)
                                return padded;
                        }
                    }
                    return null;
                }

                using var file = File.OpenRead(rom);
                return HashStream(file, file.Length, padDs: rom.EndsWith(".nds", StringComparison.OrdinalIgnoreCase));
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
            {
                return null;
            }
        }

        private static List<string> ZipRomNames(string zipPath)
        {
            try
            {
                using var zip = ZipFile.OpenRead(zipPath);
                return zip.Entries.Where(e => IsRomName(e.Name)).Select(e => e.Name).ToList();
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
            {
                return [];
            }
        }

        private static bool IsRomName(string name)
            => RomExtensions.Contains(Path.GetExtension(name).ToLowerInvariant()) && !name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase);

        private void LoadCache()
        {
            if (_cacheFile is null || !File.Exists(_cacheFile))
                return;
            try
            {
                foreach (var line in File.ReadLines(_cacheFile))
                {
                    var parts = line.Split('\t');
                    if (parts.Length != 2)
                        continue;
                    _cache[parts[0]] = parts[1].Length == 8 && uint.TryParse(parts[1], System.Globalization.NumberStyles.HexNumber, null, out var crc) && IsOfficialCrc(crc, out var name)
                        ? (crc, name)
                        : null;
                }
            }
            catch (IOException) { /* rebuild */ }
        }

        private void SaveCache()
        {
            if (_cacheFile is null)
                return;
            try
            {
                File.WriteAllLines(_cacheFile, _cache.Select(kv => $"{kv.Key}\t{(kv.Value is { } v ? v.Crc.ToString("X8") : "-")}"));
            }
            catch (IOException) { /* a cache is optional */ }
        }
    }

    /// <summary>
    /// CRC32 of a ROM. Trimmed DS ROMs (padding removed) are checked as if their 0xFF padding were still
    /// there, at each retail cartridge size.
    /// </summary>
    internal static (uint Crc, string Name)? HashStream(Stream stream, long length, bool padDs)
    {
        var crc = new Crc32();
        var buffer = new byte[1 << 20];
        int read;
        while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
            crc.Append(buffer.AsSpan(0, read));

        uint value = crc.GetCurrentHashAsUInt32();
        if (IsOfficialCrc(value, out var name))
            return (value, name);
        if (!padDs)
            return null;

        buffer.AsSpan().Fill(0xFF);
        long position = length;
        foreach (var size in DsSizes)
        {
            if (size <= position)
                continue;
            while (position < size)
            {
                int chunk = (int)Math.Min(buffer.Length, size - position);
                crc.Append(buffer.AsSpan(0, chunk));
                position += chunk;
            }
            value = crc.GetCurrentHashAsUInt32();
            if (IsOfficialCrc(value, out name))
                return (value, name);
        }
        return null;
    }
}

/// <summary>
/// Splits scanned saves into those made with official ROMs and the rest. Saves the user put in
/// PokemonManager/Saves have no ROM on the card and are always kept.
/// </summary>
public static class OfficialRomFilter
{
    public sealed record Hidden(SaveEntry Save, RomCheck Check);

    public static (List<SaveEntry> Shown, List<Hidden> Hidden) Apply(
        IEnumerable<SaveEntry> saves, IEnumerable<string> romRoots, string? cacheFile, string? exemptDir)
        => Apply(saves, new VanillaRoms.Index(romRoots, cacheFile), exemptDir);

    internal static (List<SaveEntry> Shown, List<Hidden> Hidden) Apply(IEnumerable<SaveEntry> saves, VanillaRoms.Index index, string? exemptDir)
    {
        var exempt = exemptDir is null ? null : Path.GetFullPath(exemptDir) + Path.DirectorySeparatorChar;
        var shown = new List<SaveEntry>();
        var hidden = new List<Hidden>();
        foreach (var save in saves)
        {
            if (exempt is not null && Path.GetFullPath(save.Path).StartsWith(exempt, StringComparison.Ordinal))
            {
                shown.Add(save);
                continue;
            }
            var check = index.Check(save.Path);
            if (check.Status == RomStatus.Official)
                shown.Add(save);
            else
                hidden.Add(new Hidden(save, check));
        }
        return (shown, hidden);
    }
}
