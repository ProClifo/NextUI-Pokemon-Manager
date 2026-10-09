using System.IO.Compression;
using System.Text;

namespace PokemonManager.Core;

/// <summary>
/// Reads the 4-letter game code from a GBA or DS ROM header (e.g. BPRE = FireRed USA, CPUE = Platinum USA).
/// The first three letters name the game; the fourth is the region, which for these games also fixes
/// the language.
/// </summary>
public static class RomHeader
{
    private const int GbaCodeOffset = 0xAC;
    private const int NdsCodeOffset = 0x0C;

    /// <summary>Game code of a .gba/.nds file, or of the first .gba/.nds inside a .zip.</summary>
    public static string? ReadGameCode(string romPath)
    {
        try
        {
            if (Offset(romPath) is { } offset)
            {
                using var file = File.OpenRead(romPath);
                return Read(file, offset);
            }
            if (romPath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            {
                using var zip = ZipFile.OpenRead(romPath);
                foreach (var entry in zip.Entries)
                {
                    if (Offset(entry.Name) is not { } inner)
                        continue;
                    using var stream = entry.Open();
                    return Read(stream, inner);
                }
            }
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
        {
        }
        return null;
    }

    /// <summary>Language of a region letter: J Japan, E USA, P Europe (English), D/F/I/S, K Korea.</summary>
    public static string? Language(char region) => char.ToUpperInvariant(region) switch
    {
        'J' => GalleryLanguage.Japanese,
        'E' or 'P' => GalleryLanguage.English,
        'D' => GalleryLanguage.German,
        'F' => GalleryLanguage.French,
        'I' => GalleryLanguage.Italian,
        'S' => GalleryLanguage.Spanish,
        'K' => GalleryLanguage.Korean,
        _ => null,
    };

    private static int? Offset(string name)
    {
        if (name.EndsWith(".gba", StringComparison.OrdinalIgnoreCase))
            return GbaCodeOffset;
        if (name.EndsWith(".nds", StringComparison.OrdinalIgnoreCase))
            return NdsCodeOffset;
        return null;
    }

    private static string? Read(Stream stream, int offset)
    {
        var header = new byte[offset + 4];
        int read = 0, n;
        while (read < header.Length && (n = stream.Read(header, read, header.Length - read)) > 0)
            read += n;
        if (read < header.Length)
            return null;
        var code = Encoding.ASCII.GetString(header, offset, 4);
        return code.All(char.IsAsciiLetterOrDigit) ? code : null;
    }
}
