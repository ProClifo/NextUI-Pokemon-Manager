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
            if (File.Exists(path))
                AbilityDescriptions = JsonSerializer.Deserialize<Dictionary<int, string>>(File.ReadAllText(path)) ?? [];
        }
        catch (Exception ex) when (ex is IOException or JsonException)
        {
            Console.Error.WriteLine($"Couldn't read {path}: {ex.Message}");
        }
    }

    public static JsonObject For(PKM pk, SaveFile sav, bool inParty)
    {
        var strings = GameInfo.Strings;
        var summary = new JsonObject
        {
            ["nickname"] = Name(pk),
            ["species"] = pk.IsEgg ? "" : "/" + Up(Names.Species(pk.Species)),
            ["level"] = pk.IsEgg ? "" : $"Lv{pk.CurrentLevel}",
            ["ot"] = pk.OriginalTrainerName,
            ["ot_female"] = pk.OriginalTrainerGender == 1,
            ["id"] = $"{pk.DisplayTID:D5}",
            ["egg"] = pk.IsEgg,
        };
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
        summary["ability"] = Up(strings.abilitylist[pk.Ability]);
        summary["ability_desc"] = AbilityDescriptions.GetValueOrDefault(pk.Ability, "");
        summary["memo"] = Memo(pk, sav);
        summary["item"] = pk.HeldItem > 0 ? Up(Names.Item(pk.HeldItem, pk.Context)) : "NONE";
        int ribbons = RibbonInfo.GetRibbonInfo(pk).Sum(r => r.HasRibbon ? 1 : r.RibbonCount > 0 ? 1 : 0);
        summary["ribbon"] = ribbons == 0 ? "NONE" : ribbons.ToString();

        // Box Pokémon keep no stats (and are healed): work them out as the game does when it shows them.
        var stats = pk.Clone();
        stats.ResetPartyStats();
        int hp = inParty && pk.Stat_HPMax > 0 ? pk.Stat_HPCurrent : stats.Stat_HPMax;
        summary["hp"] = $"{hp,3}/{stats.Stat_HPMax,3}";
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

    private static string Name(PKM pk) => pk.IsEgg ? "EGG" : pk.IsNicknamed ? pk.Nickname : Up(Names.Species(pk.Species));

    private static string Up(string text) => text.ToUpperInvariant();

    /// <summary>The type icon's file name: "psychic", "mystery" (???) ...</summary>
    private static string TypeName(int type) => type < GameInfo.Strings.types.Length && GameInfo.Strings.types[type] is { } name
        ? name is "???" ? "mystery" : name.ToLowerInvariant()
        : "mystery";
}
