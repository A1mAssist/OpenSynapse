using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.Windows.Lighting;

/// <summary>
/// Holds only the newest frame submitted by an external Chroma client.
/// The lighting runtime owns the cadence; HTTP callers never write HID directly.
/// </summary>
public sealed class ChromaExternalFrameSource : ISoftwareLightingFrameSource
{
    private readonly RazerRgb[] _blackFrame = new RazerRgb[QuickLightingEngine.PixelCount];
    private RazerRgb[] _latestFrame;

    public ChromaExternalFrameSource()
    {
        _latestFrame = _blackFrame.ToArray();
    }

    public ValueTask<IReadOnlyList<RazerRgb>> RenderAsync(
        TimeSpan elapsed,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult<IReadOnlyList<RazerRgb>>(Volatile.Read(ref _latestFrame));
    }

    public void Publish(IReadOnlyList<RazerRgb> frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        if (frame.Count != QuickLightingEngine.PixelCount)
        {
            throw new ArgumentException("Chroma 外部帧必须包含完整的 Blade 矩阵。", nameof(frame));
        }

        Volatile.Write(ref _latestFrame, frame.ToArray());
    }

    public void Clear() => Publish(_blackFrame);
}

public static class ChromaKeyboardFrameMapper
{
    private const uint KeyActiveMask = 0x01000000;

    public static RazerRgb[] Static(RazerRgb color) =>
        QuickLightingEngine.RenderSolid(color);

    public static RazerRgb[] Custom(IReadOnlyList<IReadOnlyList<uint>> matrix)
    {
        ValidateMatrix(matrix, 6, 22, nameof(matrix));
        return BladeLightingLayout.MapToDeviceFrame(ToLogicalFrame(matrix, ToRgb));
    }

    public static RazerRgb[] Custom2(IReadOnlyList<IReadOnlyList<uint>> matrix)
    {
        ValidateMatrix(matrix, 8, 24, nameof(matrix));
        return BladeLightingLayout.MapToDeviceFrame(ToLogicalFrame(matrix, ToRgb));
    }

    public static RazerRgb[] Custom2Key(
        IReadOnlyList<IReadOnlyList<uint>> colors,
        IReadOnlyList<IReadOnlyList<uint>> keys)
    {
        ValidateMatrix(colors, 8, 24, nameof(colors));
        ValidateMatrix(keys, 6, 22, nameof(keys));
        var output = ToLogicalFrame(colors, ToRgb);
        ApplyKeyOverrides(output, keys);
        return BladeLightingLayout.MapToDeviceFrame(output);
    }

    public static RazerRgb[] CustomKey(
        IReadOnlyList<IReadOnlyList<uint>> colors,
        IReadOnlyList<IReadOnlyList<uint>> keys)
    {
        ArgumentNullException.ThrowIfNull(colors);
        ArgumentNullException.ThrowIfNull(keys);
        ValidateMatrix(colors, 6, 22, nameof(colors));
        ValidateMatrix(keys, 6, 22, nameof(keys));
        var output = ToLogicalFrame(colors, ToRgb);
        ApplyKeyOverrides(output, keys);
        return BladeLightingLayout.MapToDeviceFrame(output);
    }

    private static void ApplyKeyOverrides(
        RazerRgb[] output,
        IReadOnlyList<IReadOnlyList<uint>> keys)
    {
        for (var row = 0; row < QuickLightingEngine.LogicalRows; row++)
        {
            var sourceRow = Math.Min(
                (int)((long)row * keys.Count / QuickLightingEngine.LogicalRows),
                keys.Count - 1);
            var source = keys[sourceRow];
            for (var column = 0; column < QuickLightingEngine.LogicalColumns; column++)
            {
                var sourceColumn = Math.Min(
                    (int)((long)column * source.Count / QuickLightingEngine.LogicalColumns),
                    source.Count - 1);
                var encoded = source[sourceColumn];
                if ((encoded & KeyActiveMask) != 0)
                {
                    output[row * QuickLightingEngine.LogicalColumns + column] =
                        ToRgb((~encoded) & 0x00FFFFFFu);
                }
            }
        }

    }

    public static RazerRgb ToRgb(uint bgr) => new(
        (byte)(bgr & 0xFF),
        (byte)((bgr >> 8) & 0xFF),
        (byte)((bgr >> 16) & 0xFF));

    private static RazerRgb[] ToLogicalFrame(
        IReadOnlyList<IReadOnlyList<uint>> matrix,
        Func<uint, RazerRgb> convert)
    {
        var output = new RazerRgb[BladeLightingLayout.LogicalPixelCount];
        for (var row = 0; row < QuickLightingEngine.LogicalRows; row++)
        {
            var sourceRow = Math.Min(
                (int)((long)row * matrix.Count / QuickLightingEngine.LogicalRows),
                matrix.Count - 1);
            var source = matrix[sourceRow];
            for (var column = 0; column < QuickLightingEngine.LogicalColumns; column++)
            {
                var sourceColumn = Math.Min(
                    (int)((long)column * source.Count / QuickLightingEngine.LogicalColumns),
                    source.Count - 1);
                output[row * QuickLightingEngine.LogicalColumns + column] =
                    convert(source[sourceColumn]);
            }
        }

        return output;
    }

    private static void ValidateMatrix(
        IReadOnlyList<IReadOnlyList<uint>> matrix,
        int rows,
        int columns,
        string parameterName)
    {
        ArgumentNullException.ThrowIfNull(matrix, parameterName);
        if (matrix.Count != rows || matrix.Any(row => row is null || row.Count != columns))
        {
            throw new ArgumentException($"Chroma 键盘矩阵必须是 {rows} x {columns}。", parameterName);
        }
    }
}
