using PKHeX.Core;
using PokemonManager.Gen3;

namespace PokemonManager.Core;

/// <summary>A key item that unlocks an in-game event, and the games that have that event.</summary>
public sealed record Ticket(string Name, string Key, string[] Games);

/// <summary>
/// The gallery file used to give a ticket to one save, and whether that's a legitimate distribution. No file
/// means Emerald's Eon Ticket, which is given the way Record Mixing did (see <see cref="Gen3Events.GiveEonTicketByRecordMixing"/>).
/// </summary>
public sealed record TicketChoice(Ticket Ticket, GalleryEntry? File, bool Legal, Gen3EventKind? Gen3Kind = null);

/// <summary>
/// The event tickets each game has. Every ticket a game has is offered, legitimate or not: when no official
/// distribution exists for the game and language, the gallery's debug or other-language card is used and the
/// ticket is marked illegal (e.g. the Old Sea Map outside Japanese Emerald).
/// </summary>
public static class Tickets
{
    public static readonly Ticket[] All =
    [
        new("Eon Ticket", "eonticket", ["R", "S", "E"]),
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

    /// <summary>
    /// The Pokémon only reachable with a ticket (its island or event), which PKHeX counts as ordinary catches:
    /// without the ticket in the game's language they need a distribution instead.
    /// </summary>
    public static readonly (string Key, ushort[] Species)[] Unlocks =
    [
        ("auroraticket", [(ushort)Species.Deoxys]),
        ("mysticticket", [(ushort)Species.Lugia, (ushort)Species.HoOh]),
        ("membercard", [(ushort)Species.Darkrai]),
        ("oaksletter", [(ushort)Species.Shaymin]),
        ("libertypass", [(ushort)Species.Victini]),
    ];

    /// <summary>Whether a ticket was officially distributed in this generation and language (any game).</summary>
    public static bool IsDistributed(GalleryArchive gallery, int generation, string language, string key)
        => gallery.Entries.Any(e => e.IsEventItem && e.Released && e.Generation == generation && e.Language is not null
                                    && e.ForLanguage(language) && Letters(e.Title).Contains(key));

    /// <summary>Ticket-only Pokémon whose ticket wasn't distributed in this generation and language.</summary>
    public static IEnumerable<ushort> Unreachable(GalleryArchive gallery, int generation, string language)
        => Unlocked(gallery, generation, language, distributed: false);

    /// <summary>Ticket-only Pokémon whose ticket was distributed in this generation and language (Mystery Gift has it).</summary>
    public static IEnumerable<ushort> Reachable(GalleryArchive gallery, int generation, string language)
        => Unlocked(gallery, generation, language, distributed: true);

    private static IEnumerable<ushort> Unlocked(GalleryArchive gallery, int generation, string language, bool distributed)
        => All.Where(t => Generation(t) == generation && IsDistributed(gallery, generation, language, t.Key) == distributed)
            .SelectMany(t => Unlocks.Where(u => u.Key == t.Key).SelectMany(u => u.Species));

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
            bool found = false;
            foreach (var (entry, rank) in ranked)
            {
                if (gallery.Load(entry) is { } gift && Works(gift, sav))
                {
                    result.Add(new TicketChoice(ticket, entry, Legal: rank == 0, gift.Gen3?.Kind));
                    found = true;
                    break;
                }
            }

            // Emerald got the Eon Ticket by Record Mixing with Ruby/Sapphire (Ruby/Sapphire's Mystery Event script
            // doesn't run in Emerald). That's legitimate wherever Ruby/Sapphire's ticket was distributed.
            if (!found && ticket.Key == "eonticket" && sav is SAV3E && games.Contains("E"))
            {
                bool distributed = gallery.Entries.Any(e => e.IsEventItem && e.Released && e.Generation == 3
                    && e.Language == profile.Language && e.Games.Contains("R") && Letters(e.Title).Contains(ticket.Key));
                result.Add(new TicketChoice(ticket, null, distributed));
            }
        }
        return result;
    }

    /// <summary>
    /// Why the save can't receive the ticket yet, or null: the game's menu that delivered it must be unlocked
    /// in-game (Mystery Gift; Ruby/Sapphire's and Japanese Emerald's Mystery Event), and Emerald's Record Mixing
    /// Eon Ticket needs Record Mixing.
    /// </summary>
    public static string? Locked(TicketChoice ticket, SaveFile sav) => sav switch
    {
        SAV3 gen3 when ticket.File is null => Decorations.RecordMixingUnlocked(gen3) ? null : "Visit Mauville City first to open Record Mixing.",
        SAV3 gen3 => Gen3Events.MenuLocked(gen3, ticket.Gen3Kind ?? Gen3EventKind.WonderCard),
        _ => GiftService.MysteryGiftLocked(sav),
    };

    /// <summary>0 = an official distribution for this game and language; higher = less legitimate.</summary>
    private static int Rank(GalleryEntry e, GameProfile profile)
    {
        bool language = e.Language is not null && e.ForLanguage(profile.Language);
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
