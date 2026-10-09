using PKHeX.Core;
using PokemonManager.Gen3;

namespace PokemonManager.Core;

/// <summary>A key item that unlocks an in-game event, and the games that have that event.</summary>
public sealed record Ticket(string Name, string Key, string[] Games);

/// <summary>The gallery file used to give a ticket to one save, and whether that's a legitimate distribution.</summary>
public sealed record TicketChoice(Ticket Ticket, GalleryEntry File, bool Legal);

/// <summary>
/// The event tickets each game has. Every ticket a game has is offered, legitimate or not: when no official
/// distribution exists for the game and language, the gallery's debug or other-language card is used and the
/// ticket is marked illegal (e.g. the Old Sea Map outside Japanese Emerald).
/// </summary>
public static class Tickets
{
    public static readonly Ticket[] All =
    [
        new("Eon Ticket", "eonticket", ["R", "S"]),
        new("Aurora Ticket", "auroraticket", ["FR", "LG", "E"]),
        new("Mystic Ticket", "mysticticket", ["FR", "LG", "E"]),
        new("Old Sea Map", "oldseamap", ["E"]),
        new("Member Card", "membercard", ["D", "P", "Pt"]),
        new("Oak's Letter", "oaksletter", ["D", "P", "Pt"]),
        new("Secret Key", "secretkey", ["Pt"]),
        new("Azure Flute", "azureflute", ["D", "P", "Pt"]),
        new("Enigma Stone", "enigmastone", ["HG", "SS"]),
        new("Liberty Pass", "libertypass", ["B", "W"]),
    ];

    private static int Generation(Ticket ticket) => ticket.Games[0] switch
    {
        "R" or "S" or "E" or "FR" or "LG" => 3,
        "D" or "P" or "Pt" or "HG" or "SS" => 4,
        _ => 5,
    };

    /// <summary>The tickets this save's game has, each with the best gallery file that works in the save.</summary>
    public static List<TicketChoice> For(GalleryArchive gallery, GameProfile profile, SaveFile sav)
    {
        var result = new List<TicketChoice>();
        foreach (var ticket in All)
        {
            if (Generation(ticket) != profile.Generation)
                continue;
            // A save whose exact game isn't known (no ROM found) gets the tickets of every game it could be.
            var games = profile.Games.Count == 0 ? ticket.Games : ticket.Games.Intersect(profile.Games).ToArray();
            if (games.Length == 0)
                continue;

            // Gen 3 tickets are scripts written for one game, so only that game's files will do. Gen 4/5 cards
            // made for a sibling game (e.g. Platinum's Member Card in Diamond) are tried too, and PKHeX's
            // compatibility check below decides whether they work.
            var ranked = gallery.Entries
                .Where(e => e.IsEventItem && e.Generation == profile.Generation && Letters(e.Title).Contains(ticket.Key))
                .Select(e => (Entry: e, ForGame: e.Games.Count == 0 || e.Games.Intersect(games).Any()))
                .Where(x => x.ForGame || (profile.Generation >= 4 && x.Entry.Games.Intersect(ticket.Games).Any()))
                .Select(x => (x.Entry, Rank: Rank(x.Entry, profile) + (x.ForGame ? 0 : 4)))
                .OrderBy(x => x.Rank);
            foreach (var (entry, rank) in ranked)
            {
                if (gallery.Load(entry) is { } gift && Works(gift, sav))
                {
                    result.Add(new TicketChoice(ticket, entry, Legal: rank == 0));
                    break;
                }
            }
        }
        return result;
    }

    /// <summary>0 = an official distribution for this game and language; higher = less legitimate.</summary>
    private static int Rank(GalleryEntry e, GameProfile profile)
    {
        bool language = e.Language == profile.Language;
        return (e.Released, language) switch
        {
            (true, true) => 0,
            (true, false) => 1,
            (false, true) => 2,
            _ => 3,
        };
    }

    private static bool Works(GalleryGift gift, SaveFile sav) => gift switch
    {
        { Gen3: { } g3 } => sav is SAV3 sav3 && Gen3Events.IsApplicable(sav3, g3),
        { Card: { } card } => GiftService.SupportsAlbum(sav) && card.IsCardCompatible(sav, out _),
        _ => false,
    };

    private static string Letters(string text) => new(text.ToLowerInvariant().Where(char.IsAsciiLetter).ToArray());
}
