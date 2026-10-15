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
/// <item>A gift is an item chosen by the partner's game from MysteryGiftItems (engine/link/mystery_gift_2.asm,
/// StageDataForMysteryGift.RandomSample), partly from the partner's trainer ID.</item>
/// </list>
/// Japanese and Korean games lay SRAM out differently, so they aren't supported.
/// </summary>
public static class MysteryGift2
{
    public const int GiftsPerDay = 5;

    private const int WorkingCount = 0xBE3; // sMysteryGiftUnlocked: copied from the count when the game loads
    private const int Count = 0xBE5;        // sNumDailyMysteryGiftPartnerIDs
    private const int PartnerIds = 0xBE6;   // sDailyMysteryGiftPartnerIDs, big endian
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
    /// Receives today's remaining gifts (one from each of up to five new partners) into the bag, or the PC when
    /// its pocket is full. Returns the items, or null (and changes nothing) when there's no room.
    /// </summary>
    public static List<ushort>? Receive(SAV2 sav, int today, Random rng)
    {
        int left = Left(sav, today);
        if (left == 0)
            return [];
        var data = sav.Data;
        int count = DayPassed(sav, today) ? 0 : data[Count];
        var bag = sav.Inventory;
        var items = new List<ushort>();
        var partners = new List<ushort>();
        for (int i = 0; i < left; i++)
        {
            // a different trainer for each gift, never yourself (the game turns down the same one twice a day)
            ushort partner;
            do partner = (ushort)rng.Next(0x10000);
            while (partner == sav.TID16 || partners.Contains(partner) || SentToday(sav, count, partner));
            partners.Add(partner);
            ushort item = Items[Sample(rng, partner)];
            if (!Give(bag, item))
                return null;
            items.Add(item);
        }
        bag.CopyTo(sav);
        if (count == 0)
        {
            data[Timer] = 1; // InitOneDayCountdown
            data[Timer + 1] = (byte)(today % DaysWrap);
        }
        for (int i = 0; i < partners.Count; i++)
        {
            data[PartnerIds + 2 * (count + i)] = (byte)(partners[i] >> 8);
            data[PartnerIds + 2 * (count + i) + 1] = (byte)partners[i];
        }
        data[Count] = data[WorkingCount] = GiftsPerDay;
        return items;
    }

    private static bool SentToday(SAV2 sav, int count, ushort partner)
    {
        for (int i = 0; i < count; i++)
        {
            if ((sav.Data[PartnerIds + 2 * i] << 8 | sav.Data[PartnerIds + 2 * i + 1]) == partner)
                return true;
        }
        return false;
    }

    private static bool Give(PlayerBag bag, ushort item)
    {
        foreach (var type in new[] { InventoryType.Items, InventoryType.Balls, InventoryType.PCItems })
        {
            var pouch = bag.GetPouch(type);
            if (pouch.CanContain(item) && pouch.GiveItem(bag, item, 1) > 0)
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
