using System.Buffers.Binary;

namespace PokemonManager.Core;

/// <summary>
/// One of NextUI's built-in fonts (in .system/res): "Next" (font1.ttf, Rounded M+) for the main menu and "OG"
/// (font2.ttf, BPreplay) inside a save. The characters a font has are read from its cmap table, so text it can't
/// show (OG has no Japanese, and no ♂/♀) can be drawn in another font instead.
/// </summary>
public sealed class UiFont(string path)
{
    private HashSet<int>? _chars;

    public string Path { get; } = path;

    public static UiFont? Next(string? sdRoot) => Find(sdRoot, "font1.ttf");
    public static UiFont? OG(string? sdRoot) => Find(sdRoot, "font2.ttf");

    private static UiFont? Find(string? sdRoot, string file)
    {
        if (sdRoot is null)
            return null;
        var path = System.IO.Path.Combine(sdRoot, ".system", "res", file);
        return File.Exists(path) ? new UiFont(path) : null;
    }

    /// <summary>The device's UI scale: 3 on the TrimUI Brick, 2 everywhere else NextUI runs.</summary>
    public static int Scale(string? platform, string? device) => platform == "tg5040" && device is "brick" or "brickpro" ? 3 : 2;

    /// <summary>Whether the font has every (non-control) character of the texts.</summary>
    public bool Covers(IEnumerable<string?> texts)
    {
        var chars = _chars ??= ReadCmap(Path);
        foreach (var text in texts)
        {
            foreach (var rune in (text ?? "").EnumerateRunes())
            {
                if (rune.Value >= 0x20 && !chars.Contains(rune.Value))
                    return false;
            }
        }
        return true;
    }

    /// <summary>The code points in a TrueType font's Unicode cmap (formats 4 and 12); empty if it can't be read.</summary>
    internal static HashSet<int> ReadCmap(string path)
    {
        var chars = new HashSet<int>();
        try
        {
            var data = File.ReadAllBytes(path).AsSpan();
            int tables = BinaryPrimitives.ReadUInt16BigEndian(data[4..]);
            int cmap = -1;
            for (int i = 0; i < tables; i++)
            {
                var record = data.Slice(12 + 16 * i, 16);
                if (record[..4].SequenceEqual("cmap"u8))
                    cmap = (int)BinaryPrimitives.ReadUInt32BigEndian(record[8..]);
            }
            if (cmap < 0)
                return chars;
            int subtables = BinaryPrimitives.ReadUInt16BigEndian(data[(cmap + 2)..]);
            for (int i = 0; i < subtables; i++)
            {
                var record = data.Slice(cmap + 4 + 8 * i, 8);
                int platform = BinaryPrimitives.ReadUInt16BigEndian(record);
                if (platform is not (0 or 3))
                    continue; // Unicode or Windows
                var table = data[(cmap + (int)BinaryPrimitives.ReadUInt32BigEndian(record[4..]))..];
                switch (BinaryPrimitives.ReadUInt16BigEndian(table))
                {
                    case 4:
                        // A code is in the font when it maps to a glyph other than 0 (.notdef).
                        int segments = BinaryPrimitives.ReadUInt16BigEndian(table[6..]) / 2;
                        int ends = 14, starts = 16 + 2 * segments, deltas = starts + 2 * segments, offsets = deltas + 2 * segments;
                        for (int s = 0; s < segments; s++)
                        {
                            int end = BinaryPrimitives.ReadUInt16BigEndian(table[(ends + 2 * s)..]);
                            int start = BinaryPrimitives.ReadUInt16BigEndian(table[(starts + 2 * s)..]);
                            int delta = BinaryPrimitives.ReadInt16BigEndian(table[(deltas + 2 * s)..]);
                            int rangeOffset = BinaryPrimitives.ReadUInt16BigEndian(table[(offsets + 2 * s)..]);
                            for (int c = start; c <= end && c != 0xFFFF; c++)
                            {
                                int glyph = rangeOffset == 0
                                    ? (c + delta) & 0xFFFF
                                    : BinaryPrimitives.ReadUInt16BigEndian(table[(offsets + 2 * s + rangeOffset + 2 * (c - start))..]) is var g && g != 0
                                        ? (g + delta) & 0xFFFF : 0;
                                if (glyph != 0)
                                    chars.Add(c);
                            }
                        }
                        break;
                    case 12:
                        uint groups = BinaryPrimitives.ReadUInt32BigEndian(table[12..]);
                        for (int g = 0; g < groups; g++)
                        {
                            var group = table.Slice(16 + 12 * g, 12);
                            uint first = BinaryPrimitives.ReadUInt32BigEndian(group), last = BinaryPrimitives.ReadUInt32BigEndian(group[4..]);
                            uint glyph = BinaryPrimitives.ReadUInt32BigEndian(group[8..]);
                            for (uint c = first; c <= last; c++)
                            {
                                if (glyph + (c - first) != 0)
                                    chars.Add((int)c);
                            }
                        }
                        break;
                }
            }
        }
        catch (Exception ex) when (ex is IOException or ArgumentOutOfRangeException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine($"Couldn't read {path}: {ex.Message}");
        }
        return chars;
    }
}
