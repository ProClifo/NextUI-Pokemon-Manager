namespace PokemonManager.Core;

/// <summary>
/// Saves the player has moved from the ROM hacks list to the main list (their ROM is a retail dump the No-Intro
/// list doesn't have, like an old scene dump): one path per line in PokemonManager/official-saves.txt, relative
/// to the SD card.
/// </summary>
public sealed class OfficialSaves(string file, string? sdRoot = null)
{
    private HashSet<string>? _paths;

    private HashSet<string> Paths => _paths ??= Load();

    public bool Contains(string savePath) => Paths.Contains(Key(savePath));

    /// <summary>Throws IOException or UnauthorizedAccessException when the file can't be written.</summary>
    public void Add(string savePath)
    {
        if (!Paths.Add(Key(savePath)))
            return;
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(file))!);
        File.WriteAllLines(file, Paths);
    }

    private string Key(string savePath)
    {
        if (sdRoot is null)
            return savePath;
        var relative = Path.GetRelativePath(Path.GetFullPath(sdRoot), Path.GetFullPath(savePath));
        return relative.StartsWith("..", StringComparison.Ordinal) || Path.IsPathRooted(relative) ? savePath : relative;
    }

    private HashSet<string> Load()
    {
        try
        {
            return File.Exists(file) ? File.ReadAllLines(file).Where(l => l.Length > 0).ToHashSet() : [];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }
}
