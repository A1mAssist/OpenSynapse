namespace OpenSynapse.Windows.Protocols;

/// <summary>
/// Compatibility facade for the 2026 Blade matrix dialect. The middleware sends
/// firmware effects on <c>03:0A</c> and matrix rows on <c>03:0B</c>, the same
/// source-backed wire format as <see cref="BladeLightingProtocol"/>.
/// </summary>
public static class BladeLightingLedMatrixProtocol
{
    public const int Rows = BladeLightingProtocol.Rows;
    public const int Columns = BladeLightingProtocol.Columns;

    public static byte[] CreateOffRequest() => BladeLightingProtocol.CreateOffRequest();

    public static byte[] CreateWaveRequest(BladeWaveDirection direction) =>
        BladeLightingProtocol.CreateWaveRequest(direction);

    public static byte[] CreateSpectrumRequest() => BladeLightingProtocol.CreateSpectrumRequest();

    public static byte[] CreateReactiveRequest(byte speed, RazerRgb color) =>
        BladeLightingProtocol.CreateReactiveRequest(speed, color);

    public static byte[] CreateStaticRequest(RazerRgb color) =>
        BladeLightingProtocol.CreateStaticRequest(color);

    public static byte[] CreateBreathingRandomRequest() =>
        BladeLightingProtocol.CreateBreathingRandomRequest();

    public static byte[] CreateBreathingSingleRequest(RazerRgb color) =>
        BladeLightingProtocol.CreateBreathingSingleRequest(color);

    public static byte[] CreateBreathingDualRequest(RazerRgb first, RazerRgb second) =>
        BladeLightingProtocol.CreateBreathingDualRequest(first, second);

    public static byte[] CreateStarlightRandomRequest(byte speed) =>
        BladeLightingProtocol.CreateStarlightRandomRequest(speed);

    public static byte[] CreateStarlightSingleRequest(byte speed, RazerRgb color) =>
        BladeLightingProtocol.CreateStarlightSingleRequest(speed, color);

    public static byte[] CreateStarlightDualRequest(byte speed, RazerRgb first, RazerRgb second) =>
        BladeLightingProtocol.CreateStarlightDualRequest(speed, first, second);

    public static byte[] CreateCustomFrameEffectRequest() =>
        BladeLightingProtocol.CreateCustomFrameEffectRequest(checked((byte)(Rows + 1)));

    public static byte[][] CreateMatrixFrameRequests(IReadOnlyList<RazerRgb> frame) =>
        CreateMatrixFrameRequests(frame, Rows, Columns);

    public static byte[][] CreateMatrixFrameRequests(
        IReadOnlyList<RazerRgb> frame,
        int rows,
        int columns) =>
        BladeLightingProtocol.CreateMatrixFrameRequests(frame, rows, columns);

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
        int columns) =>
        BladeLightingProtocol.CreateMatrixRowRequest(
            checked((byte)(row + 1)),
            row,
            startColumn,
            colors,
            rows,
            columns);
}
