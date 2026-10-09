using PokemonManager.Core;

namespace PokemonManager.Ui;

/// <summary>
/// The handful of screens the app needs. Implemented with NextUI's minui-* tools on device and with a
/// plain text console for desktop testing.
/// </summary>
public interface IUi
{
    /// <summary>The font screens are drawn with (a game's own font in its menus); null for the NextUI font.</summary>
    GameFont? Font { get; set; }

    /// <summary>
    /// Shows a list and returns the chosen index, or null when the user backs out. The optional
    /// background image is drawn behind the list where the UI supports it, and <paramref name="tags"/>
    /// (e.g. "Legal") are shown at the right of the highlighted item. With <paramref name="images"/> every
    /// item shows its tag, and a tag written "LEFT\tRIGHT" has the item's image between the two parts.
    /// </summary>
    int? Choose(string title, IReadOnlyList<string> items, int selected = 0, string? background = null, IReadOnlyList<string?>? tags = null, IReadOnlyList<string?>? images = null);

    /// <summary>Shows text until the user presses A or B. Long text is split into pages.</summary>
    void Message(string text);

    /// <summary>Asks a yes/no question.</summary>
    bool Confirm(string text, string yes = "YES", string no = "NO");

    /// <summary>Shows a "please wait" message that stays up until the next screen is shown.</summary>
    void Busy(string text);
}

public static class UiText
{
    public const int LinesPerPage = 9;

    /// <summary>Splits long text into pages on paragraph/line boundaries.</summary>
    public static List<string> Paginate(string text, int linesPerPage = LinesPerPage)
    {
        var pages = new List<string>();
        var current = new List<string>();
        foreach (var line in text.Replace("\r", "").Split('\n'))
        {
            if (current.Count >= linesPerPage)
            {
                pages.Add(string.Join('\n', current).Trim('\n'));
                current.Clear();
            }
            current.Add(line);
        }
        if (current.Count != 0)
            pages.Add(string.Join('\n', current).Trim('\n'));
        pages.RemoveAll(p => p.Length == 0);
        if (pages.Count == 0)
            pages.Add(" ");
        return pages;
    }
}
