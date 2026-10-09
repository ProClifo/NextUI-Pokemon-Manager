using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using PKHeX.Core;
using PokemonManager.Gen3;

namespace PokemonManager.Core;

/// <summary>Language codes as Project Pokémon's EventsGallery writes them.</summary>
public static class GalleryLanguage
{
    public const string Japanese = "JPN";
    public const string English = "ENG";
    public const string French = "FRE";
    public const string Italian = "ITA";
    public const string German = "GER";
    public const string Spanish = "SPA";
    public const string Korean = "KOR";
    /// <summary>Gen 1-2 only: every non-Japanese, non-Korean release (they share one save format).</summary>
    public const string International = "INT";

    public static readonly string[] Tags = [Japanese, English, French, Italian, German, Spanish, Korean];

    public static string? FromLanguageId(int language) => (LanguageID)language switch
    {
        LanguageID.Japanese => Japanese,
        LanguageID.English => English,
        LanguageID.French => French,
        LanguageID.Italian => Italian,
        LanguageID.German => German,
        LanguageID.Spanish or LanguageID.SpanishL => Spanish,
        LanguageID.Korean => Korean,
        _ => null,
    };

    /// <summary>Gen 1-2 games only come in Japanese, Korean (Gen 2) and "international" save formats.</summary>
    public static string ForGeneration(string language, int generation)
        => generation <= 2 && language is not (Japanese or Korean) ? International : language;

    public static string Name(string code) => code switch
    {
        Japanese => "Japanese",
        English => "English",
        French => "French",
        Italian => "Italian",
        German => "German",
        Spanish => "Spanish",
        Korean => "Korean",
        International => "International",
        _ => code,
    };
}

public enum GalleryKind
{
    /// <summary>A Pokémon file (.pk1-.pk5): goes straight into a PC box.</summary>
    Pokemon,
    /// <summary>A Gen 4/5 Mystery Gift (.wc4 .pcd .pgt .pgf).</summary>
    Card,
    /// <summary>A Gen 3 Wonder Card, Mystery Event or e-Reader card.</summary>
    Gen3,
}

[Flags]
public enum GalleryFlags
{
    None = 0,
    /// <summary>Gives a key item that unlocks an in-game event (Aurora Ticket, Member Card...).</summary>
    EventItem = 1,
    /// <summary>A Pokémon file that passes PKHeX's legality check (released ones always do).</summary>
    FileLegal = 2,
}

/// <summary>One file from the gallery, as listed in the bundled index.</summary>
public sealed record GalleryEntry(
    string Path, int Generation, IReadOnlyList<string> Games, string? Language, bool Released,
    GalleryKind Kind, ushort Species, byte Form, GalleryFlags Flags, string Title)
{
    public bool IsEventItem => Flags.HasFlag(GalleryFlags.EventItem);
    public bool IsFileLegal => Flags.HasFlag(GalleryFlags.FileLegal);

    /// <summary>Folder path shown in the gallery: the gallery's own folders without "Released/Gen N".</summary>
    public string Folder
    {
        get
        {
            var parts = Path.Split('/');
            var folders = parts.Skip(2).Take(parts.Length - 3);
            return string.Join('/', Released ? folders : folders.Prepend("Unreleased"));
        }
    }

    public string ToLine() => string.Join('\t',
        Path, Generation, string.Join(',', Games), Language ?? "-", Released ? "R" : "U",
        Kind, Species, Form, (int)Flags, Title);

    public static GalleryEntry? FromLine(string line)
    {
        var f = line.Split('\t');
        if (f.Length != 10 || !int.TryParse(f[1], out var gen) || !Enum.TryParse<GalleryKind>(f[5], out var kind)
            || !ushort.TryParse(f[6], out var species) || !byte.TryParse(f[7], out var form) || !int.TryParse(f[8], out var flags))
            return null;
        return new GalleryEntry(f[0], gen, f[2].Length == 0 ? [] : f[2].Split(','), f[3] == "-" ? null : f[3], f[4] == "R",
            kind, species, form, (GalleryFlags)flags, f[9]);
    }
}

/// <summary>A gallery file read into the object that can be given to a save.</summary>
public sealed record GalleryGift(PKM? Pokemon, DataMysteryGift? Card, Gen3EventFile? Gen3)
{
    public static readonly string[] Extensions =
        [".pk1", ".pk2", ".pk3", ".pk4", ".pk5", ".wc3", ".me3", ".ect", ".ecb", ".wc4", ".pcd", ".pgt", ".pgf"];

    public static GalleryGift? Load(string path, byte[] data)
    {
        var ext = System.IO.Path.GetExtension(path).ToLowerInvariant();
        try
        {
            if (ext.StartsWith(".pk"))
            {
                int gen = ext[^1] - '0';
                return FileUtil.TryGetPKM(data, out var pk, ext) && pk.Format == gen && pk.Species > 0
                    ? new GalleryGift(pk, null, null)
                    : null;
            }
            if (Gen3Events.Extensions.Contains(ext))
                return Gen3Events.Parse(path, data) is { } g3 ? new GalleryGift(null, null, g3) : null;
            return MysteryGift.GetMysteryGift(data, ext) is DataMysteryGift card && !card.IsEmpty
                ? new GalleryGift(null, card, null)
                : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    public GalleryKind Kind => Pokemon is not null ? GalleryKind.Pokemon : Card is not null ? GalleryKind.Card : GalleryKind.Gen3;

    public ushort Species => Pokemon?.Species ?? (Card is { IsEntity: true } c ? c.Species : (ushort)0);

    public byte Form => Pokemon?.Form ?? (Card is { IsEntity: true } c ? c.Form : (byte)0);
}

/// <summary>
/// Reads file names from Project Pokémon's EventsGallery. Names follow
/// "[card id] &lt;games&gt; - &lt;title&gt; (&lt;LANG&gt;) (region).ext", e.g. "018 Pt - Item Member Card (ENG).wc4".
/// </summary>
public static partial class GalleryNames
{
    // Game letters per generation, longest first so "Pt" isn't read as "P" + "t".
    private static readonly Dictionary<int, (string Tag, string[] Games)[]> GameTokens = new()
    {
        [1] = [("R", ["R"]), ("G", ["G"]), ("B", ["B"]), ("Y", ["Y"])],
        [2] = [("G", ["G"]), ("S", ["S"]), ("C", ["C"])],
        [3] = [("FRLG", ["FR", "LG"]), ("FR", ["FR"]), ("LG", ["LG"]), ("F", ["FR"]), ("L", ["LG"]), ("R", ["R"]), ("S", ["S"]), ("E", ["E"])],
        [4] = [("HGSS", ["HG", "SS"]), ("HG", ["HG"]), ("SS", ["SS"]), ("Pt", ["Pt"]), ("D", ["D"]), ("P", ["P"])],
        [5] = [("B2", ["B2"]), ("W2", ["W2"]), ("B", ["B"]), ("W", ["W"])],
    };

    private static readonly string[] EventItems =
    [
        "eonticket", "auroraticket", "mysticticket", "oldseamap",
        "membercard", "oaksletter", "secretkey", "azureflute", "enigmastone", "libertypass",
    ];

    [GeneratedRegex(@"^(?:[-\d]+|\d+-[A-Z]\d+)\s+")]
    private static partial Regex LeadingId();

    [GeneratedRegex(@"\s*\((ENG|JPN|GER|FRE|ITA|SPA|KOR)\)")]
    private static partial Regex LanguageTag();

    [GeneratedRegex(@"\s*\(([0-9A-Fa-f]{4,12}|\d+ of \d+|\d+|NA|UK|AU|AUS|US|USA|EU|EUR|Hex Extracted)\)")]
    private static partial Regex VariantTag();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Spaces();

    [GeneratedRegex(@"\([^()]*\)")]
    private static partial Regex Parenthesized();

    // Trainer IDs written into a title, e.g. "PCNYb 0510 Shiny Raikou".
    [GeneratedRegex(@"(?<!\S)\d{3,5}(?!\S)")]
    private static partial Regex StandaloneNumber();

    /// <summary>Splits a file name into the games it's for (empty = any) and a readable title.</summary>
    public static (List<string> Games, string Title) Parse(string fileName, int generation)
    {
        var name = System.IO.Path.GetFileNameWithoutExtension(fileName).Trim();
        name = LeadingId().Replace(name, "");
        var games = new List<string>();
        int dash = name.IndexOf(" - ", StringComparison.Ordinal);
        if (dash >= 0 && ParseGames(name[..dash], generation) is { } parsed)
        {
            games = parsed;
            name = name[(dash + 3)..];
        }
        name = name.TrimStart('-', ' ');
        name = Spaces().Replace(LanguageTag().Replace(name, ""), " ").Trim();
        return (games, name.Length == 0 ? System.IO.Path.GetFileNameWithoutExtension(fileName) : name);
    }

    public static List<string>? ParseGames(string tag, int generation)
    {
        var text = tag.Replace("[LW]", "").Replace("Debug", "").Replace(" ", "");
        if (text.Length == 0 || !GameTokens.TryGetValue(generation, out var tokens))
            return null;
        var games = new List<string>();
        int i = 0;
        while (i < text.Length)
        {
            var match = tokens.FirstOrDefault(t => string.CompareOrdinal(text, i, t.Tag, 0, t.Tag.Length) == 0);
            if (match.Tag is null)
                return null;
            games.AddRange(match.Games.Where(g => !games.Contains(g)));
            i += match.Tag.Length;
        }
        return games;
    }

    /// <summary>Language from the file name, else from a language folder ("ENG", "Japanese", "Debug GS Mons (JPN)"...).</summary>
    public static string? LanguageFromPath(string relativePath)
    {
        var parts = relativePath.Split('/');
        if (LanguageTag().Match(parts[^1]) is { Success: true } inName)
            return inName.Groups[1].Value;
        for (int i = parts.Length - 2; i >= 0; i--)
        {
            var folder = parts[i];
            if (GalleryLanguage.Tags.Contains(folder))
                return folder;
            var known = folder switch
            {
                "Japanese" => GalleryLanguage.Japanese,
                "English" => GalleryLanguage.English,
                "Korean" => GalleryLanguage.Korean,
                "International" => GalleryLanguage.International,
                _ => null,
            };
            if (known is not null)
                return known;
            if (LanguageTag().Match(folder) is { Success: true } inFolder)
                return inFolder.Groups[1].Value;
        }
        return null;
    }

    public static bool IsEventItem(string title) => EventItems.Any(Letters(title).Contains);

    /// <summary>Title without per-copy IDs and regions, so the copies of one distribution group together.</summary>
    public static string GroupKey(string title)
        => Spaces().Replace(StandaloneNumber().Replace(VariantTag().Replace(title, ""), ""), " ").Trim();

    /// <summary>
    /// The name the menus show: the group key without anything in parentheses either (IDs, regions, berries,
    /// notes). Copies with the same display title are listed once; one of them is picked when it's chosen.
    /// </summary>
    public static string DisplayTitle(string title)
    {
        var shown = Spaces().Replace(Parenthesized().Replace(GroupKey(title), " "), " ").Trim();
        return shown.Length > 0 ? shown : title;
    }

    private static string Letters(string text) => new(text.ToLowerInvariant().Where(char.IsAsciiLetter).ToArray());
}

/// <summary>Builds the bundled gallery (res/gallery.zip) from a checkout of the EventsGallery repository.</summary>
public static class GalleryBuilder
{
    public const string IndexName = "index.tsv";
    public const string EventOnlyName = "eventonly.tsv";
    public const string FilesPrefix = "files/";

    private static readonly string[] SkippedFolders = ["hex extracted cards", "Wondercard Fulls"];

    /// <summary>
    /// Bundles every file PKHeX can read. Released Pokémon files that PKHeX's legality check rejects are
    /// left out, so the menus only hand out legal Pokémon; unreleased (debug/test) files are kept as they are.
    /// </summary>
    public static (int Added, int Skipped) Build(string galleryRoot, string outZip, TextWriter? log = null)
    {
        Legality.UseCartridgeEra();
        var entries = new List<(GalleryEntry Entry, string Source)>();
        int skipped = 0;
        foreach (var top in new[] { "Released", "Unreleased" })
        {
            for (int gen = 1; gen <= 5; gen++)
            {
                var dir = Path.Combine(galleryRoot, top, $"Gen {gen}");
                if (!Directory.Exists(dir))
                    continue;
                foreach (var file in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
                {
                    var rel = Path.GetRelativePath(galleryRoot, file).Replace('\\', '/');
                    if (!GalleryGift.Extensions.Contains(Path.GetExtension(file).ToLowerInvariant())
                        || rel.Split('/').Any(p => SkippedFolders.Contains(p, StringComparer.OrdinalIgnoreCase)))
                        continue;
                    var entry = Describe(rel, gen, top == "Released", File.ReadAllBytes(file), out var problem);
                    if (entry is null)
                    {
                        skipped++;
                        log?.WriteLine($"skipped ({problem}): {rel}");
                        continue;
                    }
                    entries.Add((entry, file));
                }
            }
        }

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outZip))!);
        File.Delete(outZip);
        using (var zip = ZipFile.Open(outZip, ZipArchiveMode.Create))
        {
            var index = zip.CreateEntry(IndexName, CompressionLevel.SmallestSize);
            using (var writer = new StreamWriter(index.Open(), new UTF8Encoding(false)))
            {
                foreach (var (entry, _) in entries)
                    writer.Write(entry.ToLine() + "\n");
            }
            var table = zip.CreateEntry(EventOnlyName, CompressionLevel.SmallestSize);
            using (var writer = new StreamWriter(table.Open(), new UTF8Encoding(false)))
            {
                foreach (var line in EventOnlyTable(entries.Select(e => e.Entry), log))
                    writer.Write(line + "\n");
            }
            foreach (var (entry, source) in entries)
                zip.CreateEntryFromFile(source, FilesPrefix + entry.Path, CompressionLevel.SmallestSize);
        }
        return (entries.Count, skipped);
    }

    /// <summary>
    /// Which of the gallery's Pokémon are event-only, per generation and language ("gen \t lang \t species-form,...").
    /// Candidates are the mythical, legendary and alternate-form Pokémon in released files; every other species
    /// can be caught or bred in the handheld games.
    /// </summary>
    private static IEnumerable<string> EventOnlyTable(IEnumerable<GalleryEntry> entries, TextWriter? log)
    {
        var candidates = entries
            .Where(e => e.Released && e.Species > 0 && (e.Form > 0 || SpeciesCategory.IsMythical(e.Species)
                        || SpeciesCategory.IsLegendary(e.Species) || SpeciesCategory.IsSubLegendary(e.Species)))
            .GroupBy(e => e.Generation)
            .ToDictionary(g => g.Key, g => g.Select(e => (e.Species, EventOnly.KeyForm(e.Species, e.Form, e.Generation))).Distinct().ToList());
        foreach (var (gen, list) in candidates.OrderBy(c => c.Key))
        {
            foreach (var language in EventOnly.Languages(gen))
            {
                var only = EventOnly.Compute(gen, language, list);
                log?.WriteLine($"event-only, Gen {gen} {language}: {string.Join(", ", only.Select(o => o.Form == 0 ? $"{(Species)o.Species}" : $"{(Species)o.Species}-{o.Form}"))}");
                yield return $"{gen}\t{language}\t{string.Join(',', only.Select(o => $"{o.Species}-{o.Form}"))}";
            }
        }
    }

    public static GalleryEntry? Describe(string relativePath, int generation, bool released, byte[] data, out string problem)
    {
        problem = "";
        var gift = GalleryGift.Load(relativePath, data);
        if (gift is null)
        {
            problem = "unreadable";
            return null;
        }
        if (released && gift.Pokemon is { } file && !Legality.IsLegal(file))
        {
            problem = $"illegal: {Legality.FirstProblem(file)}";
            return null;
        }
        var (games, title) = GalleryNames.Parse(Path.GetFileName(relativePath), generation);
        var language = GalleryNames.LanguageFromPath(relativePath)
                       ?? (gift.Pokemon is { } pk ? GalleryLanguage.FromLanguageId(pk.Language) : null);
        if (language is not null)
            language = GalleryLanguage.ForGeneration(language, generation);

        var flags = GalleryFlags.None;
        if (gift.Kind != GalleryKind.Pokemon && GalleryNames.IsEventItem(title))
            flags |= GalleryFlags.EventItem;
        if (gift.Pokemon is { } legalCheck && (released || Legality.IsLegal(legalCheck)))
            flags |= GalleryFlags.FileLegal;
        return new GalleryEntry(relativePath, generation, games, language, released, gift.Kind, gift.Species, gift.Form, flags, title);
    }
}

/// <summary>The bundled gallery: an index plus every file, in one zip so the SD card holds a single file.</summary>
public sealed class GalleryArchive(string zipPath) : IDisposable
{
    private List<GalleryEntry>? _entries;
    private Dictionary<(int, string), HashSet<(ushort, byte)>>? _eventOnly;

    public string ZipPath { get; } = zipPath;

    /// <summary>Whether this Pokémon can only be obtained from an event in games of the given generation and language.</summary>
    public bool IsEventOnly(int generation, string language, ushort species, byte form)
        => (_eventOnly ??= LoadEventOnly()).TryGetValue((generation, language), out var set)
           && set.Contains((species, EventOnly.KeyForm(species, form, generation)));

    private Dictionary<(int, string), HashSet<(ushort, byte)>> LoadEventOnly()
    {
        var result = new Dictionary<(int, string), HashSet<(ushort, byte)>>();
        if (!Exists)
            return result;
        using var zip = ZipFile.OpenRead(ZipPath);
        var table = zip.GetEntry(GalleryBuilder.EventOnlyName);
        if (table is null)
            return result;
        using var reader = new StreamReader(table.Open(), Encoding.UTF8);
        while (reader.ReadLine() is { } line)
        {
            var f = line.Split('\t');
            if (f.Length != 3 || !int.TryParse(f[0], out var gen))
                continue;
            var set = new HashSet<(ushort, byte)>();
            foreach (var item in f[2].Split(',', StringSplitOptions.RemoveEmptyEntries))
            {
                var parts = item.Split('-');
                if (parts.Length == 2 && ushort.TryParse(parts[0], out var species) && byte.TryParse(parts[1], out var form))
                    set.Add((species, form));
            }
            result[(gen, f[1])] = set;
        }
        return result;
    }

    public bool Exists => File.Exists(ZipPath);

    public IReadOnlyList<GalleryEntry> Entries => _entries ??= LoadIndex();

    private List<GalleryEntry> LoadIndex()
    {
        if (!Exists)
            return [];
        using var zip = ZipFile.OpenRead(ZipPath);
        var index = zip.GetEntry(GalleryBuilder.IndexName);
        if (index is null)
            return [];
        using var reader = new StreamReader(index.Open(), Encoding.UTF8);
        var list = new List<GalleryEntry>();
        while (reader.ReadLine() is { } line)
        {
            if (GalleryEntry.FromLine(line) is { } entry)
                list.Add(entry);
        }
        return list;
    }

    public void Dispose() => _zip?.Dispose();

    // Kept open: reading the zip's directory of ~7,000 entries each time would be slow on the handheld.
    private ZipArchive? _zip;

    public GalleryGift? Load(GalleryEntry entry)
    {
        var zip = _zip ??= ZipFile.OpenRead(ZipPath);
        var file = zip.GetEntry(GalleryBuilder.FilesPrefix + entry.Path);
        if (file is null)
            return null;
        using var stream = file.Open();
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return GalleryGift.Load(entry.Path, buffer.ToArray());
    }
}

/// <summary>
/// Which game and language a save is, as the gallery names them. The ROM header is the most precise
/// source (it tells Ruby from Sapphire and gives the language); the save fills in what it can.
/// </summary>
public sealed record GameProfile(int Generation, IReadOnlyList<string> Games, string Language)
{
    private static readonly Dictionary<string, string> RomGames = new()
    {
        ["AXV"] = "R", ["AXP"] = "S", ["BPE"] = "E", ["BPR"] = "FR", ["BPG"] = "LG",
        ["ADA"] = "D", ["APA"] = "P", ["CPU"] = "Pt", ["IPK"] = "HG", ["IPG"] = "SS",
        ["IRB"] = "B", ["IRA"] = "W", ["IRE"] = "B2", ["IRD"] = "W2",
    };

    public static GameProfile For(SaveFile sav, IEnumerable<string> roms)
    {
        int gen = sav.Generation;
        string? romGame = null, romLanguage = null;
        foreach (var rom in roms)
        {
            if (RomHeader.ReadGameCode(rom) is { } code && RomGames.TryGetValue(code[..3], out var game))
            {
                romGame = game;
                romLanguage = RomHeader.Language(code[3]);
                break;
            }
        }

        var games = romGame is not null && GameGeneration(romGame) == gen ? [romGame] : GamesFromSave(sav);
        var language = (romGame is not null ? romLanguage : null) ?? LanguageFromSave(sav);
        return new GameProfile(gen, games, GalleryLanguage.ForGeneration(language, gen));
    }

    private static int GameGeneration(string game) => game switch
    {
        "R" or "S" or "E" or "FR" or "LG" => 3,
        "D" or "P" or "Pt" or "HG" or "SS" => 4,
        _ => 5,
    };

    private static List<string> GamesFromSave(SaveFile sav) => sav.Version switch
    {
        GameVersion.YW => ["Y"],
        GameVersion.RD or GameVersion.BU or GameVersion.GN or GameVersion.RB => ["R", "G", "B"],
        GameVersion.GD => ["G"],
        GameVersion.SI => ["S"],
        GameVersion.GS => ["G", "S"],
        GameVersion.C => ["C"],
        GameVersion.R => ["R"],
        GameVersion.S => ["S"],
        GameVersion.RS => ["R", "S"],
        GameVersion.E => ["E"],
        GameVersion.FR => ["FR"],
        GameVersion.LG => ["LG"],
        GameVersion.FRLG => ["FR", "LG"],
        GameVersion.D => ["D"],
        GameVersion.P => ["P"],
        GameVersion.DP => ["D", "P"],
        GameVersion.Pt => ["Pt"],
        GameVersion.HG => ["HG"],
        GameVersion.SS => ["SS"],
        GameVersion.HGSS => ["HG", "SS"],
        GameVersion.B => ["B"],
        GameVersion.W => ["W"],
        GameVersion.BW => ["B", "W"],
        GameVersion.B2 => ["B2"],
        GameVersion.W2 => ["W2"],
        GameVersion.B2W2 => ["B2", "W2"],
        _ => [],
    };

    private static string LanguageFromSave(SaveFile sav)
    {
        if (sav is SAV2 { Korean: true })
            return GalleryLanguage.Korean;
        if (sav is SAV1 { Japanese: true } or SAV2 { Japanese: true } or SAV3 { Japanese: true })
            return GalleryLanguage.Japanese;
        if (sav.Generation >= 4 && GalleryLanguage.FromLanguageId(sav.Language) is { } stored)
            return stored;
        // Gen 3 saves don't record their language: go by the player's own Pokémon.
        var own = SlotRef.AllOccupied(sav)
            .Select(s => s.Get(sav))
            .Where(pk => pk.TID16 == sav.TID16 && pk.SID16 == sav.SID16 && !pk.IsEgg)
            .Select(pk => GalleryLanguage.FromLanguageId(pk.Language))
            .Where(l => l is not null)
            .GroupBy(l => l)
            .OrderByDescending(g => g.Count())
            .FirstOrDefault();
        return own?.Key ?? GalleryLanguage.English;
    }

    /// <summary>Whether a gallery file belongs in this game's lists.</summary>
    public bool Matches(GalleryEntry entry, bool allLanguages, bool unreleased)
    {
        if (entry.Generation != Generation || (!entry.Released && !unreleased))
            return false;
        // Pokémon can be traded between the games of a generation; cards only work in the games they name.
        if (entry.Kind != GalleryKind.Pokemon && entry.Games.Count != 0 && Games.Count != 0 && !entry.Games.Intersect(Games).Any())
            return false;
        return allLanguages || entry.Language is null || entry.Language == Language;
    }
}

/// <summary>The curated lists on a game's menu, picked from the gallery.</summary>
public static class GalleryLists
{
    /// <summary>
    /// Released distributions of Pokémon that can't be obtained legally any other way in this game's
    /// language. The gallery keeps every known copy of a distribution (e.g. hundreds of MYSTRY Mew), which
    /// differ only in PID/IVs; the list shows each distribution once.
    /// </summary>
    public static List<GalleryEntry> Distributions(GalleryArchive gallery, GameProfile profile)
        => gallery.Entries
            .Where(e => e.Species > 0 && profile.Matches(e, allLanguages: false, unreleased: false)
                        && gallery.IsEventOnly(e.Generation, profile.Language, e.Species, e.Form))
            .GroupBy(e => GalleryNames.DisplayTitle(e.Title), StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First() with { Title = g.Key })
            .OrderBy(e => e.Species)
            .ThenBy(e => e.Title, StringComparer.OrdinalIgnoreCase)
            .ToList();
}

/// <summary>Folder navigation over a filtered set of gallery files.</summary>
public static class GalleryTree
{
    /// <summary>One menu entry: the copies of a distribution in one folder that share a display title.</summary>
    public sealed record Item(string Title, string? Language, List<GalleryEntry> Copies);

    public sealed record View(string Path, List<string> Folders, List<Item> Items);

    /// <summary>
    /// The folders and items inside <paramref name="folder"/>. A folder holding nothing but one other folder
    /// (often a language folder once the list is filtered to one language) is skipped through, and a folder
    /// with only one item in it shows that item here instead.
    /// </summary>
    public static View Open(IReadOnlyList<GalleryEntry> files, string folder)
    {
        while (true)
        {
            var prefix = folder.Length == 0 ? "" : folder + "/";
            var folders = new List<string>();
            var direct = new List<GalleryEntry>();
            foreach (var e in files)
            {
                var f = e.Folder;
                if (f == folder)
                    direct.Add(e);
                else if (f.StartsWith(prefix, StringComparison.Ordinal))
                {
                    var child = prefix + f[prefix.Length..].Split('/')[0];
                    if (!folders.Contains(child))
                        folders.Add(child);
                }
            }
            var items = Group(direct);
            foreach (var child in folders.ToList())
            {
                var inside = Group(files.Where(e => e.Folder == child || e.Folder.StartsWith(child + "/", StringComparison.Ordinal)));
                if (inside.Count == 1)
                {
                    items.Add(inside[0]);
                    folders.Remove(child);
                }
            }
            if (folders.Count == 1 && items.Count == 0)
            {
                folder = folders[0];
                continue;
            }
            folders = folders
                .OrderBy(f => f == "Unreleased")
                .ThenBy(f => f, StringComparer.OrdinalIgnoreCase)
                .ToList();
            items.Sort((a, b) => string.Compare(a.Title, b.Title, StringComparison.OrdinalIgnoreCase));
            return new View(folder, folders, items);
        }
    }

    /// <summary>Groups copies of the same distribution (same folder, language and display title).</summary>
    private static List<Item> Group(IEnumerable<GalleryEntry> files)
        => files
            .GroupBy(e => (e.Folder, e.Language, Title: GalleryNames.DisplayTitle(e.Title).ToUpperInvariant()))
            .Select(g => new Item(GalleryNames.DisplayTitle(g.First().Title), g.Key.Language, g.ToList()))
            .ToList();

    public static string Name(string folder) => folder[(folder.LastIndexOf('/') + 1)..];
}
