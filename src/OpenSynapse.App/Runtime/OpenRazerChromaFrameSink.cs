using OpenSynapse.Windows.Devices;
using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.App.Runtime;

internal sealed class OpenRazerChromaFrameSink
{
    private const int SourceRows = 6;
    private const int SourceColumns = 22;
    private readonly OpenRazerDeviceService _service;
    private readonly Func<IReadOnlyList<OpenRazerDeviceConnection>> _connections;
    private readonly Func<OpenRazerDeviceConnection, bool> _lightingEnabled;
    private readonly Func<OpenRazerDeviceConnection, bool> _chromaOverrideEnabled;
    private readonly SemaphoreSlim _gate = new(1, 1);

    internal OpenRazerChromaFrameSink(
        OpenRazerDeviceService service,
        Func<IReadOnlyList<OpenRazerDeviceConnection>> connections,
        Func<OpenRazerDeviceConnection, bool> lightingEnabled,
        Func<OpenRazerDeviceConnection, bool> chromaOverrideEnabled)
    {
        _service = service ?? throw new ArgumentNullException(nameof(service));
        _connections = connections ?? throw new ArgumentNullException(nameof(connections));
        _lightingEnabled = lightingEnabled ?? throw new ArgumentNullException(nameof(lightingEnabled));
        _chromaOverrideEnabled = chromaOverrideEnabled ?? throw new ArgumentNullException(nameof(chromaOverrideEnabled));
    }

    internal async Task<bool> ApplyAsync(
        IReadOnlyList<RazerRgb> sourceFrame,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sourceFrame);
        if (sourceFrame.Count != SourceRows * SourceColumns)
        {
            throw new ArgumentException("A Chroma source frame must contain 6 x 22 colors.", nameof(sourceFrame));
        }

        var targets = _connections()
            .Where(connection => connection.IsReady &&
                connection.Definition.MatrixDimensions is not null &&
                connection.Capabilities.Contains(OpenRazerBackendCapability.MatrixFrameWrite) &&
                _lightingEnabled(connection) &&
                _chromaOverrideEnabled(connection))
            .ToArray();
        if (targets.Length == 0)
        {
            return false;
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            foreach (var target in targets)
            {
                var dimensions = target.Definition.MatrixDimensions!;
                var frame = Resize(sourceFrame, dimensions.Rows, dimensions.Columns)
                    .Select(color => new OpenRazerColor(color.Red, color.Green, color.Blue))
                    .ToArray();
                await _service.SetCustomFrameAsync(target, frame, cancellationToken).ConfigureAwait(false);
            }
            return true;
        }
        finally
        {
            _gate.Release();
        }
    }

    private static IReadOnlyList<RazerRgb> Resize(
        IReadOnlyList<RazerRgb> source,
        int rows,
        int columns)
    {
        var result = new RazerRgb[rows * columns];
        for (var row = 0; row < rows; row++)
        {
            var sourceRow = rows == 1 ? 0 : (int)Math.Round(row * (SourceRows - 1d) / (rows - 1d));
            for (var column = 0; column < columns; column++)
            {
                var sourceColumn = columns == 1
                    ? 0
                    : (int)Math.Round(column * (SourceColumns - 1d) / (columns - 1d));
                result[row * columns + column] = source[sourceRow * SourceColumns + sourceColumn];
            }
        }
        return result;
    }
}
