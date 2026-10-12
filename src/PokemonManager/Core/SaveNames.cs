namespace PokemonManager.Core;

/// <summary>
/// Names given to saves on the ROM hacks list (Y), shown instead of the game's name: one "path&lt;TAB&gt;name"
/// line per save in PokemonManager/save-names.txt. An empty name removes it.
/// </summary>
public sealed class SaveNames(string file)
{
    private Dictionary<string, string>? _names;

    private Dictionary<string, string> Names => _names ??= Load();

    public string? Get(string savePath) => Names.TryGetValue(savePath, out var name) ? name : null;

    public void Set(string savePath, string? name)
    {
        name = name?.Replace('\t', ' ').Replace('\n', ' ').Replace('\r', ' ').Trim();
        if (string.IsNullOrEmpty(name))
            Names.Remove(savePath);
        else
            Names[savePath] = name;
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(file))!);
        File.WriteAllLines(file, Names.Select(kv => $"{kv.Key}\t{kv.Value}"));
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
