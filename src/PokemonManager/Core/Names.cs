using PKHeX.Core;

namespace PokemonManager.Core;

/// <summary>
/// Human-readable names for display on the handheld.
/// </summary>
public static class Names
{
    private static GameStrings Strings => GameInfo.Strings;

    public static string Species(ushort species)
        => species < Strings.specieslist.Length ? Strings.specieslist[species] : $"#{species}";

    public static string Species(PKM pk) => pk.IsEgg ? "Egg" : Species(pk.Species);

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
    public static string Details(PKM pk)
    {
        if (pk.Species == 0)
            return "Empty slot";
        var lines = new List<string>();
        try
        {
            lines.Add(new ShowdownSet(pk).Text.Trim());
        }
        catch
        {
            lines.Add(Summary(pk));
        }
        lines.Add("");
        lines.Add($"OT: {pk.OriginalTrainerName} ({pk.DisplayTID:D5})");
        lines.Add($"Held item: {Item(pk.HeldItem, pk.Context)}");
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
