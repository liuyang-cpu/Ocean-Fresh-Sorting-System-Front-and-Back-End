using System.Buffers.Binary;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;

namespace OceanFresh.SortingSystem.Infrastructure;

internal static class TechikRawFrameCodec
{
    private const int HeaderSize = 40;

    public static async Task<TechikDetectorFrame> ReadFrameAsync(
        Stream stream,
        CancellationToken cancellationToken)
    {
        var header = new byte[HeaderSize];
        await stream.ReadExactlyAsync(header, cancellationToken);
        ValidateHeader(header, out var width, out var height, out var channels);

        var detectorId = BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(8, 4));
        var sequence = BinaryPrimitives.ReadInt64LittleEndian(header.AsSpan(24, 8));
        var capturedMicroseconds =
            BinaryPrimitives.ReadInt64LittleEndian(header.AsSpan(32, 8));
        var payloadLength = checked(width * height * channels * sizeof(ushort));
        var rawFrame = new byte[checked(HeaderSize + payloadLength)];
        header.CopyTo(rawFrame, 0);
        await stream.ReadExactlyAsync(
            rawFrame.AsMemory(HeaderSize, payloadLength),
            cancellationToken);

        var capturedAt = DateTimeOffset.FromUnixTimeMilliseconds(
            capturedMicroseconds / 1000);
        var remainingMicroseconds = capturedMicroseconds % 1000;
        if (remainingMicroseconds > 0)
        {
            capturedAt = capturedAt.AddTicks(remainingMicroseconds * 10);
        }

        return new TechikDetectorFrame(
            rawFrame,
            detectorId,
            width,
            height,
            sequence,
            capturedAt);
    }

    public static async Task<byte[]> EncodePngAsync(
        byte[] rawFrame,
        CancellationToken cancellationToken)
    {
        ValidateHeader(rawFrame, out var width, out var height, out var channels);

        var expectedLength = checked(HeaderSize + width * height * channels * sizeof(ushort));
        if (rawFrame.Length != expectedLength)
        {
            throw new InvalidDataException(
                $"Techik 原始帧长度不匹配：期望 {expectedLength}，实际 {rawFrame.Length}。");
        }

        using var image = new Image<L16>(width, height);
        image.ProcessPixelRows(accessor =>
        {
            var source = rawFrame.AsSpan(HeaderSize);
            for (var y = 0; y < height; y++)
            {
                var row = accessor.GetRowSpan(y);
                for (var x = 0; x < width; x++)
                {
                    var offset = (y * width + x) * sizeof(ushort);
                    row[x] = new L16(BinaryPrimitives.ReadUInt16LittleEndian(source.Slice(offset, 2)));
                }
            }
        });

        await using var output = new MemoryStream();
        await image.SaveAsPngAsync(
            output,
            new PngEncoder { BitDepth = PngBitDepth.Bit16, ColorType = PngColorType.Grayscale },
            cancellationToken);
        return output.ToArray();
    }

    private static void ValidateHeader(
        ReadOnlySpan<byte> rawFrame,
        out int width,
        out int height,
        out int channels)
    {
        if (rawFrame.Length < HeaderSize ||
            rawFrame[0] != (byte)'O' ||
            rawFrame[1] != (byte)'F' ||
            rawFrame[2] != (byte)'R' ||
            rawFrame[3] != (byte)'1')
        {
            throw new InvalidDataException("Techik 原始帧头无效。");
        }

        var declaredHeaderSize = BinaryPrimitives.ReadUInt32LittleEndian(rawFrame.Slice(4, 4));
        width = BinaryPrimitives.ReadInt32LittleEndian(rawFrame.Slice(12, 4));
        height = BinaryPrimitives.ReadInt32LittleEndian(rawFrame.Slice(16, 4));
        channels = BinaryPrimitives.ReadInt32LittleEndian(rawFrame.Slice(20, 4));
        if (declaredHeaderSize != HeaderSize ||
            width <= 0 ||
            height <= 0 ||
            channels != 1 ||
            width > 32768 ||
            height > 8192)
        {
            throw new InvalidDataException("Techik 原始帧尺寸无效。");
        }
    }
}
