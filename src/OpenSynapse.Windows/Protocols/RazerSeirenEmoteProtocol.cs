namespace OpenSynapse.Windows.Protocols;

/// <summary>
/// Seiren Emote 0F1B matrix protocol: 64 RGB pixels flattened row-major into four
/// 16-pixel HID rows, then a latch. The shared feature-report builder owns the
/// 91-byte framing and XOR checksum.
/// </summary>
public static class RazerSeirenEmoteProtocol
{
    public const byte TransactionId = 0x3F;
    public const byte CommandClass = 0x0F;
    public const byte RowCommandId = 0x03;
    public const byte LatchCommandId = 0x02;

    public const int Width = 8;
    public const int Height = 8;
    public const int PixelCount = Width * Height;
    public const int PackedRgbLength = PixelCount * 3;
    public const int RowCount = 4;
    public const int PixelsPerRow = 16;
    public const int RowDataSize = 5 + (PixelsPerRow * 3);
    public const int LatchDataSize = 12;

    public static byte[][] CreateFrameRequests(IReadOnlyList<RazerRgb> pixels)
    {
        ArgumentNullException.ThrowIfNull(pixels);
        if (pixels.Count != PixelCount)
        {
            throw new ArgumentException(
                $"A Seiren Emote frame must contain exactly {PixelCount} RGB pixels.",
                nameof(pixels));
        }

        var packed = new byte[PackedRgbLength];
        for (var index = 0; index < PixelCount; index++)
        {
            var color = pixels[index];
            var offset = index * 3;
            packed[offset] = color.Red;
            packed[offset + 1] = color.Green;
            packed[offset + 2] = color.Blue;
        }

        return CreateFrameRequests(packed);
    }

    public static byte[][] CreateFrameRequests(ReadOnlySpan<byte> packedRgb)
    {
        if (packedRgb.Length != PackedRgbLength)
        {
            throw new ArgumentException(
                $"A packed Seiren Emote frame must contain exactly {PackedRgbLength} RGB bytes.",
                nameof(packedRgb));
        }

        var requests = new byte[RowCount + 1][];
        for (byte row = 0; row < RowCount; row++)
        {
            var arguments = new byte[RowDataSize];
            arguments[2] = row;
            arguments[4] = PixelsPerRow - 1;
            packedRgb.Slice(row * PixelsPerRow * 3, PixelsPerRow * 3)
                .CopyTo(arguments.AsSpan(5));
            requests[row] = RazerFeatureReport.CreateRequest(
                TransactionId,
                RowDataSize,
                CommandClass,
                RowCommandId,
                arguments);
        }

        var latchArguments = new byte[LatchDataSize];
        latchArguments[2] = Height;
        requests[RowCount] = RazerFeatureReport.CreateRequest(
            TransactionId,
            LatchDataSize,
            CommandClass,
            LatchCommandId,
            latchArguments);
        return requests;
    }
}
