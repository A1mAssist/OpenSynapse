namespace OpenSynapse.Windows.Protocols;

public enum BladeWaveDirection : byte
{
    Left = 0x01,
    Right = 0x02,
}

public readonly record struct RazerRgb(byte Red, byte Green, byte Blue);

/// <summary>
/// Source-backed request builders for the Blade 16 2025 standard matrix protocol.
/// Sending remains gated until the current effect can be restored deterministically.
/// </summary>
public static class BladeLightingProtocol
{
    public const int Rows = 6;
    public const int Columns = 17;

    private const byte TransactionId = 0xFF;
    private const byte CommandClass = 0x03;
    private const byte EffectCommandId = 0x0A;
    private const byte MatrixCommandClass = 0x03;
    private const byte MatrixCommandId = 0x0B;

    public static byte[] CreateLightingEngineGateRequest(byte transactionId) =>
        RazerFeatureReport.CreateRequest(
            transactionId,
            0x06,
            0x0F,
            0x02,
            new byte[] { 0x00, 0x00, 0x08, 0x00, 0x00, 0x00 });

    public static byte[] CreateCustomFrameEffectRequest(byte transactionId) =>
        RazerFeatureReport.CreateRequest(
            ValidateMatrixTransactionId(transactionId),
            0x02,
            CommandClass,
            EffectCommandId,
            new byte[] { 0x05, 0x00 });

    public static byte[][] CreateMatrixFrameRequests(IReadOnlyList<RazerRgb> frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        if (frame.Count != Rows * Columns)
        {
            throw new ArgumentException("Blade 灯光帧必须正好包含 6 x 17 个颜色。", nameof(frame));
        }

        var requests = new byte[Rows + 1][];
        for (byte row = 0; row < Rows; row++)
        {
            var offset = row * Columns;
            requests[row] = CreateMatrixRowRequest(
                (byte)(row + 1),
                row,
                0,
                frame.Skip(offset).Take(Columns).ToArray());
        }
        requests[Rows] = CreateCustomFrameEffectRequest(Rows + 1);
        return requests;
    }

    public static byte[] CreateOffRequest() =>
        CreateEffectRequest(0x01, new byte[] { 0x00 });

    public static byte[] CreateWaveRequest(BladeWaveDirection direction)
    {
        if (!Enum.IsDefined(direction))
        {
            throw new ArgumentOutOfRangeException(nameof(direction));
        }

        return CreateEffectRequest(0x02, new byte[] { 0x01, (byte)direction });
    }

    public static byte[] CreateSpectrumRequest() =>
        CreateEffectRequest(0x01, new byte[] { 0x04 });

    public static byte[] CreateReactiveRequest(byte speed, RazerRgb color)
    {
        if (speed is < 1 or > 4)
        {
            throw new ArgumentOutOfRangeException(nameof(speed));
        }

        return CreateEffectRequest(
            0x05,
            new byte[] { 0x02, speed, color.Red, color.Green, color.Blue });
    }

    public static byte[] CreateStaticRequest(RazerRgb color) =>
        CreateEffectRequest(
            0x04,
            new byte[] { 0x06, color.Red, color.Green, color.Blue });

    public static byte[] CreateBreathingRandomRequest() =>
        CreateEffectRequest(0x08, new byte[] { 0x03, 0x03, 0, 0, 0, 0, 0, 0 });

    public static byte[] CreateBreathingSingleRequest(RazerRgb color) =>
        CreateEffectRequest(
            0x08,
            new byte[] { 0x03, 0x01, color.Red, color.Green, color.Blue, 0, 0, 0 });

    public static byte[] CreateBreathingDualRequest(RazerRgb first, RazerRgb second) =>
        CreateEffectRequest(
            0x08,
            new byte[]
            {
                0x03, 0x02,
                first.Red, first.Green, first.Blue,
                second.Red, second.Green, second.Blue,
            });

    public static byte[] CreateStarlightRandomRequest(byte speed) =>
        CreateStarlightRequest(speed, 0x03, default, default);

    public static byte[] CreateStarlightSingleRequest(byte speed, RazerRgb color) =>
        CreateStarlightRequest(speed, 0x01, color, default);

    public static byte[] CreateStarlightDualRequest(
        byte speed,
        RazerRgb first,
        RazerRgb second) =>
        CreateStarlightRequest(speed, 0x02, first, second);

    public static byte[] CreateMatrixRowRequest(
        byte transactionId,
        byte row,
        byte startColumn,
        IReadOnlyList<RazerRgb> colors)
    {
        ArgumentNullException.ThrowIfNull(colors);
        ValidateMatrixTransactionId(transactionId);
        if (row >= Rows)
        {
            throw new ArgumentOutOfRangeException(nameof(row));
        }
        if (colors.Count == 0 || startColumn >= Columns || startColumn + colors.Count > Columns)
        {
            throw new ArgumentOutOfRangeException(nameof(colors));
        }

        var arguments = new byte[4 + colors.Count * 3];
        arguments[0] = 0xFF;
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
            transactionId,
            checked((byte)arguments.Length),
            MatrixCommandClass,
            MatrixCommandId,
            arguments);
    }

    private static byte[] CreateEffectRequest(byte dataSize, byte[] arguments) =>
        RazerFeatureReport.CreateRequest(
            TransactionId,
            dataSize,
            CommandClass,
            EffectCommandId,
            arguments);

    private static byte ValidateMatrixTransactionId(byte transactionId) =>
        transactionId <= 30
            ? transactionId
            : throw new ArgumentOutOfRangeException(nameof(transactionId));

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

        // OpenRazer commit 4e65f1249587 verified this wire format on hardware:
        // the report declares one data byte while carrying all nine arguments.
        return RazerFeatureReport.CreateRequestWithDeclaredSize(
            TransactionId,
            0x01,
            CommandClass,
            EffectCommandId,
            new byte[]
            {
                0x19, colorMode, speed,
                first.Red, first.Green, first.Blue,
                second.Red, second.Green, second.Blue,
            });
    }
}
