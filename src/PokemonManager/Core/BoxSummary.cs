using System.Text.Json;
using System.Text.Json.Nodes;
using PKHeX.Core;

namespace PokemonManager.Core;

/// <summary>
/// The text of a Pokémon's summary pages for the PC box viewer, worded like Emerald's summary screen
/// (pokemon_summary_screen.c). Highlighted parts of the trainer memo are wrapped in { } (drawn in red).
/// </summary>
public static class BoxSummary
{
    /// <summary>Gen 3 ability descriptions by ability number (res/box/abilities.json, from pokeemerald).</summary>
    public static IReadOnlyDictionary<int, string> AbilityDescriptions { get; set; } = new Dictionary<int, string>();

    public static void LoadAbilityDescriptions(string path)
    {
        try
        {
            if (!File.Exists(path))
                return;
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            var descriptions = new Dictionary<int, string>();
            foreach (var p in doc.RootElement.EnumerateObject())
            {
                if (int.TryParse(p.Name, out int ability) && p.Value.GetString() is { } text)
                    descriptions[ability] = text;
            }
            AbilityDescriptions = descriptions;
        }
        catch (Exception ex) when (ex is IOException or JsonException)
        {
            Console.Error.WriteLine($"Couldn't read {path}: {ex.Message}");
        }
    }

    /// <summary>The species Gen 3's summary screens show unmirrored (base stats' noFlip).</summary>
    private static readonly HashSet<ushort> NotFlipped = [60, 61, 62, 99, 125, 159, 174, 186, 201, 215, 216, 239, 315, 327, 335, 336, 359, 386];

    /// <summary>The Hoenn Pokédex in order, as national numbers (pokeemerald's HOENN_DEX_* constants).</summary>
    private static readonly ushort[] HoennDex = [252, 253, 254, 255, 256, 257, 258, 259, 260, 261, 262, 263, 264, 265, 266, 267, 268, 269, 270, 271, 272, 273, 274, 275, 276, 277, 278, 279, 280, 281, 282, 283, 284, 285, 286, 287, 288, 289, 63, 64, 65, 290, 291, 292, 293, 294, 295, 296, 297, 118, 119, 129, 130, 298, 183, 184, 74, 75, 76, 299, 300, 301, 41, 42, 169, 72, 73, 302, 303, 304, 305, 306, 66, 67, 68, 307, 308, 309, 310, 311, 312, 81, 82, 100, 101, 313, 314, 43, 44, 45, 182, 84, 85, 315, 316, 317, 318, 319, 320, 321, 322, 323, 218, 219, 324, 88, 89, 109, 110, 325, 326, 27, 28, 327, 227, 328, 329, 330, 331, 332, 333, 334, 335, 336, 337, 338, 339, 340, 341, 342, 343, 344, 345, 346, 347, 348, 174, 39, 40, 349, 350, 351, 120, 121, 352, 353, 354, 355, 356, 357, 358, 359, 37, 38, 172, 25, 26, 54, 55, 360, 202, 177, 178, 203, 231, 232, 127, 214, 111, 112, 361, 362, 363, 364, 365, 366, 367, 368, 369, 222, 170, 171, 370, 116, 117, 230, 371, 372, 373, 374, 375, 376, 377, 378, 379, 380, 381, 382, 383, 384, 385, 386];

    /// <summary>
    /// The number the summary shows (SpeciesToPokedexNum): national once the National Dex is on, otherwise the
    /// regional number, or none for a species outside the regional Pokédex. (Emerald's summary shows none.)
    /// </summary>
    private static int? DexNumber(ushort species, SaveFile sav)
    {
        if (sav is not SAV3 || TradeRules.NationalDex3((SAV3)sav))
            return species; // Game Boy stats screens show the national number
        if (sav is SAV3FRLG) // FireRed/LeafGreen: the Kanto Pokédex is the first 151
            return species <= 151 ? species : null;
        int hoenn = Array.IndexOf(HoennDex, species);
        return hoenn >= 0 ? hoenn + 1 : null;
    }

    public static JsonObject For(PKM pk, SaveFile sav, bool inParty)
    {
        var strings = GameInfo.Strings;
        var summary = new JsonObject
        {
            ["nickname"] = Name(pk),
            ["species"] = pk.IsEgg ? "" : Up(Names.Species(pk.Species)),
            ["level"] = pk.IsEgg ? "" : pk.CurrentLevel.ToString(),
            ["ball"] = BallName(pk.Ball),
            ["ot"] = pk.OriginalTrainerName,
            ["ot_female"] = pk.OriginalTrainerGender == 1,
            ["id"] = $"{pk.DisplayTID:D5}",
            ["egg"] = pk.IsEgg,
            ["flip"] = !pk.IsEgg && !NotFlipped.Contains(pk.Species),
        };
        if (!pk.IsEgg && DexNumber(pk.Species, sav) is { } dex)
            summary["dex_no"] = $"{dex:D3}";
        else if (!pk.IsEgg && sav is SAV3FRLG)
            summary["dex_no"] = "???";
        if (pk.Gender is 0 or 1 && !pk.IsEgg)
            summary["gender"] = pk.Gender == 0 ? "male" : "female";
        if (pk.IsEgg)
        {
            summary["memo"] = "The EGG Watch\nIt looks like this EGG will\ntake a long time to hatch.";
            return summary;
        }

        var personal = pk.PersonalInfo;
        var types = new JsonArray((JsonNode)TypeName(personal.Type1));
        if (personal.Type2 != personal.Type1)
            types.Add((JsonNode)TypeName(personal.Type2));
        summary["types"] = types;
        // The Game Boy stats screens spell the types out ("PSYCHIC"); Gen 1 calls Psychic's partner "PSYCHIC" too.
        var typeNames = new JsonArray((JsonNode)Up(TypeText(personal.Type1)));
        if (personal.Type2 != personal.Type1)
            typeNames.Add((JsonNode)Up(TypeText(personal.Type2)));
        summary["type_names"] = typeNames;
        summary["species_id"] = pk.Species;
        // Crystal shows the OT's gender only for Pokémon with catch data
        summary["ot_gender_known"] = pk is not PK2 || ((PK2)pk).MetLevel > 0;
        summary["shiny"] = pk.IsShiny;
        summary["status"] = StatusText(pk, inParty);
        if (pk.HeldItem > 0)
            summary["item_icon"] = IsMail(pk) ? "mail" : "item";
        // Game Boy Pokémon have no ability or nature.
        bool gameBoy = pk.Format <= 2;
        summary["ability"] = gameBoy ? "" : Up(strings.abilitylist[pk.Ability]);
        summary["ability_desc"] = gameBoy ? "" : AbilityDescriptions.GetValueOrDefault(pk.Ability, "");
        summary["memo"] = gameBoy ? MemoGameBoy(pk) : Memo(pk, sav);
        summary["item"] = pk.HeldItem > 0 ? Up(Names.Item(pk.HeldItem, pk.Context)) : "NONE";
        int ribbons = RibbonCount(pk);
        summary["ribbon"] = ribbons == 0 ? "NONE" : ribbons.ToString();

        // Box Pokémon keep no stats (and are healed): work them out as the game does when it shows them.
        var stats = pk.Clone();
        stats.ResetPartyStats();
        int hp = inParty && pk.Stat_HPMax > 0 ? pk.Stat_HPCurrent : stats.Stat_HPMax;
        summary["hp"] = $"{hp,3}/{stats.Stat_HPMax,3}";
        summary["hp_cur"] = hp;
        summary["hp_max"] = stats.Stat_HPMax;
        summary["attack"] = stats.Stat_ATK.ToString();
        summary["defense"] = stats.Stat_DEF.ToString();
        summary["sp_atk"] = stats.Stat_SPA.ToString();
        summary["sp_def"] = stats.Stat_SPD.ToString();
        summary["speed"] = stats.Stat_SPE.ToString();

        byte level = pk.CurrentLevel;
        byte growth = personal.EXPGrowth;
        uint exp = pk.EXP;
        summary["exp_points"] = exp.ToString();
        if (level >= 100)
        {
            summary["next_lv"] = "0";
            summary["exp_fill"] = 0.0;
        }
        else
        {
            uint start = Experience.GetEXP(level, growth), next = Experience.GetEXP((byte)(level + 1), growth);
            summary["next_lv"] = (next - exp).ToString();
            summary["exp_fill"] = next > start ? Math.Clamp((exp - start) / (double)(next - start), 0, 1) : 0;
        }

        var moves = new JsonArray();
        for (int i = 0; i < 4; i++)
        {
            ushort move = pk.GetMove(i);
            if (move == 0)
            {
                moves.Add((JsonNode)new JsonObject { ["name"] = "-" });
                continue;
            }
            int ppUps = i switch { 0 => pk.Move1_PPUps, 1 => pk.Move2_PPUps, 2 => pk.Move3_PPUps, _ => pk.Move4_PPUps };
            int pp = i switch { 0 => pk.Move1_PP, 1 => pk.Move2_PP, 2 => pk.Move3_PP, _ => pk.Move4_PP };
            moves.Add((JsonNode)new JsonObject
            {
                ["name"] = Up(strings.movelist[move]),
                ["type"] = TypeName(MoveInfo.GetType(move, pk.Context)),
                ["pp"] = pp,
                ["max_pp"] = pk.GetMovePP(move, ppUps),
            });
        }
        summary["moves"] = moves;
        return summary;
    }

    /// <summary>
    /// Emerald's trainer memo (BufferMonTrainerMemo): met/hatched where and at what level when the OT is the
    /// player, a fateful encounter for event Pokémon, else "probably met at" or "obtained in a trade".
    /// </summary>
    public static string Memo(PKM pk, SaveFile sav)
    {
        if (sav is SAV3RS)
            return MemoRS(pk, sav);
        if (sav is SAV3FRLG)
            return MemoFRLG(pk, sav);
        var nature = $"{{{Up(GameInfo.Strings.natures[(int)pk.Nature])}}} nature";
        bool hatched = pk.MetLevel == 0 || pk.WasEgg && pk.EggLocation != 0 && pk.Generation >= 4;
        int level = pk.MetLevel == 0 ? (pk.Generation >= 4 ? 1 : 5) : pk.MetLevel;
        string? place = pk.MetLocation is 0 or 254 or 255 ? null
            : GameInfo.GetLocationName(false, pk.MetLocation, pk.Format, pk.Generation, pk.Version) is { Length: > 0 } name ? Up(name) : null;
        bool own = pk.OriginalTrainerName == sav.OT && pk.TID16 == sav.TID16 && pk.OriginalTrainerGender == sav.Gender;

        if (own)
        {
            var verb = hatched ? "hatched" : "met";
            return place is null
                ? $"{nature},\n{verb} somewhere at Lv{{{level}}}."
                : $"{nature},\n{verb} at Lv{{{level}}},\n{{{place}}}.";
        }
        if (pk.FatefulEncounter)
            return $"{nature},\nobtained in a fateful\nencounter at Lv{{{level}}}.";
        if (place is not null && pk.MetLocation != 254)
            return $"{nature},\nprobably met at Lv{{{level}}},\n{{{place}}}.";
        return $"{nature},\nobtained in a trade.";
    }

    /// <summary>
    /// Ruby/Sapphire's two-line trainer memo (PokemonSummaryScreen_PrintTrainerMemo): "&lt;NATURE&gt; nature, Lv5,"
    /// then "&lt;PLACE&gt; (met)." or "(EGG).", "fateful encounter." or "obtained in a trade.".
    /// </summary>
    private static string MemoRS(PKM pk, SaveFile sav)
    {
        var nature = $"{{{Up(GameInfo.Strings.natures[(int)pk.Nature])}}} nature, ";
        int level = pk.MetLevel == 0 ? 5 : pk.MetLevel;
        string Place() => GameInfo.GetLocationName(false, pk.MetLocation, pk.Format, pk.Generation, pk.Version) is { Length: > 0 } name ? Up(name) : "";
        bool own = pk.OriginalTrainerName == sav.OT && pk.TID16 == sav.TID16 && pk.OriginalTrainerGender == sav.Gender;
        const int MapsecNone = 88, Fateful = 255;
        if (own)
        {
            if (pk.MetLevel == 0)
                return $"{nature}Lv{{5}},\n{{{Place()}}} (EGG).";
            return pk.MetLocation >= MapsecNone
                ? $"{nature}\nobtained in a trade."
                : $"{nature}Lv{{{level}}},\n{{{Place()}}} (met).";
        }
        if (pk.Version is not (GameVersion.R or GameVersion.S or GameVersion.E))
            return $"{nature}\nobtained in a trade.";
        if (pk.MetLocation == Fateful)
            return $"{nature}Lv{{{level}}},\nfateful encounter.";
        return pk.MetLocation >= MapsecNone
            ? $"{nature}\nobtained in a trade."
            : $"{nature}Lv{{{level}}},\n{{{Place()}}} (met).";
    }

    /// <summary>
    /// FireRed/LeafGreen's trainer memo (PokeSum_PrintTrainerMemo_Mon), all in one colour: places outside Kanto
    /// and the Sevii Islands read "a trade", and someone else's Pokémon was "apparently" met there.
    /// </summary>
    private static string MemoFRLG(PKM pk, SaveFile sav)
    {
        var nature = Up(GameInfo.Strings.natures[(int)pk.Nature]);
        int level = pk.MetLevel == 0 ? 5 : pk.MetLevel;
        const int KantoStart = 88, MapsecNone = 197, Fateful = 255;
        bool kanto = pk.MetLocation is >= KantoStart and < MapsecNone;
        string place = kanto && GameInfo.GetLocationName(false, pk.MetLocation, pk.Format, pk.Generation, pk.Version) is { Length: > 0 } name
            ? Up(name) : "a trade";
        bool hatched = pk.MetLevel == 0;
        string fatefulMet = $"{nature} nature.\nMet in a fateful encounter when\nat Lv {level}.";
        bool own = pk.OriginalTrainerName == sav.OT && pk.TID16 == sav.TID16 && pk.OriginalTrainerGender == sav.Gender;
        if (own)
        {
            if (hatched)
                return pk.FatefulEncounter
                    ? $"{nature} nature. Met in a fateful\nencounter (hatched: {place}\nat Lv {level})."
                    : $"{nature} nature.\nHatched: {place}\nat Lv {level}.";
            return pk.MetLocation == Fateful ? fatefulMet : $"{nature} nature.\nMet in {place} at Lv {level}.";
        }
        bool fromGba = pk.Version is GameVersion.FR or GameVersion.LG or GameVersion.R or GameVersion.S or GameVersion.E;
        if (!kanto || !fromGba)
            return pk.MetLocation == Fateful ? fatefulMet : $"{nature} nature.\nMet in a trade.";
        if (hatched && pk.FatefulEncounter)
            return $"{nature} nature. Apparently met in\na fateful encounter (hatched:\n{place} at Lv {level}).";
        return pk.MetLocation == Fateful ? fatefulMet : $"{nature} nature.\nApparently met in {place}\nat Lv {level}.";
    }

    /// <summary>Gen 3's ribbon count (GetRibbonCount): each contest rank won plus every other ribbon.</summary>
    private static int RibbonCount(PKM pk)
    {
        int count = 0;
        if (pk is IRibbonSetOnly3 only)
            count += only.RibbonCountG3Cool + only.RibbonCountG3Beauty + only.RibbonCountG3Cute + only.RibbonCountG3Smart
                + only.RibbonCountG3Tough + (only.RibbonWorld ? 1 : 0);
        if (pk is IRibbonSetCommon3 common)
            count += (common.RibbonChampionG3 ? 1 : 0) + (common.RibbonArtist ? 1 : 0) + (common.RibbonEffort ? 1 : 0);
        if (pk is IRibbonSetUnique3 unique)
            count += (unique.RibbonWinning ? 1 : 0) + (unique.RibbonVictory ? 1 : 0);
        if (pk is IRibbonSetEvent3 ev)
            count += (ev.RibbonChampionBattle ? 1 : 0) + (ev.RibbonChampionRegional ? 1 : 0) + (ev.RibbonChampionNational ? 1 : 0)
                + (ev.RibbonCountry ? 1 : 0) + (ev.RibbonNational ? 1 : 0) + (ev.RibbonEarth ? 1 : 0);
        return count;
    }

    /// <summary>Game Boy Pokémon: only Crystal records where and at what level they were met.</summary>
    private static string MemoGameBoy(PKM pk)
    {
        if (pk is not PK2 { MetLevel: > 0 } crystal)
            return "";
        var place = crystal.MetLocation > 0
            && GameInfo.GetLocationName(false, crystal.MetLocation, 2, 2, GameVersion.C) is { Length: > 0 } name ? Up(name) : null;
        return place is null ? $"Met at Lv{{{crystal.MetLevel}}}." : $"Met at Lv{{{crystal.MetLevel}}},\n{{{place}}}.";
    }

    private static string Name(PKM pk) => pk.IsEgg ? "EGG" : pk.IsNicknamed ? pk.Nickname : Up(Names.Species(pk.Species));

    private static string Up(string text) => text.ToUpperInvariant();

    private static string TypeText(int type) => type < GameInfo.Strings.types.Length ? GameInfo.Strings.types[type] : "???";

    /// <summary>A party Pokémon's status as the stats screens print it (box Pokémon are healed): FNT, SLP, PSN...</summary>
    private static string StatusText(PKM pk, bool inParty)
    {
        if (!inParty || pk.Stat_HPMax == 0)
            return "OK";
        if (pk.Stat_HPCurrent == 0)
            return "FNT";
        int status = pk.Status_Condition;
        return (status & 7) != 0 ? "SLP" : (status & 0x08) != 0 ? "PSN" : (status & 0x10) != 0 ? "BRN"
            : (status & 0x20) != 0 ? "FRZ" : (status & 0x40) != 0 ? "PAR" : "OK";
    }

    /// <summary>Whether the held item is a piece of mail (Gen 2's PC shows a letter instead of the item icon).</summary>
    private static bool IsMail(PKM pk) => Names.Item(pk.HeldItem, pk.Context).EndsWith("Mail", StringComparison.OrdinalIgnoreCase);

    /// <summary>The ball icon's file name (balls/&lt;name&gt;.png); balls Gen 3 doesn't have show as a Poké Ball.</summary>
    private static string BallName(byte ball) => (Ball)ball switch
    {
        Ball.Master => "master", Ball.Ultra => "ultra", Ball.Great => "great", Ball.Safari => "safari", Ball.Net => "net",
        Ball.Dive => "dive", Ball.Nest => "nest", Ball.Repeat => "repeat", Ball.Timer => "timer", Ball.Luxury => "luxury",
        Ball.Premier => "premier", _ => "poke",
    };

    /// <summary>The type icon's file name: "psychic", "mystery" (???) ...</summary>
    private static string TypeName(int type) => type < GameInfo.Strings.types.Length && GameInfo.Strings.types[type] is { } name
        ? name is "???" ? "mystery" : name.ToLowerInvariant()
        : "mystery";
}
