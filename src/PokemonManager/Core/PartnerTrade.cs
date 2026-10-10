using PKHeX.Core;

namespace PokemonManager.Core;

/// <summary>
/// Karrablast and Shelmet evolve only when traded for each other (Escavalier and Accelgor). Evolve on one of them
/// trades it with the other species from another Black/White/Black 2/White 2 save: both Pokémon swap places, and
/// each arrives evolved, as the link trade would leave them.
/// </summary>
public static class PartnerTrade
{
    public sealed record Partner(SaveEntry Save, SlotRef Slot, PKM Pokemon);

    /// <summary>Whether this Pokémon evolves only by this trade.</summary>
    public static bool Applies(PKM pk) => !pk.IsEgg && pk.Format == 5
        && TradeEvolution.GetOptions(pk).Any(o => o.Method == EvolutionType.TradeShelmetKarrablast);

    /// <summary>The species it has to be traded for.</summary>
    public static Species PartnerSpecies(PKM pk) => pk.Species == (ushort)Species.Karrablast ? Species.Shelmet : Species.Karrablast;

    /// <summary>
    /// The partner species in the other saves that could be traded right now: Gen 5 saves, no Everstone (it would
    /// stop the partner evolving), and party Pokémon only where the game lets the party trade.
    /// </summary>
    public static List<Partner> Find(PKM pk, IEnumerable<SaveEntry> others)
    {
        var wanted = (ushort)PartnerSpecies(pk);
        var partners = new List<Partner>();
        foreach (var save in others.Where(s => s.Sav is SAV5))
        {
            foreach (var slot in SlotRef.AllOccupied(save.Sav))
            {
                var other = slot.Get(save.Sav);
                if (other.Species != wanted || other.IsEgg || TradeEvolution.HoldsEverstone(other) || slot.IsLocked(save.Sav))
                    continue;
                if (slot.IsParty && TradeLocation.PartyTradeBlocked(save.Sav, other) is not null)
                    continue;
                partners.Add(new Partner(save, slot, other));
            }
        }
        return partners;
    }

    /// <summary>
    /// Swaps the Pokémon at <paramref name="slot"/> of <paramref name="save"/> with <paramref name="partner"/> and
    /// evolves both. Changes are made in memory only; the caller writes both saves.
    /// </summary>
    public static OpResult Trade(SaveEntry save, SlotRef slot, Partner partner, bool allowUnofficial)
    {
        var mine = slot.Get(save.Sav);
        var theirs = partner.Slot.Get(partner.Save.Sav);
        if (!allowUnofficial)
        {
            foreach (var (pk, from, to) in new[] { (mine, save.Sav, partner.Save.Sav), (theirs, partner.Save.Sav, save.Sav) })
            {
                var rules = TradeRules.CheckTransfer(pk, from, to);
                if (!rules.Ok)
                    return rules;
            }
        }
        var toPartner = TransferService.Prepare(mine, partner.Save.Sav, allowUnofficial, out var sent);
        if (!toPartner.Ok || sent is null)
            return toPartner;
        var toMe = TransferService.Prepare(theirs, save.Sav, allowUnofficial, out var received);
        if (!toMe.Ok || received is null)
            return toMe;

        // Each takes the other's place, so neither party loses a member.
        partner.Slot.Set(partner.Save.Sav, sent.Converted);
        slot.Set(save.Sav, received.Converted);

        var lines = new List<string>();
        foreach (var (sav, at) in new[] { (partner.Save.Sav, partner.Slot), (save.Sav, slot) })
        {
            var arrived = at.Get(sav);
            var option = TradeEvolution.GetOptions(arrived).FirstOrDefault(o => o.Method == EvolutionType.TradeShelmetKarrablast);
            if (option is null || TradeEvolution.HoldsEverstone(arrived))
                continue;
            var evolved = TradeEvolution.Evolve(sav, at, option);
            if (!evolved.Ok)
                return evolved;
            lines.Add($"{Names.Species(option.Species)} arrived in {Names.Game(sav)} ({at.Describe(sav)}).");
        }
        return OpResult.Success(string.Join('\n', lines));
    }
}
