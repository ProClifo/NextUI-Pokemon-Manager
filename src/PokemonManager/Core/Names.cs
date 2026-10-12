using PKHeX.Core;

namespace PokemonManager.Core;

/// <summary>
/// Human-readable names for display on the handheld.
/// </summary>
public static class Names
{
    private static GameStrings Strings => GameInfo.Strings;

    /// <summary>Species names in the game's language while a PC scene is built (UiStrings.Scope), else PKHeX's.</summary>
    internal static string[]? SpeciesNames { get; set; }

    public static string Species(ushort species)
    {
        var list = SpeciesNames ?? Strings.specieslist;
        return species < list.Length ? list[species] : $"#{species}";
    }

    public static string Species(PKM pk) => pk.IsEgg ? "Egg" : Species(pk.Species);

    /// <summary>"Inject Mew (Lv. 10) into Emerald?" or "Inject Manaphy Egg into Platinum?"</summary>
    public static string InjectQuestion(PKM pk, SaveFile sav)
        => pk.IsEgg ? $"Inject {Species(pk.Species)} Egg into {Game(sav)}?" : $"Inject {Species(pk.Species)} (Lv. {pk.CurrentLevel}) into {Game(sav)}?";

    /// <summary>"MEW has arrived in your PC!" (the in-game capitals) or "The EGG has arrived in your PC!"</summary>
    public static string Arrived(PKM pk)
        => pk.IsEgg ? "The EGG has arrived in your PC!" : $"{Species(pk.Species).ToUpperInvariant()} has arrived in your PC!";

    public static string Move(ushort move)
        => move < Strings.movelist.Length ? Strings.movelist[move] : $"Move {move}";

    public static string Item(int item, EntityContext context)
    {
        if (item <= 0)
            return "(none)";
        var list = Strings.GetItemStrings(context);
        return item < list.Length && list[item].Length != 0 ? list[item] : $"Item {item}";
    }

    public static string Game(SaveFile sav) => sav.Version switch
    {
        GameVersion.RD => "Red",
        GameVersion.GN => "Green",
        GameVersion.BU => "Blue",
        GameVersion.YW => "Yellow",
        GameVersion.RBY => "Red/Blue/Yellow",
        GameVersion.GD => "Gold",
        GameVersion.SI => "Silver",
        GameVersion.C => "Crystal",
        GameVersion.GS => "Gold/Silver",
        GameVersion.GSC => "Gold/Silver/Crystal",
        GameVersion.R => "Ruby",
        GameVersion.S => "Sapphire",
        GameVersion.RS => "Ruby/Sapphire",
        GameVersion.E => "Emerald",
        GameVersion.FR => "FireRed",
        GameVersion.LG => "LeafGreen",
        GameVersion.FRLG => "FireRed/LeafGreen",
        GameVersion.CXD => "Colosseum/XD",
        GameVersion.COLO => "Colosseum",
        GameVersion.XD => "XD",
        GameVersion.D => "Diamond",
        GameVersion.P => "Pearl",
        GameVersion.DP => "Diamond/Pearl",
        GameVersion.Pt => "Platinum",
        GameVersion.HG => "HeartGold",
        GameVersion.SS => "SoulSilver",
        GameVersion.HGSS => "HeartGold/SoulSilver",
        GameVersion.B => "Black",
        GameVersion.W => "White",
        GameVersion.BW => "Black/White",
        GameVersion.B2 => "Black 2",
        GameVersion.W2 => "White 2",
        GameVersion.B2W2 => "Black 2/White 2",
        _ => sav.Version.ToString(),
    };

    // The games' names in their own languages, as their titles say them (short: "Emerald", "Smaragd").
    // Columns: English, French, Italian, German, Spanish, Japanese, Korean (null where there was no release).
    private static readonly Dictionary<GameVersion, string?[]> NativeNames = new()
    {
        [GameVersion.RD] = ["Red", "Rouge", "Rosso", "Rot", "Rojo", "赤", null],
        [GameVersion.GN] = ["Green", "Vert", "Verde", "Grün", "Verde", "緑", null],
        [GameVersion.BU] = ["Blue", "Bleu", "Blu", "Blau", "Azul", "青", null],
        [GameVersion.YW] = ["Yellow", "Jaune", "Giallo", "Gelb", "Amarillo", "ピカチュウ", null],
        [GameVersion.GD] = ["Gold", "Or", "Oro", "Gold", "Oro", "金", "금"],
        [GameVersion.SI] = ["Silver", "Argent", "Argento", "Silber", "Plata", "銀", "은"],
        [GameVersion.C] = ["Crystal", "Cristal", "Cristallo", "Kristall", "Cristal", "クリスタル", null],
        [GameVersion.R] = ["Ruby", "Rubis", "Rubino", "Rubin", "Rubí", "ルビー", null],
        [GameVersion.S] = ["Sapphire", "Saphir", "Zaffiro", "Saphir", "Zafiro", "サファイア", null],
        [GameVersion.E] = ["Emerald", "Émeraude", "Smeraldo", "Smaragd", "Esmeralda", "エメラルド", null],
        [GameVersion.FR] = ["FireRed", "Rouge Feu", "Rosso Fuoco", "Feuerrot", "Rojo Fuego", "ファイアレッド", null],
        [GameVersion.LG] = ["LeafGreen", "Vert Feuille", "Verde Foglia", "Blattgrün", "Verde Hoja", "リーフグリーン", null],
        [GameVersion.D] = ["Diamond", "Diamant", "Diamante", "Diamant", "Diamante", "ダイヤモンド", "디아루가"],
        [GameVersion.P] = ["Pearl", "Perle", "Perla", "Perl", "Perla", "パール", "펄기아"],
        [GameVersion.Pt] = ["Platinum", "Platine", "Platino", "Platin", "Platino", "プラチナ", "기라티나"],
        [GameVersion.HG] = ["HeartGold", "Or HeartGold", "Oro HeartGold", "Goldene Edition HeartGold", "Oro HeartGold", "ハートゴールド", "하트골드"],
        [GameVersion.SS] = ["SoulSilver", "Argent SoulSilver", "Argento SoulSilver", "Silberne Edition SoulSilver", "Plata SoulSilver", "ソウルシルバー", "소울실버"],
        [GameVersion.B] = ["Black", "Noire", "Nera", "Schwarz", "Negra", "ブラック", "블랙"],
        [GameVersion.W] = ["White", "Blanche", "Bianca", "Weiß", "Blanca", "ホワイト", "화이트"],
        [GameVersion.B2] = ["Black 2", "Noire 2", "Nera 2", "Schwarz 2", "Negra 2", "ブラック2", "블랙2"],
        [GameVersion.W2] = ["White 2", "Blanche 2", "Bianca 2", "Weiß 2", "Blanca 2", "ホワイト2", "화이트2"],
    };

    // The pairs PKHeX reports when a save doesn't say which of the two it is.
    private static readonly Dictionary<GameVersion, GameVersion[]> Pairs = new()
    {
        [GameVersion.RB] = [GameVersion.RD, GameVersion.BU],
        [GameVersion.RBY] = [GameVersion.RD, GameVersion.BU, GameVersion.YW],
        [GameVersion.GS] = [GameVersion.GD, GameVersion.SI],
        [GameVersion.GSC] = [GameVersion.GD, GameVersion.SI, GameVersion.C],
        [GameVersion.RS] = [GameVersion.R, GameVersion.S],
        [GameVersion.FRLG] = [GameVersion.FR, GameVersion.LG],
        [GameVersion.DP] = [GameVersion.D, GameVersion.P],
        [GameVersion.HGSS] = [GameVersion.HG, GameVersion.SS],
        [GameVersion.BW] = [GameVersion.B, GameVersion.W],
        [GameVersion.B2W2] = [GameVersion.B2, GameVersion.W2],
    };

    /// <summary>
    /// The game's name in its own language (a gallery language code: ENG, GER, JPN...): "Smaragd" for a German
    /// Emerald. English for languages the game wasn't released in, or that aren't known.
    /// </summary>
    public static string NativeGame(GameVersion version, string? language)
    {
        int column = language switch
        {
            "FRE" => 1, "ITA" => 2, "GER" => 3, "SPA" => 4, "JPN" => 5, "KOR" => 6, _ => 0,
        };
        string One(GameVersion v) => NativeNames.TryGetValue(v, out var names) ? names[column] ?? names[0]! : v.ToString();
        return Pairs.TryGetValue(version, out var pair) ? string.Join("/", pair.Select(One)) : One(version);
    }

    /// <summary>One-line summary used in box listings, e.g. "Pikachu Lv.25 M *".</summary>
    public static string Summary(PKM pk)
    {
        if (pk.Species == 0)
            return "(empty)";
        if (pk.IsEgg)
            return $"Egg ({Species(pk.Species)})";
        var name = Species(pk.Species);
        var nick = pk.Nickname;
        var label = pk.IsNicknamed && nick.Length != 0 && nick != name ? $"{nick} ({name})" : name;
        var gender = pk.Gender switch { 0 => " M", 1 => " F", _ => "" };
        var shiny = pk.IsShiny ? " *" : "";
        return $"{label} Lv.{pk.CurrentLevel}{gender}{shiny}";
    }

    /// <summary>Multi-line detail view (Showdown-style set plus origin and legality).</summary>
    /// <summary>
    /// What the game's own summary shows about a Pokémon, and nothing hidden (no EVs, IVs, PID or secret ID).
    /// </summary>
    public static string Details(PKM pk)
    {
        if (pk.Species == 0)
            return "Empty slot";
        var lines = new List<string> { Summary(pk) };
        if (!pk.IsEgg)
        {
            if (pk.Format >= 3)
            {
                lines.Add($"Nature: {GameInfo.Strings.natures[(int)pk.Nature]}");
                lines.Add($"Ability: {GameInfo.Strings.abilitylist[pk.Ability]}");
            }
            var moves = Enumerable.Range(0, 4).Select(pk.GetMove).Where(m => m != 0).Select(m => GameInfo.Strings.movelist[m]);
            lines.Add($"Moves: {string.Join(", ", moves)}");
            lines.Add($"Held item: {(pk.HeldItem == 0 ? "None" : Item(pk.HeldItem, pk.Context))}");
        }
        lines.Add($"OT: {pk.OriginalTrainerName} ({pk.DisplayTID:D5})");
        lines.Add($"Legality: {Legality(pk)}");
        return string.Join('\n', lines);
    }

    public static string Legality(PKM pk)
    {
        try
        {
            var la = new LegalityAnalysis(pk);
            return la.Valid ? "Legal" : "Flagged by PKHeX legality check";
        }
        catch
        {
            return "Unknown";
        }
    }
}
