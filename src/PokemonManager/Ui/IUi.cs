using PokemonManager.Core;

namespace PokemonManager.Ui;

/// <summary>
/// The handful of screens the app needs. Implemented with NextUI's minui-* tools on device and with a
/// plain text console for desktop testing.
/// </summary>
public interface IUi
{
    /// <summary>The NextUI font screens are drawn with (Next on the main menu, OG in a save); null for the NextUI setting.</summary>
    UiFont? Font { get; set; }

    /// <summary>The background drawn behind lists and messages that don't name their own (a game's title art in its menus).</summary>
    string? Background { get; set; }

    /// <summary>L works as A in lists (the game's BUTTON MODE: L=A, while its menus are open).</summary>
    bool LIsA { get; set; }

    /// <summary>
    /// Shows a list and returns the chosen index, or null when the user backs out. The optional
    /// background image is drawn behind the list where the UI supports it, and <paramref name="tags"/>
    /// (e.g. "Legal") are shown at the right of the highlighted item. With <paramref name="images"/> every
    /// item shows its tag, and a tag written "LEFT\tRIGHT" has the item's image between the two parts.
    /// <paramref name="disabled"/> items are greyed out; picking one returns its index, which callers ignore.
    /// <paramref name="icons"/> are images drawn in front of the items' names. <paramref name="titleImageRect"/> picks
    /// a part of <paramref name="titleImage"/> (one frame of an icon sheet). With <paramref name="actionText"/>, Y is
    /// shown with that label; pressing it returns the highlighted index with <see cref="ActionPressed"/> set.
    /// <paramref name="secondaryText"/> does the same for SELECT (<see cref="SecondaryPressed"/>); rows marked in
    /// <paramref name="noActions"/> offer neither. <paramref name="confirmText"/> labels A (SELECT by default).
    /// <paramref name="unselectable"/> rows are text the highlight skips (a question's lines above its answers).
    /// </summary>
    int? Choose(string title, IReadOnlyList<string> items, int selected = 0, string? background = null, IReadOnlyList<string?>? tags = null, IReadOnlyList<string?>? images = null, string? titleImage = null, IReadOnlyList<bool>? disabled = null, IReadOnlyList<string?>? icons = null, (int X, int Y, int W, int H)? titleImageRect = null, string? actionText = null, string? secondaryText = null, IReadOnlyList<bool>? noActions = null, string? confirmText = null, IReadOnlyList<bool>? unselectable = null);

    /// <summary>
    /// A settings list like NextUI's: each row's name on the left and its value on the right, changed with
    /// left/right. Rows without values are actions. Returns every row's value index and the highlighted row
    /// when A is pressed, or null for B.
    /// </summary>
    (int[] Values, int Selected)? Options(string title, IReadOnlyList<string> names, IReadOnlyList<string[]> options, IReadOnlyList<int> values, int selected = 0);

    /// <summary>Whether the last <see cref="Choose"/> ended with Y (its action) rather than A.</summary>
    bool ActionPressed { get; }

    /// <summary>Whether the last <see cref="Choose"/> ended with SELECT (its secondary action).</summary>
    bool SecondaryPressed { get; }

    /// <summary>An on-screen keyboard; returns the text entered, or null when the user backs out.</summary>
    string? Keyboard(string title, string initial);

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
