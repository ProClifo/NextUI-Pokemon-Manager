using PKHeX.Core;

namespace PokemonManager.Core;

/// <summary>
/// A trade evolution available to a specific Pokémon.
/// </summary>
public sealed record TradeEvolutionOption(
    ushort Species,
    byte Form,
    EvolutionType Method,
    ushort RequiredItem,
    bool HoldsRequiredItem,
    string Label)
{
    /// <summary>True when a real trade would evolve the Pokémon as it is right now.</summary>
    public bool ConditionsMet => Method switch
    {
        EvolutionType.TradeHeldItem => HoldsRequiredItem,
        EvolutionType.TradeShelmetKarrablast => false,
        _ => true,
    };
}

/// <summary>
/// Performs evolutions that normally need a link trade, using PKHeX's evolution tables.
/// </summary>
public static class TradeEvolution
{
    public static IReadOnlyList<TradeEvolutionOption> GetOptions(PKM pk)
    {
        if (pk.Species == 0 || pk.IsEgg)
            return [];

        var tree = EvolutionTree.GetEvolutionTree(pk.Context);
        var methods = tree.Forward.GetForward(pk.Species, pk.Form).Span;
        var result = new List<TradeEvolutionOption>();
        foreach (var m in methods)
        {
            if (!m.Method.IsTrade)
                continue;

            var method = m.Method;
            ushort item = method == EvolutionType.TradeHeldItem ? m.Argument : (ushort)0;
            if (pk.Context == EntityContext.Gen2 && method == EvolutionType.Trade && Gen2TradeItem(pk.Species) is { } gen2Item)
            {
                method = EvolutionType.TradeHeldItem;
                item = gen2Item;
            }
            bool holds = item != 0 && pk.HeldItem == item;
            var target = Names.Species(m.Species);
            var label = method switch
            {
                EvolutionType.TradeHeldItem when holds => $"{target} (uses held {Names.Item(item, pk.Context)})",
                EvolutionType.TradeHeldItem => $"{target} (normally needs {Names.Item(item, pk.Context)})",
                EvolutionType.TradeShelmetKarrablast => $"{target} (normally traded for {(pk.Species == (ushort)PKHeX.Core.Species.Karrablast ? "Shelmet" : "Karrablast")})",
                _ => target,
            };
            result.Add(new TradeEvolutionOption(m.Species, m.GetDestinationForm(pk.Form), method, item, holds, label));
        }
        return result;
    }

    /// <summary>
    /// PKHeX's Gen 2 evolution table stores held-item trade evolutions as plain trades, so look the item up
    /// by name in the Gen 2 item list.
    /// </summary>
    private static ushort? Gen2TradeItem(ushort species)
    {
        string? name = (Species)species switch
        {
            PKHeX.Core.Species.Poliwhirl or PKHeX.Core.Species.Slowpoke => "King's Rock",
            PKHeX.Core.Species.Onix or PKHeX.Core.Species.Scyther => "Metal Coat",
            PKHeX.Core.Species.Seadra => "Dragon Scale",
            PKHeX.Core.Species.Porygon => "Up-Grade",
            _ => null,
        };
        if (name is null)
            return null;
        int index = Array.IndexOf(GameInfo.Strings.GetItemStrings(EntityContext.Gen2), name);
        return index > 0 ? (ushort)index : null;
    }

    public static bool HoldsEverstone(PKM pk)
        => pk.HeldItem != 0 && Names.Item(pk.HeldItem, pk.Context).Equals("Everstone", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Evolves the Pokémon in <paramref name="slot"/>. The held item is consumed if it was the required one.
    /// Changes are made in memory only.
    /// </summary>
    public static OpResult Evolve(SaveFile sav, SlotRef slot, TradeEvolutionOption option)
    {
        var pk = slot.Get(sav).Clone();
        if (pk.Species == 0 || pk.IsEgg)
            return OpResult.Fail("There's no Pokémon here that can evolve.");
        if (HoldsEverstone(pk))
            return OpResult.Fail($"{Names.Summary(pk)} is holding an Everstone, which stops it from evolving.");

        var before = Names.Species(pk.Species);
        bool keepNickname = pk.IsNicknamed;

        pk.Species = option.Species;
        pk.Form = option.Form;
        bool consumed = false;
        if (option.Method == EvolutionType.TradeHeldItem && pk.HeldItem == option.RequiredItem)
        {
            pk.HeldItem = 0;
            consumed = true;
        }

        if (pk.Format >= 3)
        {
            int index = pk.AbilityNumber switch { 2 => 1, 4 => 2, _ => 0 };
            pk.RefreshAbility(index);
        }
        if (!keepNickname)
            pk.SetDefaultNickname();
        pk.ResetPartyStats();
        pk.RefreshChecksum();

        slot.Set(sav, pk);

        var after = slot.Get(sav);
        var msg = $"{before} evolved into {Names.Species(after.Species)}!";
        if (consumed)
            msg += $"\nThe {Names.Item(option.RequiredItem, pk.Context)} was used up.";
        else if (option.Method == EvolutionType.TradeHeldItem)
            msg += $"\n(It wasn't holding {Names.Item(option.RequiredItem, pk.Context)}; evolved anyway.)";
        msg += $"\nLegality: {Names.Legality(after)}";
        return OpResult.Success(msg);
    }

    /// <summary>
    /// Evolves every Pokémon in the save whose trade evolution conditions are met right now
    /// (no item, or already holding the right item). Returns how many evolved.
    /// </summary>
    public static (int Count, List<string> Lines) EvolveAllEligible(SaveFile sav)
    {
        var lines = new List<string>();
        foreach (var slot in SlotRef.AllOccupied(sav).ToList())
        {
            var pk = slot.Get(sav);
            if (HoldsEverstone(pk))
                continue;
            var option = GetOptions(pk).FirstOrDefault(o => o.ConditionsMet);
            if (option is null)
                continue;
            var before = Names.Summary(pk);
            var result = Evolve(sav, slot, option);
            if (result.Ok)
                lines.Add($"{before} -> {Names.Species(option.Species)}");
        }
        return (lines.Count, lines);
    }
}
