using System.IO.Compression;
using PKHeX.Core;
using PokemonManager.Core;
using Xunit;

namespace PokemonManager.Tests;

/// <summary>
/// The tests can't ship Nintendo ROMs, so they forge files whose CRC32 equals a retail dump's
/// (CRC32 is linear: four chosen bytes reach any value).
/// </summary>
public sealed class OfficialRomTests : IDisposable
{
    private const uint EmeraldUsa = 0x1F1C08FB;   // Pokemon - Emerald Version (USA, Europe)
    private const uint FireRedUsa = 0xDD88761C;   // Pokemon - FireRed Version (USA, Europe)
    private const uint DiamondUsa = 0x84427823;   // Pokemon - Diamond Version (USA) (Rev 5), 64 MB

    private readonly TestSaves _saves = new();
    private readonly string _roms;
    private readonly string _savesDir;
    private readonly SaveFile _sav;

    public OfficialRomTests()
    {
        _roms = Directory.CreateDirectory(Path.Combine(_saves.Dir, "Roms", "Game Boy Advance (GBA)")).Parent!.FullName;
        _savesDir = Directory.CreateDirectory(Path.Combine(_saves.Dir, "Saves", "GBA")).FullName;
        _sav = _saves.Create(GameVersion.E, "template.sav").Sav;
    }

    public void Dispose() => _saves.Dispose();

    private SaveEntry Save(string name) => new(Path.Combine(_savesDir, name), _sav);

    private string Rom(string name, byte[] data, string folder = "Game Boy Advance (GBA)")
    {
        var path = Path.Combine(_roms, folder, name);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, data);
        return path;
    }

    private (List<SaveEntry> Shown, List<OfficialRomFilter.Hidden> Hidden) Filter(params SaveEntry[] saves)
        => OfficialRomFilter.Apply(saves, [_roms], Path.Combine(_saves.Dir, "cache.txt"));

    [Fact]
    public void TableCoversTheMainSeries()
    {
        Assert.True(VanillaRoms.Count > 150);
        Assert.True(VanillaRoms.IsOfficialCrc(EmeraldUsa, out var name));
        Assert.Equal("Pokemon - Emerald Version (USA, Europe)", name);
        Assert.True(VanillaRoms.IsOfficialCrc(0x9F7FDD53, out _)); // Red (USA, Europe)
        Assert.True(VanillaRoms.IsOfficialCrc(0x524478D4, out _)); // Pocket Monsters Kin (Japan)
        Assert.False(VanillaRoms.IsOfficialCrc(0x6BF7E4A6, out _)); // Pokemon de Panepon: spin-off, excluded
    }

    [Fact]
    public void OfficialRomIsShown()
    {
        Rom("Pokemon Emerald.gba", Crc32Forge.Make(4096, EmeraldUsa));
        var (shown, hidden) = Filter(Save("Pokemon Emerald.gba.sav")); // NextUI default naming
        Assert.Single(shown);
        Assert.Empty(hidden);
    }

    [Fact]
    public void AlternateSaveNamingIsMatched()
    {
        Rom("Pokemon FireRed.gba", Crc32Forge.Make(4096, FireRedUsa));
        Assert.Single(Filter(Save("Pokemon FireRed.sav")).Shown);  // "Generic" save format
        Assert.Single(Filter(Save("Pokemon FireRed.srm")).Shown);  // RetroArch-style .srm
    }

    [Fact]
    public void RomHackIsHidden()
    {
        Rom("Pokemon Radical Red.gba", Crc32Forge.Make(4096, 0x12345678));
        var (shown, hidden) = Filter(Save("Pokemon Radical Red.gba.sav"));
        Assert.Empty(shown);
        Assert.Equal(RomStatus.Unofficial, Assert.Single(hidden).Check.Status);
    }

    [Fact]
    public void SaveWithoutRomIsHidden()
    {
        var (_, hidden) = Filter(Save("Mystery.sav"));
        Assert.Equal(RomStatus.RomNotFound, Assert.Single(hidden).Check.Status);
    }

    [Fact]
    public void ZippedRomsAreCheckedWithoutUnzipping()
    {
        var zipPath = Path.Combine(_roms, "Game Boy Advance (GBA)", "Emerald.zip");
        using (var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        {
            using var stream = zip.CreateEntry("Pokemon - Emerald Version (USA, Europe).gba").Open();
            stream.Write(Crc32Forge.Make(4096, EmeraldUsa));
        }
        Assert.Single(Filter(Save("Emerald.zip.sav")).Shown);                                   // named after the zip
        Assert.Single(Filter(Save("Pokemon - Emerald Version (USA, Europe).gba.sav")).Shown);  // named after the ROM inside
    }

    [Fact]
    public void TrimmedDsRomCountsAsOfficial()
    {
        // A trimmed dump is the retail ROM minus its trailing 0xFF padding.
        var trimmed = Crc32Forge.MakeTrimmed(1 << 20, DiamondUsa, paddedSize: 64L << 20);
        Rom("Pokemon Diamond.nds", trimmed, "Nintendo DS (NDS)");
        Assert.Single(Filter(Save("Pokemon Diamond.nds.sav")).Shown);
    }

    [Fact]
    public void ResultsAreCached()
    {
        var rom = Rom("Pokemon Emerald.gba", Crc32Forge.Make(4096, EmeraldUsa));
        Filter(Save("Pokemon Emerald.gba.sav"));
        var cache = File.ReadAllText(Path.Combine(_saves.Dir, "cache.txt"));
        Assert.Contains(rom, cache);
        Assert.Contains("1F1C08FB", cache);
    }
}

internal static class Crc32Forge
{
    private static readonly uint[] Table = BuildTable();
    private static readonly byte[] Reverse = BuildReverse();

    private static uint[] BuildTable()
    {
        var t = new uint[256];
        for (uint i = 0; i < 256; i++)
        {
            uint c = i;
            for (int k = 0; k < 8; k++)
                c = (c & 1) != 0 ? (c >> 1) ^ 0xEDB88320 : c >> 1;
            t[i] = c;
        }
        return t;
    }

    private static byte[] BuildReverse()
    {
        var r = new byte[256];
        for (int i = 0; i < 256; i++)
            r[Table[i] >> 24] = (byte)i;
        return r;
    }

    private static uint Register(ReadOnlySpan<byte> data)
    {
        uint reg = 0xFFFFFFFF;
        foreach (var b in data)
            reg = (reg >> 8) ^ Table[(byte)(reg ^ b)];
        return reg;
    }

    /// <summary>Undoes one CRC step: the register before <paramref name="b"/> was fed in.</summary>
    private static uint Unstep(uint reg, byte b)
    {
        byte idx = Reverse[reg >> 24];
        return ((reg ^ Table[idx]) << 8) | (byte)(idx ^ b);
    }

    /// <summary>Random bytes whose final 4 bytes make the register reach <paramref name="wantedRegister"/>.</summary>
    private static byte[] Patch(int length, uint wantedRegister)
    {
        var data = new byte[length];
        new Random(length).NextBytes(data.AsSpan(0, length - 4));
        uint reg = wantedRegister;
        for (int i = 0; i < 4; i++)
            reg = Unstep(reg, 0);
        uint patch = reg ^ Register(data.AsSpan(0, length - 4));
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(length - 4), patch);
        return data;
    }

    public static byte[] Make(int length, uint crc) => Patch(length, ~crc);

    /// <summary>Data that has CRC <paramref name="crc"/> once padded with 0xFF to <paramref name="paddedSize"/>.</summary>
    public static byte[] MakeTrimmed(int length, uint crc, long paddedSize)
    {
        uint reg = ~crc;
        for (long i = length; i < paddedSize; i++)
            reg = Unstep(reg, 0xFF);
        return Patch(length, reg);
    }
}
