namespace OpenSynapse.Windows.Protocols;

/// <summary>
/// Request builders for the rzDevice25LedMatrixBlade2 keyboard. The middleware drives
/// firmware effects through 03:02 and software frames through 03:0B, both with
/// transaction 0xFF. Frame rows carry a 0xFF frame id, the row index, the inclusive
/// column range and RGB triples.
/// </summary>
public static class BladeLightingLedMatrixProtocol
{
    public const int Rows = 6;
    public const int Columns = 17;

    private const byte TransactionId = 0xFF;
    private const byte EffectCommandClass = 0x03;
    private const byte EffectCommandId = 0x02;
    private const byte FrameCommandClass = 0x03;
    private const byte FrameCommandId = 0x03;
    private const byte FrameId = 0xFF;

    // Middleware LED effect ids (rzDevice25 LedEffect).
    private const byte EffectOff = 0;
    private const byte EffectWave = 1;
    private const byte EffectAfterglow = 2;
    private const byte EffectBreathing = 3;
    private const byte EffectSpectrum = 4;
    private const byte EffectCustomFrame = 5;
    private const byte EffectStatic = 6;
    private const byte EffectStarlight = 25;

    public static byte[] CreateOffRequest() => CreateEffectRequest(EffectOff);

    public static byte[] CreateWaveRequest(BladeWaveDirection direction)
    {
        if (!Enum.IsDefined(direction))
        {
            throw new ArgumentOutOfRangeException(nameof(direction));
        }

        return CreateEffectRequest(EffectWave, (byte)direction);
    }

    public static byte[] CreateSpectrumRequest() => CreateEffectRequest(EffectSpectrum);

    public static byte[] CreateReactiveRequest(byte speed, RazerRgb color)
    {
        if (speed is < 1 or > 4)
        {
            throw new ArgumentOutOfRangeException(nameof(speed));
        }

        return CreateEffectRequest(
            EffectAfterglow,
            speed,
            color.Red,
            color.Green,
            color.Blue);
    }

    public static byte[] CreateStaticRequest(RazerRgb color) =>
        CreateEffectRequest(EffectStatic, color.Red, color.Green, color.Blue);

    public static byte[] CreateBreathingRandomRequest() =>
        CreateEffectRequest(EffectBreathing, 0x03);

    public static byte[] CreateBreathingSingleRequest(RazerRgb color) =>
        CreateEffectRequest(
            EffectBreathing,
            0x01,
            color.Red,
            color.Green,
            color.Blue);

    public static byte[] CreateBreathingDualRequest(RazerRgb first, RazerRgb second) =>
        CreateEffectRequest(
            EffectBreathing,
            0x02,
            first.Red,
            first.Green,
            first.Blue,
            second.Red,
            second.Green,
            second.Blue);

    public static byte[] CreateStarlightRandomRequest(byte speed) =>
        CreateStarlightRequest(speed, 0x03, default, default);

    public static byte[] CreateStarlightSingleRequest(byte speed, RazerRgb color) =>
        CreateStarlightRequest(speed, 0x01, color, default);

    public static byte[] CreateStarlightDualRequest(
        byte speed,
        RazerRgb first,
        RazerRgb second) =>
        CreateStarlightRequest(speed, 0x02, first, second);

    public static byte[] CreateCustomFrameEffectRequest() =>
        CreateEffectRequest(EffectCustomFrame, 0x00);

    public static byte[][] CreateMatrixFrameRequests(IReadOnlyList<RazerRgb> frame) =>
        CreateMatrixFrameRequests(frame, Rows, Columns);

    public static byte[][] CreateMatrixFrameRequests(
        IReadOnlyList<RazerRgb> frame,
        int rows,
        int columns)
    {
        ArgumentNullException.ThrowIfNull(frame);
        ValidateMatrixDimensions(rows, columns);
        if (frame.Count != rows * columns)
        {
            throw new ArgumentException(
                $"A Blade lighting frame must contain exactly {rows} x {columns} colors.",
                nameof(frame));
        }

        var requests = new byte[rows + 1][];
        for (byte row = 0; row < rows; row++)
        {
            requests[row] = CreateMatrixRowRequest(
                row,
                0,
                frame.Skip(row * columns).Take(columns).ToArray(),
                rows,
                columns);
        }
        requests[rows] = CreateCustomFrameEffectRequest();
        return requests;
    }

    public static byte[] CreateMatrixRowRequest(
        byte row,
        byte startColumn,
        IReadOnlyList<RazerRgb> colors) =>
        CreateMatrixRowRequest(row, startColumn, colors, Rows, Columns);

    public static byte[] CreateMatrixRowRequest(
        byte row,
        byte startColumn,
        IReadOnlyList<RazerRgb> colors,
        int rows,
        int columns)
    {
        ArgumentNullException.ThrowIfNull(colors);
        ValidateMatrixDimensions(rows, columns);
        if (row >= rows)
        {
            throw new ArgumentOutOfRangeException(nameof(row));
        }
        if (colors.Count == 0 || startColumn >= columns || startColumn + colors.Count > columns)
        {
            throw new ArgumentOutOfRangeException(nameof(colors));
        }

        var arguments = new byte[4 + colors.Count * 3];
        if (arguments.Length > 80)
        {
            throw new ArgumentOutOfRangeException(nameof(colors), "A matrix row cannot exceed the 80-byte HID payload.");
        }
        arguments[0] = FrameId;
        arguments[1] = row;
        arguments[2] = startColumn;
        arguments[3] = checked((byte)(startColumn + colors.Count - 1));
        for (var index = 0; index < colors.Count; index++)
        {
            var offset = 4 + index * 3;
            arguments[offset] = colors[index].Red;
            arguments[offset + 1] = colors[index].Green;
            arguments[offset + 2] = colors[index].Blue;
        }

        return RazerFeatureReport.CreateRequest(
            TransactionId,
            checked((byte)arguments.Length),
            FrameCommandClass,
            FrameCommandId,
            arguments);
    }

    private static byte[] CreateEffectRequest(byte effectId, params byte[] arguments)
    {
        var payload = new byte[1 + arguments.Length];
        payload[0] = effectId;
        arguments.CopyTo(payload.AsSpan(1));
        return RazerFeatureReport.CreateRequest(
            TransactionId,
            checked((byte)payload.Length),
            EffectCommandClass,
            EffectCommandId,
            payload);
    }

    private static byte[] CreateStarlightRequest(
        byte speed,
        byte colorMode,
        RazerRgb first,
        RazerRgb second)
    {
        if (speed is < 1 or > 3)
        {
            throw new ArgumentOutOfRangeException(nameof(speed));
        }

        return CreateEffectRequest(
            EffectStarlight,
            colorMode,
            speed,
            first.Red,
            first.Green,
            first.Blue,
            second.Red,
            second.Green,
            second.Blue);
    }

    private static void ValidateMatrixDimensions(int rows, int columns)
    {
        if (rows is < 1 or > 6 || columns is < 1 or > 25)
        {
            throw new ArgumentOutOfRangeException(nameof(rows), "Blade matrix dimensions must be 1..6 rows and 1..25 columns.");
        }
    }
}
