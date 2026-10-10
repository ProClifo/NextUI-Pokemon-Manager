using System.Text.Json.Nodes;
using PKHeX.Core;

namespace PokemonManager.Core;

/// <summary>
/// What a game's Trainer Card shows, for pkmgr-box --card: the player's name, ID, money, Pokédex count, play time
/// and badges (bit 0 = the first gym's), plus Gen 3's stars and back-of-card records, drawn with the game's own
/// card art (the skin folder).
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
            ["money"] = Math.Min((long)sav.Money, 999999),
            ["hours"] = sav.PlayedHours,
            ["minutes"] = sav.PlayedMinutes,
            ["seconds"] = sav.PlayedSeconds,
            ["has_dex"] = TradeRules.HasPokedex(sav),
            ["dex"] = DexCount(sav),
            ["badges"] = Badges(sav),
        };
        if (font is not null)
            card["font"] = font;
        if (sav is SAV3 s3)
            AddGen3(card, s3);
        return card;
    }

    // include/constants/game_stat.h
    private const int StatFirstHofPlayTime = 1, StatEnteredHof = 10, StatTrades = 21, StatLinkWins = 23, StatLinkLosses = 24,
        StatPokeblocksWithFriends = 34, StatWonLinkContest = 35, StatUnionRoom = 50, StatBerryCrush = 51;

    /// <summary>
    /// The Gen 3 card's star count and the back's records, capped as the games cap them (src/trainer_card.c):
    /// "hof" [hours, minutes, seconds] of the first Hall of Fame entry (absent before it), and every record the
    /// back lists, which shows only those above zero.
    /// </summary>
    private static void AddGen3(JsonObject card, SAV3 sav)
    {
        uint Stat(int index, uint max) => Math.Min(sav.GetRecord(index), max);
        uint hof = sav.GetRecord(StatEnteredHof) != 0 ? sav.GetRecord(StatFirstHofPlayTime) : 0;
        int hours = (int)(hof >> 16), minutes = (int)((hof >> 8) & 0xFF), seconds = (int)(hof & 0xFF);
        if (hours > 999)
            (hours, minutes, seconds) = (999, 59, 59);
        bool enteredHof = hof != 0;
        if (enteredHof)
            card["hof"] = new JsonArray(hours, minutes, seconds);
        card["link_wins"] = Stat(StatLinkWins, 9999);
        card["link_losses"] = Stat(StatLinkLosses, 9999);
        card["trades"] = Stat(StatTrades, 0xFFFF);

        int stars = enteredHof ? 1 : 0;
        switch (sav)
        {
            case SAV3E e:
                card["pokeblocks"] = Stat(StatPokeblocksWithFriends, 0xFFFF);
                card["contests"] = Stat(StatWonLinkContest, 999);
                card["bp"] = (int)e.SmallBlock.BPEarned;
                stars += (HasAllHoenn(sav) ? 1 : 0) + (MuseumPaintings(e.LargeBlock) >= 5 ? 1 : 0) + (HasAllFrontierSymbols(e) ? 1 : 0);
                break;
            case SAV3RS rs:
                card["pokeblocks"] = Stat(StatPokeblocksWithFriends, 0xFFFF);
                card["contests"] = Stat(StatWonLinkContest, 999);
                // SaveBlock2.battleTower: totalBattleTowerWins at 0x570, bestBattleTowerWinStreak at 0x572
                var block = rs.SmallBlock.Data;
                int towerWins = Math.Min((int)System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(block[0x570..]), 9999);
                int towerStreak = Math.Min((int)System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(block[0x572..]), 9999);
                card["tower_wins"] = towerWins;
                card["tower_streak"] = towerStreak;
                stars += (HasAllHoenn(sav) ? 1 : 0) + (towerStreak > 49 ? 1 : 0) + (MuseumPaintings(rs.LargeBlock) > 4 ? 1 : 0);
                break;
            case SAV3FRLG frlg:
                card["union"] = Stat(StatUnionRoom, 0xFFFF);
                card["berry_crush"] = Stat(StatBerryCrush, 0xFFFF);
                bool kanto = AllCaught(sav, Enumerable.Range(1, 150));
                bool national = kanto && AllCaught(sav, Enumerable.Range(152, 97).Concat(Enumerable.Range(252, 133)));
                // Dodrio Berry Picking's berries picked and Pokémon Jump's jumps in a row
                bool minigames = frlg.SmallBlock.JoyfulBerriesInRow >= 200 && frlg.SmallBlock.JoyfulJumpInRow >= 200;
                stars += (kanto ? 1 : 0) + (national ? 1 : 0) + (minigames ? 1 : 0);
                break;
        }
        card["stars"] = Math.Min(stars, 4);
    }

    /// <summary>Every Pokémon of the Hoenn Pokédex but Jirachi and Deoxys caught (HasAllHoennMons).</summary>
    private static bool HasAllHoenn(SAV3 sav) =>
        AllCaught(sav, Enumerable.Range(1, sav.MaxSpeciesID).Where(s => BoxSummary.HoennDexNumber((ushort)s) is <= 200));

    private static bool AllCaught(SAV3 sav, IEnumerable<int> species) => species.All(s => sav.GetCaught((ushort)s));

    private static int MuseumPaintings(ISaveBlock3LargeHoenn block) =>
        Enumerable.Range(0, 5).Count(i => block.GetPainting(i, false).Species != 0);

    // include/constants/flags.h: FLAG_SYS_TOWER_SILVER = SYSTEM_FLAGS (0x860) + 0x64, gold the next, for the 7 facilities
    private static bool HasAllFrontierSymbols(SAV3E sav) =>
        Enumerable.Range(0, 7).All(i => sav.GetEventFlag(0x8C4 + 2 * i) && sav.GetEventFlag(0x8C5 + 2 * i));

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
