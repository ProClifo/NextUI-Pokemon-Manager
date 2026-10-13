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
                    result.Add(new TicketChoice(ticket, entry, Legal: PokemonLegal(ticket.Key, sav), gift.Gen3?.Kind));
                    found = true;
                    break;
                }
            }

            // Emerald got the Eon Ticket by Record Mixing with Ruby/Sapphire (Ruby/Sapphire's Mystery Event script
            // doesn't run in Emerald).
            if (!found && ticket.Key == "eonticket" && sav is SAV3E && games.Contains("E"))
                result.Add(new TicketChoice(ticket, null, PokemonLegal(ticket.Key, sav)));
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

    /// <summary>
    /// Whether the save already received the ticket properly: it has the item (in the bag, or Gen 3's PC to
    /// withdraw it from) and what the event's own delivery set, which the game checks alongside the item.
    /// Gen 3: the ferry's FLAG_ENABLE_SHIP_* (pokeemerald/pokefirered flags.h; the Wonder Card scripts set them).
    /// Gen 4: the distribution event var holding its magic number (pokeplatinum system_vars.c, set by the
    /// delivery man in scrcmd_mystery_gift.c; HeartGold/SoulSilver's Enigma Stone the same, var 0x4043). Steven
    /// takes the Enigma Stone at the Pewter Museum (scr_seq_T03R0101), so the event's progress (var 0x4083)
    /// counts too. Black/White: just the Liberty Pass, as they have no decompilation to say more.
    /// </summary>
    public static bool Received(TicketChoice ticket, SaveFile sav) => (ticket.Ticket.Key, sav) switch
    {
        ("eonticket", SAV3 gen3) => Gen3Events.HasEonTicket(gen3),
        ("auroraticket", SAV3 gen3) => Gen3Ticket(gen3, ItemAuroraTicket, gen3 is SAV3E ? 0x8D5 : 0x84B),
        ("mysticticket", SAV3 gen3) => Gen3Ticket(gen3, ItemMysticTicket, gen3 is SAV3E ? 0x8E0 : 0x84A),
        ("oldseamap", SAV3E e) => Gen3Ticket(e, ItemOldSeaMap, 0x8D6),
        ("membercard", SAV4Sinnoh s4) => Gen4Ticket(s4, ItemMemberCard, 0x43, 0x1209),
        ("oaksletter", SAV4Sinnoh s4) => Gen4Ticket(s4, ItemOaksLetter, 0x44, 0x1112),
        ("azureflute", SAV4Sinnoh s4) => Gen4Ticket(s4, ItemAzureFlute, 0x45, 0x1123),
        ("secretkey", SAV4Sinnoh s4) => Gen4Ticket(s4, ItemSecretKey, 0x46, 0x1103),
        ("enigmastone", SAV4HGSS hgss) => hgss.GetWork(0x43) == 0x06F2 && (HasItem(hgss, ItemEnigmaStone) || hgss.GetWork(0x83) >= 1),
        ("libertypass", SAV5 gen5) => HasItem(gen5, ItemLibertyPass),
        _ => false,
    };

    internal const int ItemAuroraTicket = 371, ItemMysticTicket = 370, ItemOldSeaMap = 376; // Gen 3 item IDs
    internal const int ItemOaksLetter = 452, ItemMemberCard = 454, ItemAzureFlute = 455, ItemSecretKey = 467, ItemEnigmaStone = 536;
    internal const int ItemLibertyPass = 574;

    private static bool Gen3Ticket(SAV3 sav, int item, int flag) => sav.GetEventFlag(flag) && HasItem(sav, item);

    private static bool Gen4Ticket(SAV4 sav, int item, int work, int magic) => sav.GetWork(work) == magic && HasItem(sav, item);

    private static bool HasItem(SaveFile sav, int item)
        => sav.Inventory.Pouches.Any(p => p.Type is InventoryType.KeyItems or InventoryType.PCItems && p.HasItem((ushort)item));

    /// <summary>The Pokémon each ticket leads to (the Secret Key only changes Rotom's form).</summary>
    private static readonly Dictionary<string, Species[]> TicketPokemon = new()
    {
        ["eonticket"] = [Species.Latias, Species.Latios],
        ["auroraticket"] = [Species.Deoxys],
        ["mysticticket"] = [Species.Lugia, Species.HoOh],
        ["oldseamap"] = [Species.Mew],
        ["membercard"] = [Species.Darkrai],
        ["oaksletter"] = [Species.Shaymin],
        ["azureflute"] = [Species.Arceus],
        ["enigmastone"] = [Species.Latias, Species.Latios],
        ["libertypass"] = [Species.Victini],
    };

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<(string, GameVersion, int), bool> LegalCache = new();

    /// <summary>
    /// Whether PKHeX accepts the Pokémon a ticket leads to as this game would make it: an in-game encounter for one
    /// of them in the save's game, caught in the save's language. Darkrai and Shaymin only came to Platinum, Arceus
    /// at the Hall of Origin never; the Faraway Island Mew only to Japanese Emerald (the Old Sea Map was Japan's) and
    /// Birth Island Deoxys only to other languages' Emerald.
    /// </summary>
    public static bool PokemonLegal(string key, SaveFile sav)
    {
        if (!TicketPokemon.TryGetValue(key, out var species))
            return true; // nothing caught through it
        return LegalCache.GetOrAdd((key, sav.Version, sav.Language), _ =>
        {
            var versions = Enum.GetValues<GameVersion>().Where(v => v != sav.Version && sav.Version.Contains(v)).DefaultIfEmpty(sav.Version).ToArray();
            foreach (var s in species)
            {
                var template = sav.BlankPKM;
                template.Species = (ushort)s;
                // the fixed encounters (EncounterStatic3/4/5): islands, legendary rooms and event gifts
                foreach (var e in EncounterMovesetGenerator.GenerateEncounters(template, sav, ReadOnlyMemory<ushort>.Empty, versions)
                             .Where(e => e.GetType().Name.StartsWith("EncounterStatic", StringComparison.Ordinal)))
                {
                    try
                    {
                        // as caught in this game: its language, and the species' name in it
                        var pk = e.ConvertToPKM(sav);
                        pk.Language = sav.Language;
                        pk.ClearNickname();
                        pk.RefreshChecksum();
                        if (new LegalityAnalysis(pk).Valid)
                            return true;
                    }
                    catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or IndexOutOfRangeException)
                    {
                    }
                }
            }
            return false;
        });
    }

    /// <summary>The ticket a gallery file gives, if it's one.</summary>
    public static Ticket? ForEntry(GalleryEntry e)
        => e.IsEventItem ? All.FirstOrDefault(t => Letters(e.Title).Contains(t.Key)) : null;

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
