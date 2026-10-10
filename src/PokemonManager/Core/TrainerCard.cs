using System.Text.Json.Nodes;
using PKHeX.Core;

namespace PokemonManager.Core;

/// <summary>
/// What a game's Trainer Card shows, for pkmgr-box --card: the player's name, ID, money, Pokédex count, play time
/// and badges (bit 0 = the first gym's), drawn with the game's own card art (the skin folder).
/// </summary>
public static class TrainerCard
{
    /// <summary>
    /// Whether the game has a card to show: Gen 1-3 (later games borrow Emerald's PC art, not its card) with the
    /// card art in the skin (scripts/gb_ui and scripts/pc_ui mark it in layout.json).
    /// </summary>
    public static bool Available(SaveFile sav, string skin)
    {
        if (sav.Generation > 3)
            return false;
        try
        {
            var layout = Path.Combine(skin, "layout.json");
            return File.Exists(layout) && JsonNode.Parse(File.ReadAllText(layout))?["card"]?.GetValue<bool>() == true;
        }
        catch (Exception ex) when (ex is IOException or System.Text.Json.JsonException or InvalidOperationException)
        {
            return false;
        }
    }

    public static JsonObject For(SaveFile sav, string skin, string? font)
    {
        var card = new JsonObject
        {
            ["skin"] = skin,
            ["name"] = sav.OT,
            ["female"] = sav.Gender == 1,
            ["id"] = (int)sav.DisplayTID,
            ["money"] = (long)sav.Money,
            ["hours"] = sav.PlayedHours,
            ["minutes"] = sav.PlayedMinutes,
            ["seconds"] = sav.PlayedSeconds,
            ["has_dex"] = TradeRules.HasPokedex(sav),
            ["dex"] = DexCount(sav),
            ["badges"] = Badges(sav),
        };
        if (font is not null)
            card["font"] = font;
        return card;
    }

    /// <summary>The badges as the card shows them: Gen 2's are Johto's (the card has no Kanto page).</summary>
    private static int Badges(SaveFile sav) => sav switch
    {
        SAV1 s1 => s1.Badges & 0xFF,
        SAV2 s2 => s2.Badges & 0xFF,
        SAV3 s3 => s3.Badges & 0xFF,
        _ => 0,
    };

    /// <summary>
    /// The Pokédex count the card shows: everything caught in Gen 1/2; in Gen 3 the national count once the
    /// National Pokédex is on, else the regional one (Hoenn, or Kanto's first 151).
    /// </summary>
    private static int DexCount(SaveFile sav)
    {
        if (sav is not SAV3 s3 || TradeRules.NationalDex3(s3))
            return sav.CaughtCount;
        int count = 0;
        for (ushort species = 1; species <= s3.MaxSpeciesID; species++)
            if (s3.GetCaught(species) && (s3 is SAV3FRLG ? species <= 151 : BoxSummary.HoennDexNumber(species) is not null))
                count++;
        return count;
    }
}
