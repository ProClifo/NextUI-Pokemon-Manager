using System.Text.Json.Nodes;
using PKHeX.Core;

namespace PokemonManager.Core;

/// <summary>
/// The PC screens' own words (the Pokémon menu, "… is selected.", "NONE", the Egg's memo, the evolution's), in
/// English like the rest of the screens; only the Pokémon's species names are in the game's language
/// (<see cref="Scope"/>).
/// </summary>
public static class UiStrings
{
    private sealed record Words(string Transfer, string Summary, string Stats, string Evolve, string Cancel, string Selected,
        string None, string EggMemo);

    private static readonly Dictionary<string, Words> Table = new()
    {
        ["en"] = new("TRANSFER", "SUMMARY", "STATS", "EVOLVE", "CANCEL", "{0} is selected.", "NONE",
            "The EGG Watch\nIt looks like this EGG will\ntake a long time to hatch."),
    };

    /// <summary>
    /// The evolution's words: the viewer's "Evolve {0}?" with YES / NO, and the message after it ({0} the
    /// nickname, {1} the new species; "\n" a new line, "\f" a new page). English follows each game's text
    /// (pokered _EvolvedText/_IntoText, pokecrystal _CongratulationsYourPokemonText/_EvolvedIntoText,
    /// pokeemerald and pokeplatinum gText_CongratsPkmnEvolved).
    /// </summary>
    private sealed record EvolveWords(string Question, string Yes, string No, string Evolved, string? Gen1 = null, string? Gen2 = null);

    private static readonly Dictionary<string, EvolveWords> Evolution = new()
    {
        ["en"] = new("Evolve {0}?", "YES", "NO", "Congratulations! Your {0}\nevolved into {1}!",
            "{0} evolved\ninto {1}!", "Congratulations!\nYour {0}\fevolved into\n{1}!"),
    };

    private static EvolveWords EvolutionFor(string code) => Evolution.TryGetValue(code, out var w) ? w : Evolution["en"];

    /// <summary>The message after an evolution, in the language and the generation's own form.</summary>
    public static string Evolved(string code, int generation, string nickname, string species)
    {
        var w = EvolutionFor(code);
        var format = generation switch { 1 => w.Gen1, 2 => w.Gen2, _ => null } ?? w.Evolved;
        return string.Format(format, nickname, species);
    }

    /// <summary>"Evolve {0}?", the viewer's question before an evolution.</summary>
    public static string EvolveQuestion(string code, string nickname) => string.Format(EvolutionFor(code).Question, nickname);

    /// <summary>PKHeX's language code for a gallery language (ENG, GER, JPN...), else the save's own.</summary>
    public static string Code(string? language, SaveFile sav) => language switch
    {
        "ENG" or "INT" => "en",
        "FRE" => "fr",
        "GER" => "de",
        "ITA" => "it",
        "SPA" => "es",
        "JPN" => "ja",
        "KOR" => "ko",
        _ => (LanguageID)sav.Language switch
        {
            LanguageID.French => "fr",
            LanguageID.German => "de",
            LanguageID.Italian => "it",
            LanguageID.Spanish or LanguageID.SpanishL => "es",
            LanguageID.Japanese => "ja",
            LanguageID.Korean => "ko",
            _ => "en",
        },
    };

    /// <summary>The LanguageID of a language code (en, de, ja...).</summary>
    public static int LanguageId(string code) => (int)(code switch
    {
        "fr" => LanguageID.French, "de" => LanguageID.German, "it" => LanguageID.Italian, "es" => LanguageID.Spanish,
        "ja" => LanguageID.Japanese, "ko" => LanguageID.Korean, _ => LanguageID.English,
    });

    private static Words For(string code) => Table.TryGetValue(code, out var w) ? w : Table["en"];

    /// <summary>The viewer's "strings" object for a language code.</summary>
    public static JsonObject Json(string code)
    {
        var w = For(code);
        return new JsonObject
        {
            ["menu"] = new JsonArray(w.Transfer, w.Summary, w.Evolve, w.Cancel),
            ["gb_menu"] = new JsonArray(w.Transfer, w.Stats, w.Evolve, w.Cancel),
            ["cancel"] = w.Cancel,
            ["selected"] = w.Selected.Replace("{0}", "%s"),
            ["evolve_question"] = EvolutionFor(code).Question.Replace("{0}", "%s"),
            ["yes_no"] = new JsonArray(EvolutionFor(code).Yes, EvolutionFor(code).No),
        };
    }

    public static string None(string code) => For(code).None;

    /// <summary>"EGG" (Gen 3 stores every egg's name as "タマゴ" whatever the language; the screens are English).</summary>
    public static string EggName(int generation) => SpeciesName.GetEggName((int)LanguageID.English, (byte)Math.Max(generation, 4));

    public static string EggMemo(string code) => For(code).EggMemo;

    /// <summary>The language of the Pokémon names while a scene is built (the screens' words stay English).</summary>
    public static string Current { get; private set; } = "en";

    /// <summary>
    /// Until disposed: PKHeX's string tables (moves, items, abilities, natures, types, places) in English, and
    /// the species names in <paramref name="code"/>'s language.
    /// </summary>
    public static IDisposable Scope(string code) => new LanguageScope(code);

    private sealed class LanguageScope : IDisposable
    {
        private readonly GameStrings _previous;
        private readonly string _previousCode;
        private readonly string[]? _previousSpecies;

        public LanguageScope(string code)
        {
            _previous = GameInfo.Strings;
            _previousCode = Current;
            _previousSpecies = Names.SpeciesNames;
            GameInfo.Strings = GameInfo.GetStrings("en");
            Names.SpeciesNames = code == "en" ? null : GameInfo.GetStrings(code).specieslist;
            Current = code;
        }

        public void Dispose()
        {
            GameInfo.Strings = _previous;
            Names.SpeciesNames = _previousSpecies;
            Current = _previousCode;
        }
    }
}
