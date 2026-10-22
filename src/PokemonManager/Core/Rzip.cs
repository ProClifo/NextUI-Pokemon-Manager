using System.Buffers.Binary;
using System.IO.Compression;

namespace PokemonManager.Core;

/// <summary>
/// RetroArch's compressed saves ("SaveRAM Compression"): "#RZIPv\x01#", the chunk size (uint32) and the whole size
/// (uint64), then each chunk as its compressed size (uint32) and zlib data; all little-endian (libretro-common's
/// rzip_stream). RetroArch reads compressed and plain saves alike.
/// </summary>
public static class Rzip
{
    private static ReadOnlySpan<byte> Magic => "#RZIPv"u8;
    private const int HeaderSize = 20;
    private const int ChunkSize = 128 * 1024; // RetroArch's own

    public static bool IsCompressed(ReadOnlySpan<byte> data)
        => data.Length >= HeaderSize && data.StartsWith(Magic) && data[7] == (byte)'#';

    /// <summary>The save inside, or null when the file is damaged.</summary>
    public static byte[]? Decompress(ReadOnlySpan<byte> data)
    {
        if (!IsCompressed(data))
            return null;
        ulong total = BinaryPrimitives.ReadUInt64LittleEndian(data[12..]);
        if (total > 64 * 1024 * 1024)
            return null;
        var result = new byte[total];
        int written = 0, offset = HeaderSize;
        try
        {
            while (offset + 4 <= data.Length && written < result.Length)
            {
                int size = (int)BinaryPrimitives.ReadUInt32LittleEndian(data[offset..]);
                offset += 4;
                if (size <= 0 || offset + size > data.Length)
                    return null;
                using var zlib = new ZLibStream(new MemoryStream(data.Slice(offset, size).ToArray()), CompressionMode.Decompress);
                int n;
                while (written < result.Length && (n = zlib.Read(result, written, result.Length - written)) > 0)
                    written += n;
                offset += size;
            }
        }
        catch (InvalidDataException)
        {
            return null;
        }
        return written == result.Length ? result : null;
    }

    public static byte[] Compress(ReadOnlySpan<byte> save)
    {
        using var output = new MemoryStream();
        Span<byte> header = stackalloc byte[HeaderSize];
        Magic.CopyTo(header);
        header[6] = 1;
        header[7] = (byte)'#';
        BinaryPrimitives.WriteUInt32LittleEndian(header[8..], ChunkSize);
        BinaryPrimitives.WriteUInt64LittleEndian(header[12..], (ulong)save.Length);
        output.Write(header);
        for (int start = 0; start < save.Length; start += ChunkSize)
        {
            using var chunk = new MemoryStream();
            using (var zlib = new ZLibStream(chunk, CompressionLevel.Optimal, leaveOpen: true))
                zlib.Write(save[start..Math.Min(save.Length, start + ChunkSize)]);
            Span<byte> size = stackalloc byte[4];
            BinaryPrimitives.WriteUInt32LittleEndian(size, (uint)chunk.Length);
            output.Write(size);
            output.Write(chunk.GetBuffer(), 0, (int)chunk.Length);
        }
        return output.ToArray();
    }
}
