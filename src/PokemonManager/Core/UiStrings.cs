using System.Text.Json.Nodes;
using PKHeX.Core;

namespace PokemonManager.Core;

/// <summary>
/// The PC screens' own words in the save's language: the Pokémon menu (Transfer / Summary or Stats / Evolve /
/// Cancel), "… is selected.", "NONE" and the Egg's memo. The names of Pokémon, moves, items, abilities, natures,
/// types and places come from PKHeX's string tables in the same language (<see cref="Scope"/>).
/// </summary>
public static class UiStrings
{
    private sealed record Words(string Transfer, string Summary, string Stats, string Evolve, string Cancel, string Selected,
        string None, string EggMemo);

    private static readonly Dictionary<string, Words> Table = new()
    {
        ["en"] = new("TRANSFER", "SUMMARY", "STATS", "EVOLVE", "CANCEL", "{0} is selected.", "NONE",
            "The EGG Watch\nIt looks like this EGG will\ntake a long time to hatch."),
        ["fr"] = new("TRANSFÉRER", "RÉSUMÉ", "STATS", "ÉVOLUER", "ANNULER", "{0} est sélectionné.", "AUCUN",
            "Observation de l'ŒUF\nOn dirait que cet ŒUF va\nmettre du temps à éclore."),
        ["de"] = new("TRANSFER", "BERICHT", "STATUS", "ENTWICKELN", "ZURÜCK", "{0} ist ausgewählt.", "KEINS",
            "EI-Beobachtung\nEs sieht so aus, als würde es\nnoch lange dauern."),
        ["it"] = new("TRASFERISCI", "SOMMARIO", "STATISTICHE", "EVOLVI", "ESCI", "Hai scelto {0}.", "NESSUNO",
            "Osservazione UOVO\nSembra che quest'UOVO\nci metterà molto a schiudersi."),
        ["es"] = new("TRANSFERIR", "DATOS", "DATOS", "EVOLUCIONAR", "SALIR", "Has elegido a {0}.", "NINGUNO",
            "Estado del HUEVO\nParece que a este HUEVO aún\nle falta mucho para eclosionar."),
        ["ja"] = new("おくる", "つよさをみる", "つよさをみる", "しんか", "やめる", "{0}が えらばれています", "なし",
            "タマゴの ようす\nうまれるまで まだまだ\nじかんが かかりそう"),
        ["ko"] = new("보내기", "강함을 보다", "강함을 보다", "진화", "그만두다", "{0}(이)가 선택되었다", "없음",
            "알의 상태\n태어나려면 아직\n시간이 많이 걸릴 것 같다"),
    };

    /// <summary>
    /// The evolution's words: the viewer's "Evolve {0}?" with YES / NO, and the message after it ({0} the
    /// nickname, {1} the new species; "\n" a new line, "\f" a new page). English follows each game's text
    /// (pokered _EvolvedText/_IntoText, pokecrystal _CongratulationsYourPokemonText/_EvolvedIntoText,
    /// pokeemerald and pokeplatinum gText_CongratsPkmnEvolved); the other languages say the same, broken the
    /// same way.
    /// </summary>
    private sealed record EvolveWords(string Question, string Yes, string No, string Evolved, string? Gen1 = null, string? Gen2 = null);

    private static readonly Dictionary<string, EvolveWords> Evolution = new()
    {
        ["en"] = new("Evolve {0}?", "YES", "NO", "Congratulations! Your {0}\nevolved into {1}!",
            "{0} evolved\ninto {1}!", "Congratulations!\nYour {0}\fevolved into\n{1}!"),
        ["fr"] = new("Faire évoluer {0} ?", "OUI", "NON", "Félicitations ! Votre {0}\na évolué en {1} !",
            "{0} a évolué\nen {1} !", "Félicitations !\nVotre {0}\fa évolué en\n{1} !"),
        ["de"] = new("{0} entwickeln?", "JA", "NEIN", "Glückwunsch! Dein {0}\nhat sich zu {1} entwickelt!",
            "{0} hat sich zu\n{1} entwickelt!", "Glückwunsch!\nDein {0}\fhat sich zu\n{1} entwickelt!"),
        ["it"] = new("Far evolvere {0}?", "SÌ", "NO", "Congratulazioni! Il tuo {0}\nsi è evoluto in {1}!",
            "{0} si è\nevoluto in {1}!", "Congratulazioni!\nIl tuo {0}\fsi è evoluto\nin {1}!"),
        ["es"] = new("¿Hacer evolucionar a {0}?", "SÍ", "NO", "¡Enhorabuena! ¡Tu {0}\nha evolucionado a {1}!",
            "¡{0} ha evolucionado\na {1}!", "¡Enhorabuena!\n¡Tu {0}\fha evolucionado\na {1}!"),
        ["ja"] = new("{0}を しんかさせますか？", "はい", "いいえ", "おめでとう！ {0}は\n{1}に しんかした！"),
        ["ko"] = new("{0}|진화시키겠습니까?", "예", "아니오", "축하합니다! {0}|\n{1}|진화했다!"),
    };

    private static EvolveWords EvolutionFor(string code) => Evolution.TryGetValue(code, out var w) ? w : Evolution["en"];

    /// <summary>The message after an evolution, in the language and the generation's own form.</summary>
    public static string Evolved(string code, int generation, string nickname, string species)
    {
        var w = EvolutionFor(code);
        var format = generation switch { 1 => w.Gen1, 2 => w.Gen2, _ => null } ?? w.Evolved;
        return Korean(string.Format(format, nickname, species));
    }

    /// <summary>"Evolve {0}?", the viewer's question before an evolution.</summary>
    public static string EvolveQuestion(string code, string nickname) => Korean(string.Format(EvolutionFor(code).Question, nickname));

    // Korean particles: "|" before a verb ending stands for 을/를 or 으로/로 after the name, picked by whether its
    // last syllable has a final consonant.
    private static string Korean(string text)
    {
        if (!text.Contains('|'))
            return text;
        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] != '|')
            {
                sb.Append(text[i]);
                continue;
            }
            char last = sb.Length > 0 ? sb[^1] : ' ';
            bool batchim = last is >= '가' and <= '힣' && (last - '가') % 28 != 0;
            var rest = text[(i + 1)..];
            sb.Append(rest.StartsWith('\n') ? (batchim ? "은" : "는") : rest.StartsWith("진화했") ? (batchim ? "으로 " : "로 ") : (batchim ? "을 " : "를 "));
        }
        return sb.ToString();
    }

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

    /// <summary>"EGG" in the language of the names being built (PKHeX's, by generation: タマゴ, OEUF, EI...).</summary>
    public static string EggName(int generation)
    {
        int language = Current switch
        {
            "ja" => (int)LanguageID.Japanese, "fr" => (int)LanguageID.French, "it" => (int)LanguageID.Italian,
            "de" => (int)LanguageID.German, "es" => (int)LanguageID.Spanish, "ko" => (int)LanguageID.Korean,
            _ => (int)LanguageID.English,
        };
        // Gen 3 stores every egg's name as "タマゴ" whatever the language (PKHeX's Gen 3 egg name), but shows the
        // game's own word: Gen 4's names are those words.
        return SpeciesName.GetEggName(language, (byte)Math.Max(generation, 4));
    }

    public static string EggMemo(string code) => For(code).EggMemo;

    /// <summary>The language PKHeX's names are in while a scene is built.</summary>
    public static string Current { get; private set; } = "en";

    /// <summary>
    /// Switches PKHeX's string tables (species, moves, items, abilities, natures, types, places) to a language
    /// until disposed.
    /// </summary>
    public static IDisposable Scope(string code) => new LanguageScope(code);

    private sealed class LanguageScope : IDisposable
    {
        private readonly GameStrings _previous;
        private readonly string _previousCode;

        public LanguageScope(string code)
        {
            // CurrentLanguage alone doesn't swap the tables: the strings are set themselves.
            _previous = GameInfo.Strings;
            _previousCode = Current;
            GameInfo.Strings = GameInfo.GetStrings(code);
            Current = code;
        }

        public void Dispose()
        {
            GameInfo.Strings = _previous;
            Current = _previousCode;
        }
    }
}
