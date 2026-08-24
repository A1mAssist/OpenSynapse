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
    private long _publishedFrames;
    private long _duplicateFrames;
    private long _version;
    private long _lastPublishedAtUnixMilliseconds;

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

    public long PublishedFrames => Interlocked.Read(ref _publishedFrames);
    public long DuplicateFrames => Interlocked.Read(ref _duplicateFrames);
    public long Version => Interlocked.Read(ref _version);
    public DateTimeOffset? LastPublishedAt
    {
        get
        {
            var value = Interlocked.Read(ref _lastPublishedAtUnixMilliseconds);
            return value == 0 ? null : DateTimeOffset.FromUnixTimeMilliseconds(value);
        }
    }

    public bool Publish(IReadOnlyList<RazerRgb> frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        if (frame.Count != QuickLightingEngine.PixelCount)
        {
            throw new ArgumentException("Chroma 外部帧必须包含完整的 Blade 矩阵。", nameof(frame));
        }

        var copy = frame.ToArray();
        var previous = Volatile.Read(ref _latestFrame);
        if (previous.AsSpan().SequenceEqual(copy))
        {
            Interlocked.Increment(ref _duplicateFrames);
            return false;
        }

        Volatile.Write(ref _latestFrame, copy);
        Interlocked.Increment(ref _publishedFrames);
        Interlocked.Increment(ref _version);
        Interlocked.Exchange(
            ref _lastPublishedAtUnixMilliseconds,
            DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        return true;
    }

    public bool Clear() => Publish(_blackFrame);
}

public static class ChromaKeyboardFrameMapper
{
    private const uint KeyActiveMask = 0x01000000;
    private const int ChromaColumns = 22;
    private static readonly short[] ChromaToLogicalTarget =
    [
        // Chroma source row 0. Blade physical row 0 is completed by Insert/Delete below.
        -1, 0, -1, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, -1, -1, 15, -1, -1, -1, -1,
        // Chroma source row 1. Page Up/M1 is appended to Blade physical row 1 below.
        -1, 16, 17, 18, 19, 20, 21, 22, 23, 24, 25, 26, 27, 28, 30, -1, -1, -1, -1, -1, -1, -1,
        // Chroma source row 2: Insert targets physical row 0; Page Up/M1 targets physical row 1.
        -1, 32, 33, 34, 35, 36, 37, 38, 39, 40, 41, 42, 43, 44, 46, 13, -1, 31, -1, -1, -1, -1,
        // Chroma source row 3: Delete targets physical row 0; Page Down/M2 targets physical row 2.
        -1, 48, 49, 50, 51, 52, 53, 54, 55, 56, 57, 58, 59, -1, 62, 14, -1, 47, -1, -1, -1, 63,
        // Chroma source row 4: Up targets the half-height arrow position in physical row 5.
        -1, 64, -1, 66, 67, 68, 69, 70, 71, 72, 73, 74, 75, -1, 78, -1, 93, -1, -1, -1, -1, 79,
        // Chroma source row 5: physical order is LCtrl, Fn, Win, LAlt, Space, RAlt,
        // Copilot, RCtrl, Left, stacked Up/Down, Right, M5. Space has no LED.
        -1, 80, 82, 83, -1, -1, -1, -1, -1, -1, -1, 89, 81, 90, 91, 92, 109, 94, -1, -1, -1, 95,
    ];

    public static RazerRgb[] Static(RazerRgb color) =>
        QuickLightingEngine.RenderSolid(color);

    public static RazerRgb[] Custom(IReadOnlyList<IReadOnlyList<uint>> matrix)
    {
        ValidateMatrix(matrix, 6, 22, nameof(matrix));
        return BladeLightingLayout.MapToDeviceFrame(ToSixRowLogicalFrame(matrix, ToRgb));
    }

    public static RazerRgb[] Custom2(IReadOnlyList<IReadOnlyList<uint>> matrix)
    {
        ValidateMatrix(matrix, 8, 24, nameof(matrix));
        return BladeLightingLayout.MapToDeviceFrame(ToExtendedLogicalFrame(matrix, ToRgb));
    }

    public static RazerRgb[] Custom2Key(
        IReadOnlyList<IReadOnlyList<uint>> colors,
        IReadOnlyList<IReadOnlyList<uint>> keys)
    {
        ValidateMatrix(colors, 8, 24, nameof(colors));
        ValidateMatrix(keys, 6, 22, nameof(keys));
        var output = ToExtendedLogicalFrame(colors, ToRgb);
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
        var output = ToSixRowLogicalFrame(colors, ToRgb);
        ApplyKeyOverrides(output, keys);
        return BladeLightingLayout.MapToDeviceFrame(output);
    }

    private static void ApplyKeyOverrides(
        RazerRgb[] output,
        IReadOnlyList<IReadOnlyList<uint>> keys)
    {
        for (var row = 0; row < keys.Count; row++)
        {
            var source = keys[row];
            for (var column = 0; column < source.Count; column++)
            {
                var target = GetChromaTarget(row, column);
                var encoded = source[column];
                if ((encoded & KeyActiveMask) != 0)
                {
                    if (target >= 0)
                    {
                        output[target] =
                        ToRgb((~encoded) & 0x00FFFFFFu);
                    }
                }
            }
        }

    }

    public static RazerRgb ToRgb(uint bgr) => new(
        (byte)(bgr & 0xFF),
        (byte)((bgr >> 8) & 0xFF),
        (byte)((bgr >> 16) & 0xFF));

    private static RazerRgb[] ToExtendedLogicalFrame(
        IReadOnlyList<IReadOnlyList<uint>> matrix,
        Func<uint, RazerRgb> convert)
    {
        var output = new RazerRgb[BladeLightingLayout.LogicalPixelCount];
        for (var row = 0; row < 6; row++)
        {
            for (var column = 0; column < ChromaColumns; column++)
            {
                var target = GetChromaTarget(row, column);
                if (target >= 0)
                {
                    output[target] = convert(matrix[row + 1][column + 1]);
                }
            }
        }

        return output;
    }

    private static RazerRgb[] ToSixRowLogicalFrame(
        IReadOnlyList<IReadOnlyList<uint>> matrix,
        Func<uint, RazerRgb> convert)
    {
        var output = new RazerRgb[BladeLightingLayout.LogicalPixelCount];
        for (var row = 0; row < matrix.Count; row++)
        {
            var source = matrix[row];
            for (var column = 0; column < source.Count; column++)
            {
                var target = GetChromaTarget(row, column);
                if (target >= 0)
                {
                    output[target] = convert(source[column]);
                }
            }
        }

        return output;
    }

    private static int GetChromaTarget(int row, int column) =>
        (uint)row < 6 && (uint)column < ChromaColumns
            ? ChromaToLogicalTarget[row * ChromaColumns + column]
            : -1;

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
