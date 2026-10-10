using PKHeX.Core;

namespace PokemonManager.Core;

public enum TransferMode
{
    /// <summary>Remove the Pokémon from the source save (the default; prevents cloning).</summary>
    Move,
    /// <summary>Leave the original in the source save.</summary>
    Copy,
}

/// <summary>
/// Moves Pokémon between save files, converting the data format when the games differ.
/// </summary>
public static class TransferService
{
    public sealed record Prepared(PKM Converted, string Notes);

    /// <summary>
    /// Converts <paramref name="pk"/> to the destination save's format without changing either save.
    /// </summary>
    /// <param name="allowUnofficial">Allow routes the games never supported (e.g. Gen 4 back to Gen 3).</param>
    public static OpResult Prepare(PKM pk, SaveFile dest, bool allowUnofficial, out Prepared? prepared)
    {
        prepared = null;
        if (pk.Species == 0)
            return OpResult.Fail("That slot is empty.");
        if (pk.IsEgg && pk.Format != dest.Generation && !allowUnofficial)
            return OpResult.Fail("Eggs can't be transferred to a different generation. Hatch it first.");

        if (pk.Species > dest.MaxSpeciesID)
            return OpResult.Fail($"{Names.Species(pk)} doesn't exist in {Names.Game(dest)}.");

        var previous = EntityConverter.AllowIncompatibleConversion;
        EntityConverter.AllowIncompatibleConversion = allowUnofficial
            ? EntityCompatibilitySetting.AllowIncompatibleSane
            : EntityCompatibilitySetting.DisallowIncompatible;
        try
        {
            var blank = dest.BlankPKM;
            if (!EntityConverter.IsCompatibleGB(blank, blank.Japanese, pk.Japanese))
                return OpResult.Fail("Japanese and international Gen 1/2 games can't trade with each other.");

            var converted = EntityConverter.ConvertToType(pk.Clone(), dest.PKMType, out var result);
            if (converted is null)
                return OpResult.Fail(Describe(result, pk, dest));

            if (converted.Species != pk.Species)
                return OpResult.Fail($"PKHeX couldn't convert {Names.Species(pk)} for {Names.Game(dest)}.");

            var issues = dest.EvaluateCompatibility(converted);
            if (issues.Count != 0)
                return OpResult.Fail("Not compatible with this game:\n" + string.Join('\n', issues));

            var notes = result == EntityConverterResult.SuccessIncompatibleReflection
                ? "Unofficial transfer: this Pokémon will likely be flagged as illegal."
                : "";
            prepared = new Prepared(converted, notes);
            return OpResult.Success(notes);
        }
        finally
        {
            EntityConverter.AllowIncompatibleConversion = previous;
        }
    }

    /// <summary>
    /// Transfers the Pokémon at <paramref name="from"/> into the first empty PC slot of <paramref name="dest"/>.
    /// Changes are made in memory only; the caller writes both saves.
    /// </summary>
    public static OpResult Transfer(SaveEntry source, SlotRef from, SaveEntry dest, TransferMode mode, bool allowUnofficial, out SlotRef placedAt)
    {
        placedAt = default;
        if (Path.GetFullPath(source.Path) == Path.GetFullPath(dest.Path))
            return OpResult.Fail("Pick a different save file as the destination.");

        var src = source.Sav;
        var pk = from.Get(src);
        if (pk.Species == 0)
            return OpResult.Fail("That slot is empty.");
        if (from.IsLocked(src))
            return OpResult.Fail("That slot is locked by the game and can't be moved.");
        if (mode == TransferMode.Move && from.IsParty && CountUsableParty(src) <= 1 && !pk.IsEgg)
            return OpResult.Fail("You can't move your last party Pokémon. Put another one in your party first.");

        if (!allowUnofficial)
        {
            var rules = TradeRules.CheckTransfer(pk, src, dest.Sav);
            if (!rules.Ok)
                return rules;
        }
        var check = Prepare(pk, dest.Sav, allowUnofficial, out var prepared);
        if (!check.Ok || prepared is null)
            return check;

        var target = SlotRef.FirstEmptyBoxSlot(dest.Sav);
        if (target is not { } slot)
            return OpResult.Fail($"Every PC box in {Names.Game(dest.Sav)} is full.");

        slot.Set(dest.Sav, prepared.Converted);
        if (mode == TransferMode.Move)
            from.Clear(src);
        placedAt = slot;

        var placed = slot.Get(dest.Sav);
        var verb = mode == TransferMode.Move ? "Moved" : "Copied";
        var msg = $"{verb} {Names.Summary(placed)} to {Names.Game(dest.Sav)}, {SlotRef.BoxName(dest.Sav, slot.Box)} slot {slot.Slot + 1}.";
        if (prepared.Notes.Length != 0)
            msg += "\n" + prepared.Notes;
        return OpResult.Success(msg);
    }

    /// <summary>Whether the Pokémon can leave its slot at all (not locked, not the last party Pokémon).</summary>
    public static OpResult CanMove(SaveFile src, SlotRef from)
    {
        var pk = from.Get(src);
        if (pk.Species == 0)
            return OpResult.Fail("That slot is empty.");
        if (from.IsLocked(src))
            return OpResult.Fail("That slot is locked by the game and can't be moved.");
        if (from.IsParty && CountUsableParty(src) <= 1 && !pk.IsEgg)
            return OpResult.Fail("You can't move your last party Pokémon. Put another one in your party first.");
        return OpResult.Success("");
    }

    private static int CountUsableParty(SaveFile sav)
    {
        int count = 0;
        for (int i = 0; i < sav.PartyCount; i++)
        {
            var pk = sav.GetPartySlotAtIndex(i);
            if (pk.Species != 0 && !pk.IsEgg)
                count++;
        }
        return count;
    }

    private static string Describe(EntityConverterResult result, PKM pk, SaveFile dest) => result switch
    {
        EntityConverterResult.NoTransferRoute =>
            $"There's no official way to send a Gen {pk.Format} Pokémon to Gen {dest.Generation}. " +
            "Turn on \"Illegal transfers\" in Settings to force it.",
        EntityConverterResult.IncompatibleSpecies => $"{Names.Species(pk)} doesn't exist in {Names.Game(dest)}.",
        EntityConverterResult.IncompatibleForm => $"This form of {Names.Species(pk)} doesn't exist in {Names.Game(dest)}.",
        EntityConverterResult.IncompatibleLanguageGB => "Japanese and international Gen 1/2 games can't trade with each other.",
        _ => $"PKHeX couldn't convert this Pokémon ({result}).",
    };
}
