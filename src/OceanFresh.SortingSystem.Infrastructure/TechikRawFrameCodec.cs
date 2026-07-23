using System.Buffers.Binary;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;

namespace OceanFresh.SortingSystem.Infrastructure;

internal static class TechikRawFrameCodec
{
    private const int HeaderSize = 40;

    public static async Task<byte[]> EncodePngAsync(
        byte[] rawFrame,
        CancellationToken cancellationToken)
    {
        if (rawFrame.Length < HeaderSize ||
            rawFrame[0] != (byte)'O' ||
            rawFrame[1] != (byte)'F' ||
            rawFrame[2] != (byte)'R' ||
            rawFrame[3] != (byte)'1')
        {
            throw new InvalidDataException("Techik 原始帧头无效。");
        }

        var declaredHeaderSize = BinaryPrimitives.ReadUInt32LittleEndian(rawFrame.AsSpan(4, 4));
        var width = BinaryPrimitives.ReadInt32LittleEndian(rawFrame.AsSpan(12, 4));
        var height = BinaryPrimitives.ReadInt32LittleEndian(rawFrame.AsSpan(16, 4));
        var channels = BinaryPrimitives.ReadInt32LittleEndian(rawFrame.AsSpan(20, 4));
        if (declaredHeaderSize != HeaderSize ||
            width <= 0 ||
            height <= 0 ||
            channels != 1 ||
            width > 32768 ||
            height > 8192)
        {
            throw new InvalidDataException("Techik 原始帧尺寸无效。");
        }

        var expectedLength = checked(HeaderSize + width * height * sizeof(ushort));
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
}
