using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using PKHeX.Core;
using PokemonManager.Gen3;

namespace PokemonManager.Core;

/// <summary>
/// A gift file found in the user's Gifts folder: either a Gen 3 event or a PKHeX-supported Mystery Gift.
/// </summary>
public sealed class GiftFile
{
    public required string Path { get; init; }
    public DataMysteryGift? Gift { get; init; }
    public Gen3EventFile? Gen3 { get; init; }

    public string DisplayName => Gen3?.DisplayName ?? GiftService.Describe(Gift!);
}

/// <summary>
/// An entry from PKHeX's built-in event database (every official distribution PKHeX knows about).
/// </summary>
public sealed record EventEntry(string Name, IEncounterable Encounter);

public static class GiftService
{
    private static readonly string[] MysteryGiftExtensions =
    [
        ".pgt", ".pcd", ".wc4", ".pgf", ".wc5full", ".wc6", ".wc6full", ".wc7", ".wc7full", ".wr7", ".wb7", ".wb7full",
        ".wc8", ".wc8full", ".wb8", ".wa8", ".wc9", ".wa9",
    ];

    public static List<GiftFile> ListFiles(string dir, SaveFile sav)
    {
        var result = new List<GiftFile>();
        if (!Directory.Exists(dir))
            return result;

        foreach (var path in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories))
        {
            if (System.IO.Path.GetFileName(path).StartsWith('.'))
                continue;
            var ext = System.IO.Path.GetExtension(path).ToLowerInvariant();
            var size = new FileInfo(path).Length;
            if (size > 0x10000)
                continue;
            try
            {
                if (Gen3Events.Extensions.Contains(ext))
                {
                    if (sav is not SAV3 sav3)
                        continue;
                    var file = Gen3Events.Parse(path);
                    if (file is not null && Gen3Events.IsApplicable(sav3, file))
                        result.Add(new GiftFile { Path = path, Gen3 = file });
                }
                else if (MysteryGiftExtensions.Contains(ext))
                {
                    var gift = MysteryGift.GetMysteryGift(File.ReadAllBytes(path), ext);
                    if (gift is not null && gift.Context == sav.Context)
                        result.Add(new GiftFile { Path = path, Gift = gift });
                }
            }
            catch (Exception)
            {
                // Unreadable or malformed file: just don't list it.
            }
        }
        result.Sort((a, b) => string.Compare(a.DisplayName, b.DisplayName, StringComparison.OrdinalIgnoreCase));
        return result;
    }

    public static string Describe(MysteryGift gift)
    {
        var title = gift.CardTitle.Replace('　', ' ').Trim();
        string what;
        if (gift.IsEntity)
            what = gift.IsEgg ? $"Egg ({Names.Species(gift.Species)})" : $"{Names.Species(gift.Species)} Lv.{gift.Level}";
        else if (gift.IsItem)
            what = "Item";
        else
            what = gift.Type;
        var id = gift.CardID > 0 ? $"#{gift.CardID:0000} " : "";
        return title.Length == 0 ? $"{id}{what}" : $"{id}{what} - {title}";
    }

    /// <summary>True when the save has a Mystery Gift album that cards can be placed into (Gen 4-7).</summary>
    public static bool SupportsAlbum(SaveFile sav) => sav is IMysteryGiftStorageProvider;

    /// <summary>
    /// Why the save can't receive Mystery Gifts yet: Diamond/Pearl/Platinum unlock Mystery Gift with the Jubilife TV
    /// questionnaire (scripts_jubilife_tv_3f.s: CheckIsMysteryGiftPhrase, UnlockMysteryGift). HeartGold/SoulSilver
    /// unlock it with the Pokédex, and Gen 5 has it from the start.
    /// </summary>
    public static string? MysteryGiftLocked(SaveFile sav) => sav switch
    {
        SAV4Sinnoh { IsMysteryGiftUnlocked: false } =>
            $"Unlock Mystery Gift in {Names.Game(sav)} first: fill in the questionnaire at Jubilife TV with \"EVERYONE HAPPY\" \"WI-FI CONNECTION\".",
        SAV4HGSS when !TradeRules.HasPokedex(sav) => $"{Names.Game(sav)} unlocks Mystery Gift once you've received the Pokédex.",
        _ => null,
    };

    /// <summary>
    /// Places a Wonder Card into the save's album so it can be picked up in-game from the delivery person,
    /// exactly as if it had been received over Wi-Fi/local wireless.
    /// </summary>
    public static OpResult InjectCard(SaveFile sav, DataMysteryGift gift)
    {
        if (sav is not IMysteryGiftStorageProvider provider)
            return OpResult.Fail($"{Names.Game(sav)} has no Mystery Gift album.");
        if (!gift.IsCardCompatible(sav, out var why))
            return OpResult.Fail($"This gift isn't compatible with {Names.Game(sav)}. {why}".Trim());
        if (gift is PCD { IsLockCapsule: true })
            return OpResult.Fail("The Lock Capsule can't be injected as a card.");
        if (MysteryGiftLocked(sav) is { } locked)
            return OpResult.Fail(locked);

        var storage = provider.MysteryGiftStorage;
        int count = storage.GiftCountMax;
        var album = new DataMysteryGift[count];
        for (int i = 0; i < count; i++)
            album[i] = storage.GetMysteryGift(i);

        // An event key item (Member Card, Azure Flute, Liberty Pass...) the save already has, or that's already waiting to be
        // picked up, isn't given twice: the real games turned a card they already had away, and the delivery man
        // would hand a second copy out. Ordinary items can come again, as from different distributions.
        if (gift.IsItem && AlreadyHas(sav, album, gift.ItemID) is { } had)
            return OpResult.Fail(had);

        if (storage is MysteryBlock4 block4)
        {
            // Gen 4 keeps the card (PCD, shown in the album) and the pending gift (PGT, handed out by the
            // delivery man) separately. A wireless download writes both, so do the same.
            int pcdSlot = -1, pgtSlot = -1;
            for (int i = 0; i < count; i++)
            {
                if (!album[i].IsEmpty)
                    continue;
                if (album[i] is PCD && pcdSlot < 0) pcdSlot = i;
                if (album[i] is PGT && pgtSlot < 0) pgtSlot = i;
            }

            // A full album (3 cards) or delivery queue (8 gifts) drops its oldest: a gift dropped before it was picked
            // up comes back by injecting its card again.
            var pgtSlots = Enumerable.Range(0, count).Where(i => album[i] is PGT).ToList();
            if (pgtSlot < 0)
                pgtSlot = DropOldest(album, pgtSlots);
            if (gift is PCD pcd)
            {
                if (pcdSlot < 0)
                    pcdSlot = DropOldest(album, Enumerable.Range(0, count).Where(i => album[i] is PCD).ToList());
                album[pcdSlot] = pcd.Clone();
                album[pgtSlot] = pcd.Gift.Clone();
            }
            else if (gift is PGT pgt)
            {
                album[pgtSlot] = pgt.Clone();
            }
            MysteryBlock4.UpdateSlotPGT(album, sav is SAV4HGSS);
            block4.IsDeliveryManActive = true;
        }
        else
        {
            if (!album.Any(g => g.Type == gift.Type))
                return OpResult.Fail($"{Names.Game(sav)} stores a different kind of card than this file.");
            int slot = Array.FindIndex(album, g => g.IsEmpty && g.Type == gift.Type);
            if (slot < 0)
            {
                // A full album throws away the oldest card whose gift was already picked up, else the oldest (its
                // gift comes back by injecting the card again).
                var slots = Enumerable.Range(0, count).Where(i => album[i].Type == gift.Type).ToList();
                int used = slots.FindIndex(i => album[i].GiftUsed);
                slot = DropOldest(album, slots, Math.Max(used, 0));
            }
            var copy = gift.Clone();
            copy.GiftUsed = false;
            album[slot] = copy;
        }

        for (int i = 0; i < count; i++)
            storage.SetMysteryGift(i, album[i]);
        if (storage is IMysteryGiftFlags flags && (uint)gift.CardID < (uint)flags.MysteryGiftReceivedFlagMax)
            flags.SetMysteryGiftReceivedFlag(gift.CardID, true);
        if (storage is MysteryBlock5 block5)
            block5.EndAccess(); // re-encrypts the album

        var where = sav.Generation switch
        {
            4 => "Pick it up from the delivery man in green at any Poké Mart.",
            _ => "Pick it up from the delivery person (Pokémon Center / Poké Mart, depending on the game).",
        };
        return OpResult.Success($"Added \"{Describe(gift)}\" to the Mystery Gift album.\n{where}");
    }

    /// <summary>The DS games' event key items (Gen 4/5 item IDs).</summary>
    private static readonly HashSet<int> EventKeyItems =
        [Tickets.ItemMemberCard, Tickets.ItemOaksLetter, Tickets.ItemAzureFlute, Tickets.ItemSecretKey, Tickets.ItemEnigmaStone, Tickets.ItemLibertyPass];

    private static string? AlreadyHas(SaveFile sav, DataMysteryGift[] album, int item)
    {
        if (!EventKeyItems.Contains(item))
            return null;
        var name = GameInfo.Strings.Item.Count > item ? GameInfo.Strings.Item[item] : "item";
        if (sav.Inventory.Pouches.Any(p => p.HasItem((ushort)item)))
            return $"You already have the {name}.";
        // Gen 4 gifts wait in their own slots (PGT); later cards carry theirs until it's picked up
        bool waiting = album.Any(g => !g.IsEmpty && g.IsItem && g.ItemID == item && (g is PGT || (g is not PCD && !g.GiftUsed)));
        return waiting ? $"The {name} is already waiting to be picked up." : null;
    }

    /// <summary>
    /// Throws away the card at <paramref name="slots"/>[<paramref name="at"/>] (the oldest by default): the cards
    /// after it move up, keeping the album in the order they arrived, and the last of the slots is returned, empty.
    /// </summary>
    private static int DropOldest(DataMysteryGift[] album, List<int> slots, int at = 0)
    {
        for (int k = at; k < slots.Count - 1; k++)
            album[slots[k]] = album[slots[k + 1]];
        return slots[^1];
    }

    /// <summary>
    /// Generates the gift Pokémon with the save's trainer details and places it in the PC.
    /// Works for every generation, including games without a Mystery Gift album. The trade rules don't apply
    /// (the real distributions didn't need the National Pokédex), but the player must have the Pokédex.
    /// </summary>
    public static OpResult Redeem(SaveFile sav, IEncounterable encounter)
    {
        if (encounter is MysteryGift { IsEntity: false })
            return OpResult.Fail("This gift is an item, not a Pokémon. Use \"Add to Mystery Gift album\" instead.");
        if (encounter is not IEncounterConvertible)
            return OpResult.Fail("PKHeX can't generate a Pokémon from this event.");
        if (TradeRules.CheckDistribution(sav) is { Ok: false } noDex)
            return noDex;
        if (encounter is MysteryGift mg && !mg.IsCardCompatible(sav, out var why))
            return OpResult.Fail($"This gift isn't compatible with {Names.Game(sav)}. {why}".Trim());

        // A new Pokémon for this recipient, as the real distribution made, and only if it's legal.
        var pk = EventPokemon.Generate(encounter, sav);
        if (pk is null)
            return OpResult.Fail("PKHeX couldn't generate a legal Pokémon from this event, so nothing was added.");
        var target = SlotRef.ForDistribution(sav);
        if (target.Value is not { } slot)
            return OpResult.Fail(target.Message);
        slot.Set(sav, pk);
        return OpResult.Success(Names.Arrived(pk));
    }

    /// <summary>
    /// A legal event Mew for a Gen 1 save, generated as the distribution did, or null. PKHeX has one Game Boy
    /// era Mew event per region; only the save's region's generates a legal Mew, so they're tried in random order.
    /// </summary>
    public static PKM? Mew(SaveFile sav, Random? random = null)
    {
        random ??= Random.Shared;
        if (sav is SAV2 gen2)
        {
            // Korean Gold/Silver can't link with any Gen 1 game: an international Mew moved up by Time Capsule
            // and traded over.
            var red = BlankSaveFile.Get(GameVersion.RD, gen2.OT, LanguageID.English);
            return Mew(red, random) is { } mew && EntityConverter.ConvertToType(mew, sav.PKMType, out _) is { } pk2 && Legality.IsLegal(pk2, sav)
                ? pk2
                : null;
        }
        foreach (var ev in BuiltInEvents(sav).Where(e => e.Encounter.Species == (ushort)Species.Mew).OrderBy(_ => random.Next()))
        {
            if (EventPokemon.Generate(ev.Encounter, sav) is { } pk && Legality.IsLegal(pk, sav))
                return pk;
        }
        return null;
    }

    /// <summary>
    /// A legal event Celebi for a Gen 2 game outside Japan, generated as the distribution did: the Pokémon Center
    /// New York one from PKHeX's Game Boy era events (the GS Ball event only ran on Japanese cartridges).
    /// </summary>
    public static PKM? Celebi(SaveFile sav, Random? random = null)
    {
        random ??= Random.Shared;
        foreach (var ev in BuiltInEvents(sav).Where(e => e.Encounter.Species == (ushort)Species.Celebi).OrderBy(_ => random.Next()))
        {
            if (EventPokemon.Generate(ev.Encounter, sav) is { } pk && Legality.IsLegal(pk, sav))
                return pk;
        }
        return null;
    }

    /// <summary>
    /// Every event PKHeX knows about for the save's game, newest generation formats first.
    /// </summary>
    public static List<EventEntry> BuiltInEvents(SaveFile sav)
    {
        IEnumerable<IEncounterable> source = sav.Context switch
        {
            EntityContext.Gen1 => Internal("PKHeX.Core.Encounters1GBEra", "Gifts"),
            EntityContext.Gen2 => Internal("PKHeX.Core.Encounters2GBEra", "Gifts"),
            EntityContext.Gen3 => Internal("PKHeX.Core.EncountersWC3", "Encounter_WC3"),
            EntityContext.Gen4 => EncounterEvent.MGDB_G4,
            EntityContext.Gen5 => EncounterEvent.MGDB_G5,
            EntityContext.Gen6 => EncounterEvent.MGDB_G6,
            EntityContext.Gen7 => EncounterEvent.MGDB_G7,
            EntityContext.Gen7b => EncounterEvent.MGDB_G7GG,
            EntityContext.Gen8 => EncounterEvent.MGDB_G8,
            EntityContext.Gen8a => EncounterEvent.MGDB_G8A,
            EntityContext.Gen8b => EncounterEvent.MGDB_G8B,
            EntityContext.Gen9 => EncounterEvent.MGDB_G9,
            EntityContext.Gen9a => EncounterEvent.MGDB_G9A,
            _ => [],
        };

        var list = new List<EventEntry>();
        foreach (var enc in source)
        {
            if (enc is MysteryGift mg)
            {
                if (!mg.IsEntity && !SupportsAlbum(sav))
                    continue; // item-only gifts need an album
                if (mg.IsEntity && !sav.CanReceiveGift(mg))
                    continue;
                list.Add(new EventEntry(Describe(mg), enc));
            }
            else
            {
                // Gen 1-3 event Pokémon were often received in one game and traded to another, so any
                // game of the same generation can hold them.
                if (enc.Species > sav.MaxSpeciesID)
                    continue;
                list.Add(new EventEntry(DescribeClassic(enc), enc));
            }
        }
        list.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
        return list;
    }

    private static string DescribeClassic(IEncounterable enc)
    {
        var what = enc.IsEgg ? $"Egg ({Names.Species(enc.Species)})" : $"{Names.Species(enc.Species)} Lv.{enc.LevelMin}";
        var from = enc is IVersion v ? $" [{v.Version}]" : "";
        if (enc is EncounterGift3 { IsFixedTrainer: true } g3)
        {
            var region = g3.Language == (byte)LanguageID.Japanese ? " JPN" : "";
            return $"{what} - OT {g3.OriginalTrainerName}{region}{from}";
        }
        return $"{what} - {enc.Name}{from}";
    }

    /// <summary>
    /// PKHeX keeps its Gen 1-3 event lists internal; read them by reflection and degrade to an empty list.
    /// </summary>
    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "PKHeX.Core is not trimmed.")]
    [UnconditionalSuppressMessage("Trimming", "IL2075", Justification = "PKHeX.Core is not trimmed.")]
    private static IEnumerable<IEncounterable> Internal(string typeName, string fieldName)
    {
        try
        {
            var type = typeof(EncounterEvent).Assembly.GetType(typeName);
            var field = type?.GetField(fieldName, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            if (field?.GetValue(null) is System.Collections.IEnumerable items)
                return items.OfType<IEncounterable>().ToList();
        }
        catch (Exception)
        {
            // fall through
        }
        return [];
    }
}
