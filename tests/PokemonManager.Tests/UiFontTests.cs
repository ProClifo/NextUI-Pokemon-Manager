using System.Buffers.Binary;
using PKHeX.Core;
using PokemonManager.Core;
using Xunit;

namespace PokemonManager.Tests;

public sealed class UiFontTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("uifont").FullName;
    public void Dispose() => Directory.Delete(_dir, true);

    /// <summary>A TrueType file with only a cmap table (format 4) covering the given ranges.</summary>
    internal static string WriteFont(string dir, string name, params (char First, char Last)[] ranges)
    {
        int segments = ranges.Length + 1; // plus the 0xFFFF terminator
        var sub = new byte[16 + 8 * segments];
        BinaryPrimitives.WriteUInt16BigEndian(sub, 4);
        BinaryPrimitives.WriteUInt16BigEndian(sub.AsSpan(2), (ushort)sub.Length);
        BinaryPrimitives.WriteUInt16BigEndian(sub.AsSpan(6), (ushort)(segments * 2));
        for (int i = 0; i < segments; i++)
        {
            var (first, last) = i < ranges.Length ? ranges[i] : ('￿', '￿');
            BinaryPrimitives.WriteUInt16BigEndian(sub.AsSpan(14 + 2 * i), last);
            BinaryPrimitives.WriteUInt16BigEndian(sub.AsSpan(16 + 2 * segments + 2 * i), first);
            BinaryPrimitives.WriteInt16BigEndian(sub.AsSpan(16 + 4 * segments + 2 * i), 1); // idDelta: glyph = code + 1, never 0
        }
        var cmap = new byte[12 + sub.Length];
        BinaryPrimitives.WriteUInt16BigEndian(cmap.AsSpan(2), 1);
        BinaryPrimitives.WriteUInt16BigEndian(cmap.AsSpan(4), 3); // Windows
        BinaryPrimitives.WriteUInt16BigEndian(cmap.AsSpan(6), 1); // Unicode BMP
        BinaryPrimitives.WriteUInt32BigEndian(cmap.AsSpan(8), 12);
        sub.CopyTo(cmap, 12);

        var font = new byte[12 + 16 + cmap.Length];
        BinaryPrimitives.WriteUInt32BigEndian(font, 0x00010000);
        BinaryPrimitives.WriteUInt16BigEndian(font.AsSpan(4), 1);
        "cmap"u8.CopyTo(font.AsSpan(12));
        BinaryPrimitives.WriteUInt32BigEndian(font.AsSpan(12 + 8), 28);
        BinaryPrimitives.WriteUInt32BigEndian(font.AsSpan(12 + 12), (uint)cmap.Length);
        cmap.CopyTo(font, 28);
        var path = Path.Combine(dir, name);
        File.WriteAllBytes(path, font);
        return path;
    }

    [Fact]
    public void CoverageComesFromTheFontsCmap()
    {
        var og = new UiFont(WriteFont(_dir, "font2.ttf", (' ', '~'), ('é', 'é')));
        Assert.True(og.Covers(["Inject Mew (Lv. 10) into Emerald?", "Pokémon", null]));
        Assert.False(og.Covers(["ニドラン♂"]));
        Assert.False(og.Covers(["NIDORAN♂"]));
    }

    [Fact]
    public void NextAndOgAreNextUisBuiltInFonts()
    {
        var res = Directory.CreateDirectory(Path.Combine(_dir, ".system", "res")).FullName;
        Assert.Null(UiFont.Next(_dir));
        WriteFont(res, "font1.ttf", (' ', '~'));
        WriteFont(res, "font2.ttf", (' ', '~'));
        Assert.EndsWith("font1.ttf", UiFont.Next(_dir)!.Path);
        Assert.EndsWith("font2.ttf", UiFont.OG(_dir)!.Path);
    }

    [Theory]
    [InlineData("tg5040", "brick", 3)]
    [InlineData("tg5040", "smartpro", 2)]
    [InlineData("h700", "rgsp", 2)]
    [InlineData(null, null, 2)]
    public void UiScaleMatchesNextUi(string? platform, string? device, int scale)
        => Assert.Equal(scale, UiFont.Scale(platform, device));
}
