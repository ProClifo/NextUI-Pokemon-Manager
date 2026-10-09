namespace PokemonManager.Core;

/// <summary>Orders saves by when their game was last played.</summary>
public static class SaveOrder
{
    /// <summary>
    /// The ROMs in NextUI's recently played list (.userdata/shared/.minui/recent.txt: one path per line relative
    /// to the SD card, optionally followed by a tab and a display name, newest first), as full paths.
    /// </summary>
    public static List<string> ReadRecents(string? recentFile, string? sdRoot)
    {
        var recents = new List<string>();
        if (recentFile is null || sdRoot is null || !File.Exists(recentFile))
            return recents;
        try
        {
            foreach (var line in File.ReadAllLines(recentFile))
            {
                var path = line.Split('\t')[0].Trim();
                if (path.Length != 0)
                    recents.Add(Path.GetFullPath(sdRoot + (path.StartsWith('/') ? path : "/" + path)));
            }
        }
        catch (IOException ex)
        {
            Console.Error.WriteLine($"Couldn't read {recentFile}: {ex.Message}");
        }
        return recents;
    }

    /// <summary>
    /// Saves whose game was played most recently first: by the place of the save's ROM in NextUI's recently
    /// played list, then (for games no longer in that list) by when the save file was last written.
    /// </summary>
    public static List<SaveEntry> ByLastPlayed(IEnumerable<SaveEntry> saves, Func<string, IReadOnlyList<string>> romsFor, IReadOnlyList<string> recents)
    {
        var rank = new Dictionary<string, int>(StringComparer.Ordinal);
        for (int i = 0; i < recents.Count; i++)
            rank.TryAdd(recents[i], i);
        return saves
            .Select(s => (Save: s, Rank: romsFor(s.Path).Select(r => rank.GetValueOrDefault(Path.GetFullPath(r), int.MaxValue)).DefaultIfEmpty(int.MaxValue).Min()))
            .OrderBy(x => x.Rank)
            .ThenByDescending(x => LastWrite(x.Save.Path))
            .Select(x => x.Save)
            .ToList();
    }

    private static DateTime LastWrite(string path)
    {
        try { return File.GetLastWriteTimeUtc(path); }
        catch (IOException) { return DateTime.MinValue; }
    }
}
