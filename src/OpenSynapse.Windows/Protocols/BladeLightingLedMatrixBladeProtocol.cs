namespace OpenSynapse.Windows.Protocols;

/// <summary>
/// Effect reports used by the older rzDevice25LedMatrixBlade generation (023A).
/// The middleware sends 03:0A with the effect id first; unlike the 2026 protocol
/// there is no store/region prefix.
/// </summary>
public static class BladeLightingLedMatrixBladeProtocol
{
    public const int Rows = 6;
    public const int Columns = 16;

    private const byte EffectCommandClass = 0x03;
    private const byte EffectCommandId = 0x0A;

    private const byte EffectOff = 0x00;
    private const byte EffectBreathing = 0x03;
    private const byte EffectSpectrum = 0x04;
    private const byte EffectStatic = 0x06;
    private const byte EffectWave = 0x01;
    private const byte EffectReactive = 0x02;

    public static byte[] CreateOffRequest(byte transactionId) =>
        CreateEffectRequest(transactionId, [EffectOff]);

    public static byte[] CreateSpectrumRequest(byte transactionId) =>
        CreateEffectRequest(transactionId, [EffectSpectrum]);

    public static byte[] CreateStaticRequest(byte transactionId, RazerRgb color) =>
        CreateEffectRequest(
            transactionId,
            [EffectStatic, color.Red, color.Green, color.Blue]);

    public static byte[] CreateBreathingSingleRequest(byte transactionId, RazerRgb color) =>
        CreateEffectRequest(
            transactionId,
            [EffectBreathing, 0x01, color.Red, color.Green, color.Blue]);

    public static byte[] CreateWaveRequest(byte transactionId, BladeWaveDirection direction)
    {
        if (!Enum.IsDefined(direction))
        {
            throw new ArgumentOutOfRangeException(nameof(direction));
        }

        return CreateEffectRequest(transactionId, [EffectWave, (byte)direction]);
    }

    public static byte[] CreateReactiveRequest(byte transactionId, byte speed, RazerRgb color)
    {
        if (speed is < 1 or > 4)
        {
            throw new ArgumentOutOfRangeException(nameof(speed));
        }

        return CreateEffectRequest(
            transactionId,
            [EffectReactive, speed, color.Red, color.Green, color.Blue]);
    }

    public static byte[][] CreateMatrixFrameRequests(IReadOnlyList<RazerRgb> frame)
    {
        byte transactionId = 0;
        return CreateMatrixFrameRequests(frame, Rows, Columns, () => transactionId++);
    }

    public static byte[][] CreateMatrixFrameRequests(
        IReadOnlyList<RazerRgb> frame,
        int rows,
        int columns,
        Func<byte> nextTransactionId)
    {
        ArgumentNullException.ThrowIfNull(frame);
        ArgumentNullException.ThrowIfNull(nextTransactionId);
        if (frame.Count != rows * columns)
        {
            throw new ArgumentException(
                $"A Blade lighting frame must contain exactly {rows} x {columns} colors.",
                nameof(frame));
        }

        var requests = new byte[rows + 1][];
        for (byte row = 0; row < rows; row++)
        {
            var offset = row * columns;
            requests[row] = BladeLightingProtocol.CreateMatrixRowRequest(
                nextTransactionId(),
                row,
                0,
                frame.Skip(offset).Take(columns).ToArray(),
                rows,
                columns);
        }

        requests[rows] = BladeLightingProtocol.CreateCustomFrameEffectRequest(nextTransactionId());
        return requests;
    }

    private static byte[] CreateEffectRequest(byte transactionId, byte[] payload) =>
        RazerFeatureReport.CreateRequest(
            transactionId,
            checked((byte)payload.Length),
            EffectCommandClass,
            EffectCommandId,
            payload);
}
