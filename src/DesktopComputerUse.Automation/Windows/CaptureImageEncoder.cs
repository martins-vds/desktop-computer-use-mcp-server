using System.Buffers.Binary;
using System.IO.Compression;
using DesktopComputerUse.Contracts.Automation;

namespace DesktopComputerUse.Automation.Windows;

public static class CaptureImageEncoder
{
    public const int MaximumPixels = 16_000_000;

    public static byte[] RedactAndEncodePng(NativePixelBuffer pixels, IReadOnlyList<PhysicalScreenRect> imageRegions)
    {
        ValidateBuffer(pixels);
        // Redact the owned buffer before encoding; no unredacted image is ever exported.
        foreach (var region in imageRegions)
        {
            ValidateRegion(region, pixels);
            RedactRegion(region, pixels);
        }
        using var compressed = new MemoryStream();
        using (var zlib = new ZLibStream(compressed, CompressionLevel.Fastest, leaveOpen: true))
        {
            var row = new byte[checked(pixels.Width * 4 + 1)];
            for (var y = 0; y < pixels.Height; y++)
            {
                row[0] = 0;
                for (var x = 0; x < pixels.Width; x++)
                {
                    var source = (y * pixels.Width + x) * 4;
                    var destination = x * 4 + 1;
                    row[destination] = pixels.BgraPixels[source + 2];
                    row[destination + 1] = pixels.BgraPixels[source + 1];
                    row[destination + 2] = pixels.BgraPixels[source];
                    row[destination + 3] = 255;
                }
                zlib.Write(row);
            }
        }
        using var output = new MemoryStream();
        output.Write([137, 80, 78, 71, 13, 10, 26, 10]);
        var header = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(0, 4), pixels.Width);
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4, 4), pixels.Height);
        header[8] = 8;
        header[9] = 6;
        WriteChunk(output, "IHDR"u8, header);
        WriteChunk(output, "IDAT"u8, compressed.ToArray());
        WriteChunk(output, "IEND"u8, []);
        return output.ToArray();
    }

    private static void ValidateBuffer(NativePixelBuffer pixels)
    {
        if (pixels.Width <= 0 || pixels.Height <= 0 || (long)pixels.Width * pixels.Height > MaximumPixels ||
            pixels.BgraPixels.Length != checked(pixels.Width * pixels.Height * 4))
            throw new ArgumentException("Invalid or oversized capture buffer.", nameof(pixels));
    }
    private static void ValidateRegion(PhysicalScreenRect region, NativePixelBuffer pixels)
    {
        if (!region.IsNonEmpty || region.X < 0 || region.Y < 0 || region.Right > pixels.Width || region.Bottom > pixels.Height)
            throw new ArgumentException("Redaction bounds are outside the image.", nameof(region));
    }
    private static void RedactRegion(PhysicalScreenRect region, NativePixelBuffer pixels)
    {
        for (var y = region.Y; y < region.Bottom; y++)
            for (var x = region.X; x < region.Right; x++)
                Array.Clear(pixels.BgraPixels, checked((y * pixels.Width + x) * 4), 4);
    }

    private static void WriteChunk(Stream output, ReadOnlySpan<byte> type, ReadOnlySpan<byte> payload)
    {
        Span<byte> integer = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(integer, payload.Length);
        output.Write(integer);
        output.Write(type);
        output.Write(payload);
        var crc = uint.MaxValue;
        foreach (var b in type) crc = UpdateCrc(crc, b);
        foreach (var b in payload) crc = UpdateCrc(crc, b);
        BinaryPrimitives.WriteUInt32BigEndian(integer, ~crc);
        output.Write(integer);
    }

    private static uint UpdateCrc(uint crc, byte value)
    {
        crc ^= value;
        for (var i = 0; i < 8; i++) crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320 : crc >> 1;
        return crc;
    }
}
