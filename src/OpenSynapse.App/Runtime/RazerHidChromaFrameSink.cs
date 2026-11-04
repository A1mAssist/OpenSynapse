using OpenSynapse.Core.Devices;
using OpenSynapse.Windows.Devices;
using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.App.Runtime;

internal sealed class RazerHidChromaFrameSink
{
    private const int SourceRows = 6;
    private const int SourceColumns = 22;
    private readonly RazerHidDeviceService _service;
    private readonly Func<IReadOnlyList<RazerHidDeviceConnection>> _connections;
    private readonly Func<RazerHidDeviceConnection, bool> _lightingEnabled;
    private readonly Func<RazerHidDeviceConnection, bool> _chromaOverrideEnabled;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Dictionary<string, FrameState> _lastFrames = new(StringComparer.OrdinalIgnoreCase);
    private long _latestFrameVersion;

    internal RazerHidChromaFrameSink(
        RazerHidDeviceService service,
        Func<IReadOnlyList<RazerHidDeviceConnection>> connections,
        Func<RazerHidDeviceConnection, bool> lightingEnabled,
        Func<RazerHidDeviceConnection, bool> chromaOverrideEnabled)
    {
        _service = service ?? throw new ArgumentNullException(nameof(service));
        _connections = connections ?? throw new ArgumentNullException(nameof(connections));
        _lightingEnabled = lightingEnabled ?? throw new ArgumentNullException(nameof(lightingEnabled));
        _chromaOverrideEnabled = chromaOverrideEnabled ?? throw new ArgumentNullException(nameof(chromaOverrideEnabled));
    }

    internal Task<bool> ApplyAsync(
        IReadOnlyList<RazerRgb> sourceFrame,
        CancellationToken cancellationToken = default) =>
        ApplyAsync(sourceFrame, SourceRows, SourceColumns, category: null, cancellationToken);

    internal async Task<bool> ApplyAsync(
        IReadOnlyList<RazerRgb> sourceFrame,
        int sourceRows,
        int sourceColumns,
        DeviceCategory? category,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sourceFrame);
        if (sourceRows <= 0 || sourceColumns <= 0 ||
            sourceFrame.Count != sourceRows * sourceColumns)
        {
            throw new ArgumentException("The Chroma source frame does not match its dimensions.", nameof(sourceFrame));
        }

        // REST clients can post faster than a device can accept its row writes.
        // Keep only the newest queued frame; an in-flight frame is allowed to finish.
        var frameVersion = Interlocked.Increment(ref _latestFrameVersion);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (frameVersion != Volatile.Read(ref _latestFrameVersion))
            {
                return false;
            }
            var targets = _connections()
                .Where(connection => connection.IsReady &&
                    (category is null || connection.Definition.Category == category) &&
                    connection.Definition.MatrixDimensions is not null &&
                    connection.Capabilities.Contains(RazerHidBackendCapability.MatrixFrameWrite) &&
                    _lightingEnabled(connection) &&
                    _chromaOverrideEnabled(connection))
                .ToArray();
            if (targets.Length == 0)
            {
                _lastFrames.Clear();
                return false;
            }

            var targetIds = targets
                .Select(target => target.InstanceId)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var staleId in _lastFrames.Keys
                         .Where(id => !targetIds.Contains(id))
                         .ToArray())
            {
                _lastFrames.Remove(staleId);
            }

            var writes = new List<(RazerHidDeviceConnection Target, RazerHidColor[] Frame)>();
            foreach (var target in targets)
            {
                var dimensions = target.Definition.MatrixDimensions!;
                var frame = Resize(sourceFrame, sourceRows, sourceColumns, dimensions.Rows, dimensions.Columns)
                    .Select(color => new RazerHidColor(color.Red, color.Green, color.Blue))
                    .ToArray();

                if (_lastFrames.TryGetValue(target.InstanceId, out var previous) &&
                    ReferenceEquals(previous.Connection, target) &&
                    previous.Frame.AsSpan().SequenceEqual(frame))
                {
                    continue;
                }

                writes.Add((target, frame));
            }

            if (writes.Count == 0)
            {
                return true;
            }

            foreach (var (target, frame) in writes)
            {
                await _service.SetCustomFrameAsync(target, frame, cancellationToken).ConfigureAwait(false);
                _lastFrames[target.InstanceId] = new FrameState(target, frame);
            }
            return true;
        }
        finally
        {
            _gate.Release();
        }
    }

    private sealed record FrameState(
        RazerHidDeviceConnection Connection,
        RazerHidColor[] Frame);

    private static IReadOnlyList<RazerRgb> Resize(
        IReadOnlyList<RazerRgb> source,
        int sourceRows,
        int sourceColumns,
        int rows,
        int columns)
    {
        var result = new RazerRgb[rows * columns];
        for (var row = 0; row < rows; row++)
        {
            var sourceRow = rows == 1 ? 0 : (int)Math.Round(row * (sourceRows - 1d) / (rows - 1d));
            for (var column = 0; column < columns; column++)
            {
                var sourceColumn = columns == 1
                    ? 0
                    : (int)Math.Round(column * (sourceColumns - 1d) / (columns - 1d));
                result[row * columns + column] = source[sourceRow * sourceColumns + sourceColumn];
            }
        }
        return result;
    }
}
