using PKHeX.Core;

namespace PokemonManager.Core;

/// <summary>
/// Gold/Silver/Crystal's Mystery Gift, as the game keeps it in SRAM bank 0 (pokecrystal/pokegold ram/sram.asm,
/// sMysteryGiftData, at $ABE2 in the English, French, German, Italian and Spanish games):
/// <list type="bullet">
/// <item>sNumDailyMysteryGiftPartnerIDs is -1 until the girl on the Goldenrod Dept. Store's 5th floor unlocks
/// Mystery Gift (UnlockMysteryGift) and the game is saved; then it counts today's gifts, five at most.</item>
/// <item>sMysteryGiftTimer is a one-day countdown: days left, then the game day it started on (InitOneDayCountdown).
/// The count goes back to 0 once a day has passed (DoMysteryGiftIfDayHasPassed).</item>
/// <item>A gift is chosen by the partner's game (engine/link/mystery_gift_2.asm, StageDataForMysteryGift): half the
/// time a decoration from MysteryGiftDecos, else an item from MysteryGiftItems, each by RandomSample, partly from the
/// partner's trainer ID. A decoration already received by Mystery Gift (sMysteryGiftDecorationsReceived) is
/// replaced by the item (engine/link/mystery_gift.asm, DoMysteryGift). The game puts received decorations in the
/// PC's decorations when the save is continued (CopyMysteryGiftReceivedDecorationsToPC).</item>
/// <item>An item waits in sBackupMysteryGiftItem for the man on the Pokémon Center's 2nd floor (GetMysteryGiftItem);
/// while one waits, no gift can be received.</item>
/// </list>
/// Japanese and Korean games lay SRAM out differently, so they aren't supported.
/// </summary>
public static class MysteryGift2
{
    public const int GiftsPerDay = 5;

    private const int WorkingItem = 0xBE2;  // sMysteryGiftItem: what the Pokémon Center 2F man hands over
    private const int WorkingCount = 0xBE3; // sMysteryGiftUnlocked: copied from the count when the game loads
    private const int WaitingItem = 0xBE4;  // sBackupMysteryGiftItem: the saved item, copied to sMysteryGiftItem on load
    private const int Count = 0xBE5;        // sNumDailyMysteryGiftPartnerIDs
    private const int PartnerIds = 0xBE6;   // sDailyMysteryGiftPartnerIDs, big endian
    private const int Decorations = 0xBF0;  // sMysteryGiftDecorationsReceived, a flag per DECOFLAG_*
    private const int Timer = 0xBFA;        // sMysteryGiftTimer
    private const int Locked = 0xFF;
    private const int DaysWrap = 140;       // wCurDay wraps at 140 (FixDays)

    /// <summary>MysteryGiftItems, by index.</summary>
    private static readonly ushort[] Items =
    [
        0xAD, 0x4E, 0x54, 0x50, 0x4F, 0x4A, 0x29, 0x33, // Berry, PRZCureBerry, Mint Berry, Ice Berry, Burnt Berry, PSNCureBerry, Guard Spec., X Defend
        0x31, 0x53, 0x2C, 0x35, 0x21, 0xB9, 0xBA, 0xBC, // X Attack, Bitter Berry, Dire Hit, X Special, X Accuracy, Eon Mail, Morph Mail, Music Mail
        0x6D, 0xAE, 0x27, 0x04, 0x2A, 0x2B, 0x41, 0x3F, // MiracleBerry, Gold Berry, Revive, Great Ball, Super Repel, Max Repel, Elixer, Ether
        0x18, 0x16, 0x22, 0x17, 0x40, 0x15, 0x28, 0x8C, // Water Stone, Fire Stone, Leaf Stone, Thunderstone, Max Ether, Max Elixer, Max Revive, Scope Lens
        0x1A, 0x3E,                                     // HP Up, PP Up (Rare Candy, Blue Sky Mail and Mirage Mail can't be picked)
    ];

    /// <summary>MysteryGiftDecos, by index: each decoration's DECOFLAG_* and name (DecorationNames).</summary>
    private static readonly (int Flag, string Name)[] Decos =
    [
        (22, "Jigglypuff Doll"), (26, "Poliwag Doll"), (27, "Diglett Doll"), (28, "Staryu Doll"),
        (29, "Magikarp Doll"), (30, "Oddish Doll"), (31, "Gengar Doll"), (32, "Shellder Doll"),
        (33, "Grimer Doll"), (34, "Voltorb Doll"), (13, "Clefairy Poster"), (14, "Jigglypuff Poster"),
        (16, "Super NES"), (35, "Weedle Doll"), (37, "Geodude Doll"), (38, "Machop Doll"),
        (8, "Magnaplant"), (9, "Tropicplant"), (15, "NES"), (17, "Nintendo 64"),
        (23, "Bulbasaur Doll"), (25, "Squirtle Doll"), (1, "Pink Bed"), (2, "Polkadot Bed"),
        (4, "Red Carpet"), (5, "Blue Carpet"), (6, "Yellow Carpet"), (7, "Green Carpet"),
        (10, "Jumboplant"), (18, "Virtual Boy"), (41, "Big Onix"), (12, "Pikachu Poster"),
        (42, "Big Lapras"), (20, "Surf Pikachu Doll"),
        (3, "Pikachu Bed"), (36, "Unown Doll"), (39, "Tentacool Doll"), // can't be picked
    ];

    /// <summary>A gift: an item waiting at the Pokémon Center, or a decoration sent home.</summary>
    public sealed record Gift(string Name, bool Decoration);

    public static bool Supported(SaveFile sav) => sav is SAV2 { Japanese: false, Korean: false };

    /// <summary>Whether the girl in the Goldenrod Dept. Store has unlocked Mystery Gift (and the game was saved since).</summary>
    public static bool Unlocked(SAV2 sav) => Supported(sav) && sav.Data[Count] != Locked;

    /// <summary>How many gifts the game would still take today (<paramref name="today"/> is the game day, wCurDay).</summary>
    public static int Left(SAV2 sav, int today)
        => !Unlocked(sav) ? 0 : DayPassed(sav, today) ? GiftsPerDay : Math.Max(0, GiftsPerDay - sav.Data[Count]);

    /// <summary>CheckDayDependentEventHL: whether the countdown ran out (days since its start, wrapping at 140).</summary>
    private static bool DayPassed(SAV2 sav, int today)
    {
        int since = today % DaysWrap - sav.Data[Timer + 1];
        if (since < 0)
            since += DaysWrap;
        return sav.Data[Timer] - since <= 0;
    }

    /// <summary>
    /// Whether an item is waiting at the Pokémon Center (sBackupMysteryGiftItem): until the man upstairs hands it
    /// over, the game takes no more gifts (DoMysteryGift's .GiftWaiting).
    /// </summary>
    public static bool ItemWaiting(SAV2 sav) => sav.Data[WaitingItem] != 0;

    /// <summary>
    /// Receives one gift from a new partner, as the game's DoMysteryGift does: a decoration is sent home (the game
    /// puts it in the PC on Continue), an item waits at the Pokémon Center 2F. Null when none can be received now:
    /// none left today, or an item still waiting.
    /// </summary>
    public static Gift? Receive(SAV2 sav, int today, Random rng)
    {
        if (Left(sav, today) == 0 || ItemWaiting(sav))
            return null;
        var data = sav.Data;
        if (DayPassed(sav, today))
        {
            // DoMysteryGiftIfDayHasPassed: a new day's five, counted from today
            data[Count] = 0;
            data[Timer] = 1; // InitOneDayCountdown
            data[Timer + 1] = (byte)(today % DaysWrap);
        }
        int count = data[Count];
        // a trainer not met today, never yourself (the game turns down the same one twice a day)
        ushort partner;
        do partner = (ushort)rng.Next(0x10000);
        while (partner == sav.TID16 || SentToday(sav, count, partner));
        data[PartnerIds + 2 * count] = (byte)(partner >> 8);
        data[PartnerIds + 2 * count + 1] = (byte)partner;
        data[Count] = data[WorkingCount] = (byte)(count + 1);

        // the partner's game: SentDeco (Random & 1), WhichItem (ID as is), WhichDeco (its bytes swapped)
        bool sendDeco = (rng.Next(256) & 1) != 0;
        ushort item = Items[Sample(rng, partner)];
        var deco = Decos[Sample(rng, (ushort)(partner << 8 | partner >> 8))];
        if (sendDeco && !DecorationReceived(sav, deco.Flag))
        {
            data[Decorations + (deco.Flag >> 3)] |= (byte)(1 << (deco.Flag & 7));
            return new Gift(deco.Name, true);
        }
        // waiting for the Pokémon Center: sBackupMysteryGiftItem, and sMysteryGiftItem the game restores it to
        data[WaitingItem] = data[WorkingItem] = (byte)item;
        return new Gift(GameInfo.GetStrings("en").GetItemStrings(EntityContext.Gen2)[item], false);
    }

    private static bool DecorationReceived(SAV2 sav, int flag) => (sav.Data[Decorations + (flag >> 3)] >> (flag & 7) & 1) != 0;

    private static bool SentToday(SAV2 sav, int count, ushort partner)
    {
        for (int i = 0; i < count; i++)
        {
            if ((sav.Data[PartnerIds + 2 * i] << 8 | sav.Data[PartnerIds + 2 * i + 1]) == partner)
                return true;
        }
        return false;
    }

    /// <summary>
    /// StageDataForMysteryGift.RandomSample with the partner's trainer ID (b = high byte, c = low byte): about 90%
    /// one of the first 16 items, then 8% of the next 8, 1.6% of the next 8, 0.4% HP Up or PP Up.
    /// </summary>
    public static int Sample(Random rng, ushort partnerId)
    {
        int b = partnerId >> 8, c = partnerId & 0xFF;
        // "ld e, $80" rotated left a times (256 times for 0): bit (a + 7) % 8
        static int Bit(int value, int a) => (value >> ((a + 7) % 8)) & 1;
        if (rng.Next(256) >= 25) // 10 percent
        {
            int a = rng.Next(8);
            return 2 * a + Bit(c, a);
        }
        if (rng.Next(256) >= 50) // 20 percent - 1
        {
            int a = rng.Next(4);
            return 0x10 + 2 * a + Bit(b, a);
        }
        if (rng.Next(256) >= 50)
            return 0x18 + ((b >> 4) & 7);
        return (b & 0x80) == 0 ? 0x20 : 0x21;
    }
}
