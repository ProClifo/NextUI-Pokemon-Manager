using PKHeX.Core;

namespace PokemonManager.Core;

/// <summary>
/// Points at a party slot (<see cref="Box"/> == -1) or a PC box slot.
/// </summary>
public readonly record struct SlotRef(int Box, int Slot)
{
    public const int PartyBox = -1;

    public bool IsParty => Box == PartyBox;

    public static SlotRef Party(int slot) => new(PartyBox, slot);

    public override string ToString() => IsParty ? $"Party {Slot + 1}" : $"Box {Box + 1}, slot {Slot + 1}";

    public PKM Get(SaveFile sav) => IsParty ? sav.GetPartySlotAtIndex(Slot) : sav.GetBoxSlotAtIndex(Box, Slot);

    public void Set(SaveFile sav, PKM pk)
    {
        if (IsParty)
            sav.SetPartySlotAtIndex(pk, Slot);
        else
            sav.SetBoxSlotAtIndex(pk, Box, Slot);
    }

    /// <summary>Empties the slot. Party slots are compacted the way the games do it.</summary>
    public void Clear(SaveFile sav)
    {
        if (IsParty)
            sav.DeletePartySlot(Slot);
        else
            sav.SetBoxSlotAtIndex(sav.BlankPKM, Box, Slot);
    }

    public bool IsLocked(SaveFile sav) => !IsParty && sav.IsBoxSlotOverwriteProtected(Box, Slot);

    public static string BoxName(SaveFile sav, int box)
    {
        if (sav is IBoxDetailName named)
        {
            var name = named.GetBoxName(box);
            if (!string.IsNullOrWhiteSpace(name))
                return name;
        }
        return $"Box {box + 1}";
    }

    /// <summary>Where the slot is, for messages: "your party" or "BOX 1, slot 3".</summary>
    public string Describe(SaveFile sav) => IsParty ? "your party" : $"{BoxName(sav, Box)}, slot {Slot + 1}";

    /// <summary>
    /// Where a distribution Pokémon goes, as in the games: the party. Gen 1-3 distributions (trades at events,
    /// distribution cartridges, the Colosseum bonus disc) needed room in the party; Gen 4/5's delivery person
    /// sends the Pokémon to the PC when the party is full.
    /// </summary>
    public static OpResult<SlotRef> ForDistribution(SaveFile sav)
    {
        if (sav.PartyCount < 6)
            return OpResult<SlotRef>.Success(Party(sav.PartyCount));
        if (sav.Generation <= 3)
            return OpResult<SlotRef>.Fail("Your party is full. Like the original distributions, this needs a free place in your party.");
        return FirstEmptyBoxSlot(sav) is { } box
            ? OpResult<SlotRef>.Success(box)
            : OpResult<SlotRef>.Fail("Your party and every PC box are full.");
    }

    /// <summary>Finds the first empty, writable PC slot.</summary>
    public static SlotRef? FirstEmptyBoxSlot(SaveFile sav)
    {
        for (int box = 0; box < sav.BoxCount; box++)
        {
            for (int slot = 0; slot < sav.BoxSlotCount; slot++)
            {
                if (sav.IsBoxSlotOverwriteProtected(box, slot))
                    continue;
                if (sav.GetBoxSlotAtIndex(box, slot).Species == 0)
                    return new SlotRef(box, slot);
            }
        }
        return null;
    }

    /// <summary>Every occupied party and box slot.</summary>
    public static IEnumerable<SlotRef> AllOccupied(SaveFile sav)
    {
        for (int i = 0; i < sav.PartyCount; i++)
            yield return Party(i);
        for (int box = 0; box < sav.BoxCount; box++)
        {
            for (int slot = 0; slot < sav.BoxSlotCount; slot++)
            {
                if (sav.GetBoxSlotAtIndex(box, slot).Species != 0)
                    yield return new SlotRef(box, slot);
            }
        }
    }
}

/// <summary>A value, or why there isn't one.</summary>
public readonly record struct OpResult<T>(T? Value, string Message) where T : struct
{
    public bool Ok => Value.HasValue;
    public static OpResult<T> Success(T value) => new(value, "");
    public static OpResult<T> Fail(string message) => new(null, message);
}

public readonly record struct OpResult(bool Ok, string Message)
{
    public static OpResult Success(string message) => new(true, message);
    public static OpResult Fail(string message) => new(false, message);
}
