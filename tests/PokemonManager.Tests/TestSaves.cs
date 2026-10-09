using System.Buffers.Binary;
using PKHeX.Core;
using PokemonManager.Core;

namespace PokemonManager.Tests;

/// <summary>
/// Creates saves with PKHeX and writes them to disk. Gen 3 saves are laid out sector-by-sector like a real
/// 128 KB cartridge dump so they go through the same detection and checksum code as a user's file.
/// </summary>
public sealed class TestSaves : IDisposable
{
    public string Dir { get; } = Directory.CreateTempSubdirectory("pkmgr-test-").FullName;
    public SaveLibrary Library { get; }

    public TestSaves() => Library = new SaveLibrary(Path.Combine(Dir, "Backups"));

    public SaveEntry Create(GameVersion version, string name, string trainer = "ASH", LanguageID language = LanguageID.English)
    {
        var sav = BlankSaveFile.Get(version, trainer, language);
        var path = Path.Combine(Dir, name);
        if (sav is SAV3 sav3)
        {
            File.WriteAllBytes(path, BuildGen3Raw(sav3, version));
            var loaded = Reload(path);
            if (loaded.Sav.GetType() != sav.GetType())
                throw new InvalidOperationException($"Detected {loaded.Sav.GetType().Name}, expected {sav.GetType().Name}");
            return loaded;
        }

        if (sav is SAV5 and IMysteryGiftStorageProvider { MysteryGiftStorage: MysteryBlock5 album })
        {
            // A blank PKHeX save has an all-zero album, which isn't what the encrypted at-rest data looks like.
            for (int i = 0; i < album.GiftCountMax; i++)
                album.SetMysteryGift(i, new PGF());
            album.EndAccess();
        }

        if (sav.Data.Length != 0)
            File.WriteAllBytes(path, sav.Write().ToArray());
        return new SaveEntry(path, sav);
    }

    /// <summary>Writes the save (with backup) and returns a copy re-read from the written bytes.</summary>
    public SaveEntry Roundtrip(SaveEntry entry)
    {
        if (entry.Sav.Data.Length == 0)
            return entry; // PKHeX's blank Gen 4 saves have no file image to write
        Library.Write(entry);
        if (entry.Sav is SAV3)
            return Reload(entry.Path);
        // Blank Gen 1/2/4+ saves lack the footers PKHeX's file detection looks for, so re-parse in memory.
        return new SaveEntry(entry.Path, entry.Sav.Clone());
    }

    public SaveEntry Reload(string path)
        => SaveLibrary.Load(path) ?? throw new InvalidOperationException($"PKHeX didn't recognize {path}");

    private static byte[] BuildGen3Raw(SAV3 sav, GameVersion version)
    {
        // Game detection keys off small-block offset 0xAC (FR/LG: 1, R/S: 0, E: security key + extra data).
        var small = sav.Small;
        switch (version)
        {
            case GameVersion.FR or GameVersion.LG or GameVersion.FRLG:
                BinaryPrimitives.WriteUInt32LittleEndian(small[0xAC..], 1);
                break;
            case GameVersion.E:
                BinaryPrimitives.WriteUInt32LittleEndian(small[0xAC..], 0x1234_5678);
                small[0xF00] = 1;
                break;
            default:
                BinaryPrimitives.WriteUInt32LittleEndian(small[0xAC..], 0);
                break;
        }

        // The game pads the OT name with 0xFF; PKHeX tells Japanese saves apart by zeroes at 0x6-0x7.
        if (!sav.Japanese)
            small[6..8].Fill(0xFF);

        var raw = new byte[0x20000];
        for (int slot = 0; slot < 2; slot++)
        {
            for (int id = 0; id < 14; id++)
            {
                int ofs = (slot * 0xE000) + (id * 0x1000);
                Chunk(sav, id).CopyTo(raw.AsSpan(ofs));
                var footer = raw.AsSpan(ofs + 0xFF4);
                BinaryPrimitives.WriteInt16LittleEndian(footer, (short)id);
                BinaryPrimitives.WriteUInt32LittleEndian(footer[4..], 0x08012025);
                BinaryPrimitives.WriteUInt32LittleEndian(footer[8..], (uint)(slot + 1));
            }
        }
        return raw;
    }

    private static ReadOnlySpan<byte> Chunk(SAV3 sav, int id) => id switch
    {
        >= 5 => sav.Storage.Slice((id - 5) * SAV3.SIZE_SECTOR_USED, SAV3.SIZE_SECTOR_USED),
        >= 1 => sav.Large.Slice((id - 1) * SAV3.SIZE_SECTOR_USED, SAV3.SIZE_SECTOR_USED),
        _ => sav.Small[..SAV3.SIZE_SECTOR_USED],
    };

    /// <summary>
    /// Marks the save as far enough in the story to trade, receive and transfer anything: the link rooms
    /// open, the National Pokédex, FireRed/LeafGreen's Sapphire delivered and Emerald's Champion title.
    /// </summary>
    public static SaveFile Progress(SaveFile sav)
    {
        switch (sav)
        {
            case SAV1 s1: s1.SetEventFlag(37, true); break;           // EVENT_GOT_POKEDEX
            case SAV2 s2: s2.SetEventFlag(31, true); break;           // EVENT_GAVE_MYSTERY_EGG_TO_ELM
            case SAV3 s3:
                s3.NationalDex = true;
                if (s3 is SAV3FRLG) s3.SetEventFlag(0x844, true);     // FLAG_SYS_CAN_LINK_WITH_RS
                if (s3 is SAV3E) s3.SetEventFlag(0x87F, true);        // FLAG_IS_CHAMPION
                break;
            case SAV4 s4: s4.NationalDex = true; break;
            case SAV5 s5: s5.Zukan.IsNationalDexUnlocked = true; break;
        }
        return sav;
    }

    public static PKM Make(SaveFile sav, Species species, int level = 30, int heldItem = 0)
    {
        var pk = sav.BlankPKM;
        pk.Species = (ushort)species;
        pk.CurrentLevel = (byte)level;
        pk.HeldItem = heldItem;
        pk.OriginalTrainerName = sav.OT;
        pk.TID16 = sav.TID16;
        pk.SID16 = sav.SID16;
        pk.Language = sav.Language;
        pk.Version = sav.Version switch
        {
            GameVersion.FRLG => GameVersion.FR,
            GameVersion.RS => GameVersion.R,
            GameVersion.RBY => GameVersion.RD,
            GameVersion.GSC => GameVersion.C,
            _ => sav.Version,
        };
        pk.SetDefaultNickname();
        pk.RefreshChecksum();
        return pk;
    }

    public void Dispose()
    {
        try { Directory.Delete(Dir, recursive: true); }
        catch (IOException) { }
    }
}
