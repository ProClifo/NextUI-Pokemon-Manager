using System.Buffers.Binary;
using PKHeX.Core;
using PokemonManager.Core;

namespace PokemonManager.Gen3;

public enum Gen3EventKind
{
    /// <summary>.wc3: Wonder Card plus the RAM script the Pokémon Center delivery man runs (FR/LG/E).</summary>
    WonderCard,
    /// <summary>.wn3: Wonder News (FR/LG/E).</summary>
    WonderNews,
    /// <summary>.me3: Mystery Event RAM script plus record-mixing item (R/S/E), e.g. the Eon Ticket e-Card.</summary>
    MysteryEvent,
    /// <summary>.ect: e-Reader Battle e-Card trainer (all Gen 3 handheld games).</summary>
    ECardTrainer,
    /// <summary>.ecb: e-Reader Berry (Ruby/Sapphire).</summary>
    ECardBerry,
}

/// <summary>
/// A Gen 3 event file in the formats used by suloku's Gen III Mystery Gift Tool and the PKHeX WC3 plugin.
/// </summary>
public sealed class Gen3EventFile
{
    public required string Path { get; init; }
    public required Gen3EventKind Kind { get; init; }
    public required byte[] Data { get; init; }
    /// <summary>Japanese layout (only meaningful for Wonder Cards and Wonder News).</summary>
    public bool Japanese { get; init; }

    public string DisplayName
    {
        get
        {
            var file = System.IO.Path.GetFileNameWithoutExtension(Path);
            string title = Kind switch
            {
                Gen3EventKind.WonderCard => Gen3Text(Data.AsSpan(0xE, Japanese ? 18 : 40), Japanese),
                Gen3EventKind.WonderNews => Gen3Text(Data.AsSpan(8, Japanese ? 20 : 40), Japanese),
                Gen3EventKind.ECardBerry => Gen3Text(Data.AsSpan(0, 7), false),
                Gen3EventKind.ECardTrainer => Gen3Text(Data.AsSpan(4, 7), false),
                _ => "",
            };
            var label = Gen3Events.KindName(Kind);
            var region = Kind is Gen3EventKind.WonderCard or Gen3EventKind.WonderNews ? (Japanese ? " [JPN]" : "") : "";
            return title.Length == 0 ? $"{label}{region}: {file}" : $"{label}{region}: {title} ({file})";
        }
    }

    private static string Gen3Text(ReadOnlySpan<byte> data, bool japanese)
    {
        try
        {
            return StringConverter3.GetString(data, japanese).Trim();
        }
        catch
        {
            return "";
        }
    }
}

/// <summary>
/// Reads Gen 3 event files and writes them into Ruby/Sapphire/Emerald/FireRed/LeafGreen saves.
/// File formats and import procedure match the PKHeX WC3 plugin (2.6.0) and suloku's Gen III Mystery Gift
/// Tool; game-side details (flags, checksums, Wonder Card save routine) follow the pret decompilations.
/// Data goes through PKHeX's own Gen 3 block accessors so offsets stay in one place.
/// </summary>
public static class Gen3Events
{
    // Sizes of the on-disk formats.
    public const int CardSize = 0x150;           // u32 CRC + 0x14C card
    public const int CardSizeJP = 0xA8;          // u32 CRC + 0xA4 card
    public const int CardMetadataSize = 0x28;    // u32 CRC + 0x24 metadata (link stats/stamps/icon)
    public const int ScriptSize = 0x3EC;         // u32 checksum + 1000-byte RAM script
    public const int WC3Size = CardSize + CardMetadataSize + 0x28 + ScriptSize;       // 1420
    public const int WC3SizeJP = CardSizeJP + CardMetadataSize + 0x28 + ScriptSize;   // 1252
    public const int WN3Size = 0x1C0;            // 448
    public const int WN3SizeJP = 0xE4;           // 228
    public const int ME3ScriptOnlySize = ScriptSize;          // 1004: RAM script only
    public const int ME3Size = ScriptSize + RecordMixing3Gift.SIZE; // 1012: RAM script + record-mixing item
    public const int ECTSize = 188;
    public const int ECBSizeRS = 0x530;          // 1328: R/S berry with sprite, palette and descriptions
    public const int ECBSizeFRLGE = 0x34;        // 52: FR/LG/E berry

    private const byte RamScriptMagic = 0x33;    // RAM_SCRIPT_MAGIC
    private const byte CardTypeLinkStat = 2;      // CARD_TYPE_LINK_STAT
    private const int VarEnigmaBerryAvailableRSE = 0x2D;  // VAR_ENIGMA_BERRY_AVAILABLE (0x402D)
    private const int VarEnigmaBerryAvailableFRLG = 0x33; // VAR_ENIGMA_BERRY_AVAILABLE (0x4033)

    // Flag bytes within the "large" block (save sectors 1-4 concatenated, as exposed by SAV3.Large).
    private const int Sector2 = 0xF80;

    public static string KindName(Gen3EventKind kind) => kind switch
    {
        Gen3EventKind.WonderCard => "Wonder Card",
        Gen3EventKind.WonderNews => "Wonder News",
        Gen3EventKind.MysteryEvent => "Mystery Event",
        Gen3EventKind.ECardTrainer => "e-Card Trainer",
        Gen3EventKind.ECardBerry => "e-Reader Berry",
        _ => kind.ToString(),
    };

    public static readonly string[] Extensions = [".wc3", ".wn3", ".me3", ".ect", ".ecb"];

    public static Gen3EventFile? Parse(string path)
    {
        var ext = System.IO.Path.GetExtension(path).ToLowerInvariant();
        if (!Extensions.Contains(ext))
            return null;
        var data = File.ReadAllBytes(path);
        return Parse(path, data);
    }

    public static Gen3EventFile? Parse(string path, byte[] data)
    {
        var ext = System.IO.Path.GetExtension(path).ToLowerInvariant();
        return (ext, data.Length) switch
        {
            (".wc3", WC3Size) => new() { Path = path, Kind = Gen3EventKind.WonderCard, Data = data },
            (".wc3", WC3SizeJP) => new() { Path = path, Kind = Gen3EventKind.WonderCard, Data = data, Japanese = true },
            (".wn3", WN3Size) => new() { Path = path, Kind = Gen3EventKind.WonderNews, Data = data },
            (".wn3", WN3SizeJP) => new() { Path = path, Kind = Gen3EventKind.WonderNews, Data = data, Japanese = true },
            (".me3", ME3Size or ME3ScriptOnlySize) => new() { Path = path, Kind = Gen3EventKind.MysteryEvent, Data = data },
            (".ect", ECTSize) => new() { Path = path, Kind = Gen3EventKind.ECardTrainer, Data = data },
            (".ecb", ECBSizeRS or ECBSizeFRLGE) => new() { Path = path, Kind = Gen3EventKind.ECardBerry, Data = data },
            _ => null,
        };
    }

    /// <summary>Whether <paramref name="file"/> can go into this save at all (used to filter lists).</summary>
    public static bool IsApplicable(SAV3 sav, Gen3EventFile file) => file.Kind switch
    {
        Gen3EventKind.WonderCard or Gen3EventKind.WonderNews => sav is SAV3E or SAV3FRLG && file.Japanese == sav.Japanese,
        Gen3EventKind.MysteryEvent => sav is SAV3RS or SAV3E,
        Gen3EventKind.ECardTrainer => true,
        Gen3EventKind.ECardBerry => file.Data.Length == (sav is SAV3RS ? ECBSizeRS : ECBSizeFRLGE),
        _ => false,
    };

    /// <summary>
    /// Checks <paramref name="file"/> against the save and writes it in memory. Like the WC3 plugin, stale
    /// checksums in the file are recalculated (the message says so) rather than refusing the file.
    /// </summary>
    public static OpResult Inject(SAV3 sav, Gen3EventFile file)
    {
        if (MenuLocked(sav, file.Kind) is { } locked)
            return OpResult.Fail(locked);
        var repaired = new List<string>();
        var result = file.Kind switch
        {
            Gen3EventKind.WonderCard => InjectWonderCard(sav, file, repaired),
            Gen3EventKind.WonderNews => InjectWonderNews(sav, file, repaired),
            Gen3EventKind.MysteryEvent => InjectMysteryEvent(sav, file, repaired),
            Gen3EventKind.ECardTrainer => InjectECardTrainer(sav, file, repaired),
            Gen3EventKind.ECardBerry => InjectECardBerry(sav, file, repaired),
            _ => OpResult.Fail("Unknown event type."),
        };
        if (result.Ok && repaired.Count != 0)
            return OpResult.Success($"{result.Message}\n(Recalculated the file's {string.Join(", ", repaired)} checksum.)");
        return result;
    }

    private static OpResult InjectWonderCard(SAV3 sav, Gen3EventFile file, List<string> repaired)
    {
        if (sav.LargeBlock is not ISaveBlock3LargeExpansion block)
            return OpResult.Fail("Wonder Cards only work in FireRed, LeafGreen and Emerald. Ruby/Sapphire use Mystery Events (.me3).");
        if (file.Japanese != sav.Japanese)
            return OpResult.Fail(file.Japanese
                ? "This is a Japanese Wonder Card; it won't work in a non-Japanese game."
                : "This is an international Wonder Card; it won't work in a Japanese game.");

        var data = file.Data.ToArray();
        int cardSize = file.Japanese ? CardSizeJP : CardSize;
        var card = new WonderCard3(data.AsMemory(0, cardSize));
        if (card.CardID == 0)
            return OpResult.Fail("This Wonder Card has no event flag set, so the game would reject it.");
        if (!card.IsChecksumValid())
        {
            card.FixChecksum();
            repaired.Add("card");
        }
        var script = new MysteryEvent3(data.AsMemory(data.Length - ScriptSize, ScriptSize));
        if (!script.IsChecksumValid())
        {
            script.FixChecksum();
            repaired.Add("script");
        }

        block.SetWonderCard(sav.Japanese, card.Data);
        if (card.Type == CardTypeLinkStat)
        {
            // Link-stat cards carry their battle/trade record in the metadata block; keep it, as the plugin does.
            block.SetWonderCardExtra(sav.Japanese, data.AsSpan(cardSize, CardMetadataSize));
        }
        else
        {
            // The game's SaveWonderCard(): clear the metadata, then copy the card's icon species into it.
            var meta = new byte[CardMetadataSize];
            BinaryPrimitives.WriteUInt16LittleEndian(meta.AsSpan(0xA), card.Icon);
            block.SetWonderCardExtra(sav.Japanese, meta);
        }
        sav.LargeBlock.MysteryData = script;

        var msg = $"Wonder Card \"{card.Title.Trim()}\" injected.\nTalk to the delivery man in green on the 2nd floor of any Pokémon Center.";
        msg += "\nNote: a game holds one card/event script at a time; this replaced any previous one.";
        return OpResult.Success(msg);
    }

    private static OpResult InjectWonderNews(SAV3 sav, Gen3EventFile file, List<string> repaired)
    {
        if (sav.LargeBlock is not ISaveBlock3LargeExpansion block)
            return OpResult.Fail("Wonder News only works in FireRed, LeafGreen and Emerald.");
        if (file.Japanese != sav.Japanese)
            return OpResult.Fail("This Wonder News is for a different language/region than this save.");

        var news = new WonderNews3(file.Data.ToArray());
        if (news.NewsID == 0)
            return OpResult.Fail("This Wonder News has no ID, so the game would reject it.");
        if (!news.IsChecksumValid())
        {
            news.FixChecksum();
            repaired.Add("news");
        }
        block.SetWonderNews(sav.Japanese, news.Data);

        return OpResult.Success("Wonder News injected. Read it from Mystery Gift > Wonder News on the title menu.");
    }

    private static OpResult InjectMysteryEvent(SAV3 sav, Gen3EventFile file, List<string> repaired)
    {
        if (sav is SAV3FRLG)
            return OpResult.Fail("FireRed/LeafGreen don't have Mystery Events. Use a Wonder Card (.wc3) instead.");
        if (sav.LargeBlock is not ISaveBlock3LargeHoenn hoenn)
            return OpResult.Fail("Mystery Events only work in Ruby, Sapphire and Emerald.");

        var data = file.Data.ToArray();
        var script = data.AsSpan(0, ScriptSize);
        uint stored = BinaryPrimitives.ReadUInt32LittleEndian(script);
        uint rs = ByteSum(script[4..]);
        uint em = Crc16(script[4..]);
        bool forRS = sav is SAV3RS;
        uint expected = forRS ? rs : em;
        if (stored != expected)
        {
            // A checksum that's valid for the *other* game means the file is for that game.
            if (stored != 0 && stored == (forRS ? em : rs))
                return OpResult.Fail(forRS ? "This Mystery Event is for Emerald, not Ruby/Sapphire." : "This Mystery Event is for Ruby/Sapphire, not Emerald.");
            // R/S keep a 32-bit byte sum; Emerald a CRC16 (pokeruby/pokeemerald CalculateRamScriptChecksum).
            BinaryPrimitives.WriteUInt32LittleEndian(script, expected);
            repaired.Add("script");
        }
        sav.LargeBlock.MysteryData = forRS ? new MysteryEvent3RS(data.AsMemory(0, ScriptSize)) : new MysteryEvent3(data.AsMemory(0, ScriptSize));

        // A Wonder Card left behind would point at a script that's no longer there (the WC3 plugin does the same).
        if (sav.LargeBlock is ISaveBlock3LargeExpansion expansion)
            expansion.SetWonderCard(sav.Japanese, new byte[sav.Japanese ? CardSizeJP : CardSize]);

        string msg = "Mystery Event injected.";
        if (data.Length == ME3Size)
        {
            var gift = new RecordMixing3Gift(data.AsMemory(ScriptSize, RecordMixing3Gift.SIZE));
            if (!gift.IsChecksumValid())
            {
                gift.FixChecksum();
                repaired.Add("record-mixing item");
            }
            hoenn.RecordMixingGift = gift;
            if (gift.Item != 0)
                msg += $"\nIt also shares {Names.Item(gift.Item, EntityContext.Gen3)} with friends through Record Mixing.";
        }

        msg += sav is SAV3RS
            ? "\nFollow the event's original instructions (for the Eon Ticket: visit your dad at the Petalburg Gym)."
            : "\nThe event is now active in your game.";
        msg += "\nNote: this replaced any Wonder Card or event script already in the save.";
        return OpResult.Success(msg);
    }

    private static OpResult InjectECardTrainer(SAV3 sav, Gen3EventFile file, List<string> repaired)
    {
        var data = file.Data.ToArray();
        uint stored = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(ECTSize - 4));
        uint expected = WordSum(data.AsSpan(0, ECTSize - 4));
        if (stored != expected)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(ECTSize - 4), expected);
            repaired.Add("trainer");
        }
        data.CopyTo(sav.SmallBlock.EReaderTrainer);
        return OpResult.Success("e-Card Trainer injected. It replaces any e-Reader trainer already stored in the save.");
    }

    private static OpResult InjectECardBerry(SAV3 sav, Gen3EventFile file, List<string> repaired)
    {
        var target = sav.LargeBlock.EReaderBerry;
        if (file.Data.Length != target.Length)
        {
            return OpResult.Fail(file.Data.Length == ECBSizeRS
                ? "This e-Reader Berry is in the Ruby/Sapphire format; it won't work in FireRed, LeafGreen or Emerald."
                : "This e-Reader Berry is in the FireRed/LeafGreen/Emerald format; it won't work in Ruby/Sapphire.");
        }

        var data = file.Data.ToArray();
        var body = data.AsSpan(0, data.Length - 4);
        uint expected = sav is SAV3RS ? BerryChecksum(body) : ByteSum(body);
        if (BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(data.Length - 4)) != expected)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(data.Length - 4), expected);
            repaired.Add("berry");
        }
        data.CopyTo(target);
        sav.SetWork(sav is SAV3FRLG ? VarEnigmaBerryAvailableFRLG : VarEnigmaBerryAvailableRSE, 1);
        return OpResult.Success($"e-Reader Berry {sav.EBerryName} injected. It replaces the Enigma Berry data in the save, as scanning the e-Card would.");
    }

    // The menus events arrive through, unlocked in-game by the questionnaire (pokeruby/pokeemerald/pokefirered flags.h).
    internal const int FlagExdataEnableRS = 0x84C;         // FLAG_SYS_EXDATA_ENABLE: Mystery Event
    internal const int FlagMysteryEventEnableE = 0x8AC;    // FLAG_SYS_MYSTERY_EVENT_ENABLE (Japanese Emerald)
    internal const int FlagMysteryGiftEnableE = 0x8DB;     // FLAG_SYS_MYSTERY_GIFT_ENABLE
    internal const int FlagMysteryGiftEnableFRLG = 0x839;  // FLAG_SYS_MYSTERY_GIFT_ENABLED

    internal const int FlagBadge05RS = 0x80B;              // FLAG_BADGE05_GET: Norman's Balance Badge

    /// <summary>
    /// Why this save can't receive the event yet: the game's own menu for it must be unlocked in-game first.
    /// Ruby/Sapphire: Mystery Event, from the Mystery Event Club's profile after beating Norman
    /// (mystery_event_club.inc); FireRed/LeafGreen/Emerald: Mystery Gift, and Japanese Emerald: Mystery Event,
    /// from the Poké Mart questionnaire (questionnaire.inc).
    /// </summary>
    public static string? MenuLocked(SAV3 sav, Gen3EventKind kind)
    {
        const string eventPhrase = "\"MYSTERY EVENT IS EXCITING\"";
        const string giftPhrase = "\"LINK TOGETHER WITH ALL\"";
        var game = Names.Game(sav);
        switch (sav, kind)
        {
            case (SAV3RS, _):
                if (sav.GetEventFlag(FlagExdataEnableRS) && sav.GetEventFlag(FlagBadge05RS))
                    return null;
                return $"Unlock Mystery Event in {game} first: after beating Norman, give {eventPhrase} as your profile to the man in the Petalburg City Pokémon Center.";
            case (SAV3E, Gen3EventKind.MysteryEvent) when !sav.Japanese:
                return "Non-Japanese Emerald has no Mystery Event, so this event can't be received.";
            case (SAV3E, Gen3EventKind.MysteryEvent):
                return sav.GetEventFlag(FlagMysteryEventEnableE) ? null
                    : $"Unlock Mystery Event in {game} first: fill in the questionnaire in a Poké Mart with {eventPhrase}.";
            default:
                return sav.GetEventFlag(sav is SAV3E ? FlagMysteryGiftEnableE : FlagMysteryGiftEnableFRLG) ? null
                    : $"Unlock Mystery Gift in {game} first: fill in the questionnaire in a Poké Mart with {giftPhrase}.";
        }
    }

    // pokeemerald include/constants/flags.h and items.h
    private const int FlagSysGameClearE = 0x864;             // FLAG_SYS_GAME_CLEAR
    private const int FlagEnableShipSouthernIslandE = 0x8B3; // FLAG_ENABLE_SHIP_SOUTHERN_ISLAND
    public const ushort ItemEonTicket = 275;

    /// <summary>
    /// Gives Emerald the Eon Ticket the way Record Mixing with a Ruby/Sapphire holding it did, which is how
    /// Emerald players got it outside Japan: the ticket goes into Key Items (unless the bag or PC already has
    /// one) and the Lilycove ferry to Southern Island is enabled (ReceiveGiftItem in record_mixing.c).
    /// </summary>
    public static OpResult GiveEonTicketByRecordMixing(SAV3E sav)
    {
        var bag = sav.Inventory;
        var keyItems = bag.GetPouch(InventoryType.KeyItems);
        var pc = bag.Pouches.FirstOrDefault(p => p.Type == InventoryType.PCItems);
        bool had = keyItems.HasItem(ItemEonTicket) || pc?.HasItem(ItemEonTicket) == true;
        if (!had)
        {
            if (keyItems.GiveItem(bag, ItemEonTicket, 1) <= 0 || !keyItems.HasItem(ItemEonTicket))
                return OpResult.Fail("The Key Items pocket is full.");
            bag.CopyTo(sav);
        }
        sav.SetEventFlag(FlagEnableShipSouthernIslandE, true);

        var message = had
            ? "You already have the Eon Ticket; the ferry to Southern Island is now enabled."
            : "The Eon Ticket was added to your Key Items, as if received through Record Mixing.";
        message += "\nShow it to the sailor at the Lilycove City harbor to sail to Southern Island.";
        if (!sav.GetEventFlag(FlagSysGameClearE))
            message += "\nThe ferry only runs once you've entered the Hall of Fame.";
        return OpResult.Success(message);
    }

    /// <summary>Describes what event data the save currently holds.</summary>
    public static string Status(SAV3 sav)
    {
        var lines = new List<string>();
        var large = sav.Large;
        if (sav is SAV3E or SAV3FRLG)
        {
            int newsOffset = sav is SAV3E ? 0x322C : 0x3120;
            int cardOffset = newsOffset + (sav.Japanese ? WN3SizeJP : WN3Size);
            int cardSize = sav.Japanese ? CardSizeJP : CardSize;
            var (mgOffset, mgMask) = sav is SAV3E ? (Sector2 + 0x40B, 0x08) : (Sector2 + 0x67, 0x02);
            lines.Add($"Mystery Gift unlocked: {((large[mgOffset] & mgMask) != 0 ? "yes" : "no")}");

            var card = large.Slice(cardOffset, cardSize);
            lines.Add(IsCrcValid(card) && ReadUInt16(card, 4) != 0
                ? $"Wonder Card: {Text(card.Slice(0xE, sav.Japanese ? 18 : 40), sav.Japanese)}"
                : "Wonder Card: none");
            var news = large.Slice(newsOffset, sav.Japanese ? WN3SizeJP : WN3Size);
            lines.Add(IsCrcValid(news) && ReadUInt16(news, 4) != 0
                ? $"Wonder News: {Text(news.Slice(8, sav.Japanese ? 20 : 40), sav.Japanese)}"
                : "Wonder News: none");
        }
        if (sav is SAV3RS)
            lines.Add($"Mystery Event unlocked: {(sav.GetEventFlag(FlagExdataEnableRS) ? "yes" : "no")}");
        lines.Add($"e-Reader Berry: {(sav.IsEBerryEngima ? "none (Enigma)" : sav.EBerryName)}");
        lines.Add($"e-Card Trainer: {(sav.SmallBlock.EReaderTrainer.ContainsAnyExcept((byte)0, (byte)0xFF) ? Text(sav.SmallBlock.EReaderTrainer.Slice(4, sav.Japanese ? 5 : 7), sav.Japanese) : "none")}");

        int scriptOffset = sav switch { SAV3E => 0x3728, SAV3FRLG => 0x361C, _ => 0x3690 };
        var script = large.Slice(scriptOffset, ScriptSize);
        bool hasScript = script[4] == RamScriptMagic && BinaryPrimitives.ReadUInt32LittleEndian(script) != 0;
        lines.Add($"Event script: {(hasScript ? "present" : "none")}");
        return string.Join('\n', lines);
    }

    private static string Text(ReadOnlySpan<byte> data, bool japanese)
    {
        try { return StringConverter3.GetString(data, japanese).Trim(); }
        catch { return "?"; }
    }

    private static ushort ReadUInt16(ReadOnlySpan<byte> data, int offset)
        => BinaryPrimitives.ReadUInt16LittleEndian(data[offset..]);

    /// <summary>u32 CRC field followed by CRC-protected data, as used by Wonder Cards/News and FR/LG/E scripts.</summary>
    private static bool IsCrcValid(ReadOnlySpan<byte> block)
        => BinaryPrimitives.ReadUInt32LittleEndian(block) == Crc16(block[4..]);

    private static readonly ushort[] CrcTable = BuildCrcTable();

    private static ushort[] BuildCrcTable()
    {
        var table = new ushort[256];
        for (int i = 0; i < 256; i++)
        {
            int crc = i;
            for (int bit = 0; bit < 8; bit++)
                crc = (crc & 1) != 0 ? (crc >> 1) ^ 0x8408 : crc >> 1;
            table[i] = (ushort)crc;
        }
        return table;
    }

    /// <summary>CalcCRC16WithTable from the Gen 3 games (CRC-16/CCITT, reflected, seed 0x1121, inverted).</summary>
    public static ushort Crc16(ReadOnlySpan<byte> data)
    {
        ushort crc = 0x1121;
        foreach (var b in data)
            crc = (ushort)((crc >> 8) ^ CrcTable[(byte)(crc ^ b)]);
        return (ushort)~crc;
    }

    /// <summary>Ruby/Sapphire RAM script checksum: sum of the script bytes.</summary>
    public static uint ByteSum(ReadOnlySpan<byte> data)
    {
        uint sum = 0;
        foreach (var b in data)
            sum += b;
        return sum;
    }

    /// <summary>e-Card trainer checksum: sum of little-endian u32 words.</summary>
    public static uint WordSum(ReadOnlySpan<byte> data)
    {
        uint sum = 0;
        for (int i = 0; i + 4 <= data.Length; i += 4)
            sum += BinaryPrimitives.ReadUInt32LittleEndian(data[i..]);
        return sum;
    }

    /// <summary>R/S Enigma Berry checksum: byte sum, skipping the two description pointers at 0xC-0x13.</summary>
    public static uint BerryChecksum(ReadOnlySpan<byte> data)
    {
        uint sum = 0;
        for (int i = 0; i < data.Length; i++)
        {
            if (i is >= 0xC and < 0x14)
                continue;
            sum += data[i];
        }
        return sum;
    }
}
