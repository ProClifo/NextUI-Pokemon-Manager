namespace PokemonManager.Core;

/// <summary>
/// The Regi dolls Pokémon Manager has given each game (PokemonManager/received-dolls.txt, one
/// "game - trainer - ID&lt;TAB&gt;doll" line each). The real Mystery Event gave each doll once, and the game keeps no
/// record of it: without this, sending a doll to another game or throwing it away would let it be given again.
/// </summary>
public sealed class DollRecord(string file)
{
    private HashSet<string>? _given;

    private HashSet<string> Given => _given ??= File.Exists(file) ? [.. File.ReadAllLines(file)] : [];

    public bool Has(string game, byte doll) => Given.Contains($"{game}\t{doll}");

    /// <summary>Throws IOException or UnauthorizedAccessException when the file can't be written.</summary>
    public void Add(string game, byte doll)
    {
        if (!Given.Add($"{game}\t{doll}"))
            return;
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(file))!);
        File.AppendAllLines(file, [$"{game}\t{doll}"]);
    }
}
