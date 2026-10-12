namespace PokemonManager.Core;

/// <summary>
/// Names given to saves on the ROM hacks list (Y), shown instead of the game's name: one "path&lt;TAB&gt;name"
/// line per save in PokemonManager/save-names.txt, the path relative to the SD card (so the names survive the
/// card being mounted elsewhere). An empty name removes it.
/// </summary>
public sealed class SaveNames(string file, string? sdRoot = null)
{
    private Dictionary<string, string>? _names;

    private Dictionary<string, string> Names => _names ??= Load();

    public string? Get(string savePath)
        => Names.TryGetValue(Key(savePath), out var name) || Names.TryGetValue(savePath, out name) ? name : null;

    /// <summary>Throws IOException or UnauthorizedAccessException when the file can't be written.</summary>
    public void Set(string savePath, string? name)
    {
        name = name?.Replace('\t', ' ').Replace('\n', ' ').Replace('\r', ' ').Trim();
        Names.Remove(savePath);
        if (string.IsNullOrEmpty(name))
            Names.Remove(Key(savePath));
        else
            Names[Key(savePath)] = name;
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(file))!);
        File.WriteAllLines(file, Names.Select(kv => $"{kv.Key}\t{kv.Value}"));
    }

    private string Key(string savePath)
    {
        if (sdRoot is null)
            return savePath;
        var relative = Path.GetRelativePath(Path.GetFullPath(sdRoot), Path.GetFullPath(savePath));
        return relative.StartsWith("..", StringComparison.Ordinal) || Path.IsPathRooted(relative) ? savePath : relative;
    }

    private Dictionary<string, string> Load()
    {
        var names = new Dictionary<string, string>();
        if (!File.Exists(file))
            return names;
        foreach (var line in File.ReadAllLines(file))
        {
            int tab = line.IndexOf('\t');
            if (tab > 0 && tab < line.Length - 1)
                names[line[..tab]] = line[(tab + 1)..];
        }
        return names;
    }
}
