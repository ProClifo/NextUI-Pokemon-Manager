namespace PokemonManager.Core;

/// <summary>
/// Saves the player has moved between the main list and Others with SELECT: one "path&lt;TAB&gt;main" or
/// "path&lt;TAB&gt;others" line per save in PokemonManager/save-lists.txt, the path relative to the SD card. Saves
/// moved to the main list with the earlier "Move to main list" (official-saves.txt, a path per line) count too.
/// </summary>
public sealed class SavePlacement(string file, string? legacyMainFile = null, string? sdRoot = null)
{
    private Dictionary<string, bool>? _main;

    private Dictionary<string, bool> Main => _main ??= Load();

    /// <summary>True: on the main list; false: in Others; null: where the ROM check puts it.</summary>
    public bool? OnMainList(string savePath) => Main.TryGetValue(Key(savePath), out var main) ? main : null;

    /// <summary>Throws IOException or UnauthorizedAccessException when the file can't be written.</summary>
    public void Set(string savePath, bool mainList)
    {
        Main[Key(savePath)] = mainList;
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(file))!);
        File.WriteAllLines(file, Main.Select(kv => $"{kv.Key}\t{(kv.Value ? "main" : "others")}"));
    }

    private string Key(string savePath)
    {
        if (sdRoot is null)
            return savePath;
        var relative = Path.GetRelativePath(Path.GetFullPath(sdRoot), Path.GetFullPath(savePath));
        return relative.StartsWith("..", StringComparison.Ordinal) || Path.IsPathRooted(relative) ? savePath : relative;
    }

    private Dictionary<string, bool> Load()
    {
        var main = new Dictionary<string, bool>();
        try
        {
            if (legacyMainFile is not null && File.Exists(legacyMainFile))
                foreach (var line in File.ReadAllLines(legacyMainFile).Where(l => l.Length > 0))
                    main[line] = true;
            if (File.Exists(file))
            {
                foreach (var line in File.ReadAllLines(file))
                {
                    int tab = line.IndexOf('\t');
                    if (tab > 0)
                        main[line[..tab]] = line[(tab + 1)..] == "main";
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
        return main;
    }
}
