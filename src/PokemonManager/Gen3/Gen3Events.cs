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
/// Offsets and procedure follow suloku's Gen III Mystery Gift Tool (wc-tool), cross-checked against
/// PKHeX's SAV3 block layout and the pokeemerald/pokeruby decompilations.
/// </summary>
public static class Gen3Events
{
    // Sizes of the on-disk formats.
    public const int CardSize = 0x150;           // u32 CRC + 0x14C card
    public const int CardSizeJP = 0xA8;          // u32 CRC + 0xA4 card
    public const int CardMetadataSize = 0x28;    // u32 CRC + 0x24 metadata (stats/stamps/icon)
    public const int ScriptSize = 0x3EC;         // u32 CRC + 1000-byte RAM script
    public const int WC3Size = CardSize + CardMetadataSize + 0x28 + ScriptSize;       // 1420
    public const int WC3SizeJP = CardSizeJP + CardMetadataSize + 0x28 + ScriptSize;   // 1252
    public const int WN3Size = 0x1C0;            // 448
    public const int WN3SizeJP = 0xE4;           // 228
    public const int ME3Size = ScriptSize + 8;   // 1012: RAM script + record-mixing gift item
    public const int ECTSize = 188;
    public const int ECBSize = 0x530;

    private const byte RamScriptMagic = 0x33;

    // Offsets within the "large" block (save sectors 1-4 concatenated, as exposed by SAV3.Large).
    private const int Sector2 = 0xF80;
    private const int Sector4 = 3 * 0xF80;

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
            (".me3", ME3Size) => new() { Path = path, Kind = Gen3EventKind.MysteryEvent, Data = data },
            (".ect", ECTSize) => new() { Path = path, Kind = Gen3EventKind.ECardTrainer, Data = data },
            (".ecb", ECBSize) => new() { Path = path, Kind = Gen3EventKind.ECardBerry, Data = data },
            _ => null,
        };
    }

    /// <summary>Whether <paramref name="file"/> can go into this save at all (used to filter lists).</summary>
    public static bool IsApplicable(SAV3 sav, Gen3EventFile file) => file.Kind switch
    {
        Gen3EventKind.WonderCard or Gen3EventKind.WonderNews => sav is SAV3E or SAV3FRLG && file.Japanese == sav.Japanese,
        Gen3EventKind.MysteryEvent => sav is SAV3RS or SAV3E,
        Gen3EventKind.ECardTrainer => true,
        Gen3EventKind.ECardBerry => sav is SAV3RS,
        _ => false,
    };

    /// <summary>
    /// Validates <paramref name="file"/> and writes it into the save in memory.
    /// </summary>
    public static OpResult Inject(SAV3 sav, Gen3EventFile file) => file.Kind switch
    {
        Gen3EventKind.WonderCard => InjectWonderCard(sav, file),
        Gen3EventKind.WonderNews => InjectWonderNews(sav, file),
        Gen3EventKind.MysteryEvent => InjectMysteryEvent(sav, file),
        Gen3EventKind.ECardTrainer => InjectECardTrainer(sav, file),
        Gen3EventKind.ECardBerry => InjectECardBerry(sav, file),
        _ => OpResult.Fail("Unknown event type."),
    };

    private static OpResult InjectWonderCard(SAV3 sav, Gen3EventFile file)
    {
        if (sav is not (SAV3E or SAV3FRLG))
            return OpResult.Fail("Wonder Cards only work in FireRed, LeafGreen and Emerald. Ruby/Sapphire use Mystery Events (.me3).");
        if (file.Japanese != sav.Japanese)
            return OpResult.Fail(file.Japanese
                ? "This is a Japanese Wonder Card; it won't work in a non-Japanese game."
                : "This is an international Wonder Card; it won't work in a Japanese game.");

        var data = file.Data.AsSpan();
        int cardSize = file.Japanese ? CardSizeJP : CardSize;
        var card = data[..cardSize];
        var meta = data.Slice(cardSize, CardMetadataSize);
        var script = data[^ScriptSize..];

        if (!IsCrcValid(card))
            return OpResult.Fail("The Wonder Card's checksum is wrong; the file is damaged.");
        if (ReadUInt16(card, 4) == 0)
            return OpResult.Fail("This Wonder Card has no event flag set, so the game would ignore it.");
        if (!IsCrcValid(script) || script[4] != RamScriptMagic)
            return OpResult.Fail("The Wonder Card's script is damaged or missing.");

        var large = sav.Large;
        int newsOffset = sav is SAV3E ? 0x322C : 0x3120;
        int cardOffset = newsOffset + (file.Japanese ? WN3SizeJP : WN3Size);
        int metaOffset = cardOffset + cardSize;
        int scriptOffset = sav is SAV3E ? 0x3728 : 0x361C;

        // Mirror the game's own SaveWonderCard(): copy the card, reset its metadata, keep the icon species.
        card.CopyTo(large[cardOffset..]);
        var saveMeta = large.Slice(metaOffset, CardMetadataSize);
        saveMeta.Clear();
        meta.Slice(0xA, 2).CopyTo(saveMeta[0xA..]);
        script.CopyTo(large[scriptOffset..]);

        bool enabled = EnableMysteryGift(sav);
        var where = "Talk to the delivery man in green on the 2nd floor of any Pokémon Center.";
        var msg = $"Wonder Card injected.\n{where}";
        if (enabled)
            msg += "\nMystery Gift was also unlocked in the main menu.";
        msg += "\nNote: a game holds one card/event script at a time; this replaced any previous one.";
        return OpResult.Success(msg);
    }

    private static OpResult InjectWonderNews(SAV3 sav, Gen3EventFile file)
    {
        if (sav is not (SAV3E or SAV3FRLG))
            return OpResult.Fail("Wonder News only works in FireRed, LeafGreen and Emerald.");
        if (file.Japanese != sav.Japanese)
            return OpResult.Fail("This Wonder News is for a different language/region than this save.");
        var data = file.Data.AsSpan();
        if (!IsCrcValid(data))
            return OpResult.Fail("The Wonder News checksum is wrong; the file is damaged.");

        int newsOffset = sav is SAV3E ? 0x322C : 0x3120;
        data.CopyTo(sav.Large[newsOffset..]);
        bool enabled = EnableMysteryGift(sav);
        var msg = "Wonder News injected. Read it from Mystery Gift > Wonder News on the title menu.";
        if (enabled)
            msg += "\nMystery Gift was also unlocked in the main menu.";
        return OpResult.Success(msg);
    }

    private static OpResult InjectMysteryEvent(SAV3 sav, Gen3EventFile file)
    {
        if (sav is SAV3FRLG)
            return OpResult.Fail("FireRed/LeafGreen don't have Mystery Events. Use a Wonder Card (.wc3) instead.");
        if (sav is not (SAV3RS or SAV3E))
            return OpResult.Fail("Mystery Events only work in Ruby, Sapphire and Emerald.");

        var data = file.Data.AsSpan();
        var script = data[..ScriptSize];
        uint stored = BinaryPrimitives.ReadUInt32LittleEndian(script);
        bool isRS = stored == ByteSum(script[4..]);
        bool isE = stored == Crc16(script[4..]);
        if (!isRS && !isE)
            return OpResult.Fail("The Mystery Event's checksum is wrong; the file is damaged.");
        if (sav is SAV3RS && !isRS)
            return OpResult.Fail("This Mystery Event is for Emerald, not Ruby/Sapphire.");
        if (sav is SAV3E && !isE)
            return OpResult.Fail("This Mystery Event is for Ruby/Sapphire, not Emerald.");

        int offset = sav is SAV3E ? 0x3728 : 0x3690;
        data.CopyTo(sav.Large[offset..]);

        string msg = "Mystery Event injected.";
        if (sav is SAV3RS)
        {
            SetBit(sav.Large, Sector2 + 0x3A9, 0x10); // FLAG_SYS_MYSTERY_EVENT_ENABLE
            msg += "\nFollow the event's original instructions (for the Eon Ticket: visit your dad at the Petalburg Gym).";
        }
        else if (sav.Japanese)
        {
            SetBit(sav.Large, Sector2 + 0x405, 0x10); // FLAG_SYS_MYSTERY_EVENT_ENABLE (JPN Emerald only)
            msg += "\nThe event is now active in your game.";
        }
        else
        {
            // Non-Japanese Emerald deletes the save if the Mystery Event flag is set, so never touch it.
            msg += "\nMystery Events were cut from non-Japanese Emerald; the script was written but the event may not trigger.";
        }
        msg += "\nNote: this replaced any Wonder Card script already in the save.";
        return OpResult.Success(msg);
    }

    private static OpResult InjectECardTrainer(SAV3 sav, Gen3EventFile file)
    {
        var data = file.Data.AsSpan();
        uint stored = BinaryPrimitives.ReadUInt32LittleEndian(data[^4..]);
        if (stored != WordSum(data[..^4]))
            return OpResult.Fail("The e-Card Trainer's checksum is wrong; the file is damaged.");

        int offset = sav switch
        {
            SAV3RS => 0x498,
            SAV3E => 0xBEC,
            SAV3FRLG => 0x4A0,
            _ => -1,
        };
        if (offset < 0)
            return OpResult.Fail("e-Card Trainers only work in Ruby, Sapphire, Emerald, FireRed and LeafGreen.");
        data.CopyTo(sav.Small[offset..]);

        return OpResult.Success("e-Card Trainer injected. It replaces any e-Reader trainer already stored in the save.");
    }

    private static OpResult InjectECardBerry(SAV3 sav, Gen3EventFile file)
    {
        if (sav is not SAV3RS)
            return OpResult.Fail("e-Reader Berries (this format) only work in Ruby and Sapphire.");
        var data = file.Data.AsSpan();
        uint stored = BinaryPrimitives.ReadUInt32LittleEndian(data[^4..]);
        if (stored != BerryChecksum(data[..^4]))
            return OpResult.Fail("The e-Reader Berry's checksum is wrong; the file is damaged.");

        data.CopyTo(sav.Large[0x3160..]);
        sav.Large[Sector2 + 0x41A] = 0x01; // VAR_ENIGMA_BERRY_AVAILABLE
        return OpResult.Success("e-Reader Berry injected. It replaces the Enigma Berry data in the save, as scanning the e-Card would.");
    }

    /// <summary>Sets FLAG_SYS_MYSTERY_GIFT_ENABLE on FR/LG/E. Returns true if it was previously off.</summary>
    private static bool EnableMysteryGift(SAV3 sav)
    {
        var (offset, mask) = sav switch
        {
            SAV3E => (Sector2 + 0x40B, (byte)0x08),
            SAV3FRLG => (Sector2 + 0x67, (byte)0x02),
            _ => (-1, (byte)0),
        };
        if (offset < 0)
            return false;
        bool was = (sav.Large[offset] & mask) != 0;
        SetBit(sav.Large, offset, mask);
        return !was;
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
        {
            lines.Add($"Mystery Event unlocked: {((large[Sector2 + 0x3A9] & 0x10) != 0 ? "yes" : "no")}");
            lines.Add($"e-Reader Berry: {(sav.IsEBerryEngima ? "none (Enigma)" : sav.EBerryName)}");
        }

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

    private static void SetBit(Span<byte> data, int offset, byte mask) => data[offset] |= mask;

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
