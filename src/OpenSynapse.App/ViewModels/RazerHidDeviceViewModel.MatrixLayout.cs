using Microsoft.UI.Dispatching;
using OpenSynapse.Core.Lighting;
using OpenSynapse.Windows.Devices;
using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.App.ViewModels;

public sealed partial class RazerHidDeviceViewModel
{
    private Func<ushort, CancellationToken, Task<StudioLedLayout?>>? _matrixLayoutLoader;
    private DispatcherQueue? _dispatcherQueue;
    private readonly CancellationTokenSource _matrixLayoutCancellation = new();
    private int _matrixColumns;

    public int MatrixColumns => _matrixColumns > 0
        ? _matrixColumns
        : Connection.Definition.MatrixDimensions?.Columns ?? 1;

    internal void AttachMatrixLayout(
        Func<ushort, CancellationToken, Task<StudioLedLayout?>> loader,
        DispatcherQueue? dispatcherQueue)
    {
        _matrixLayoutLoader = loader;
        _dispatcherQueue = dispatcherQueue;
        _ = LoadMatrixLayoutAsync();
    }

    private void InitializeMatrixEditor()
    {
        if (Connection.Definition.MatrixDimensions is not { } matrix ||
            !Connection.Capabilities.Contains(RazerHidBackendCapability.MatrixFrameWrite))
        {
            return;
        }

        _matrixColumns = matrix.Columns;
        foreach (var cell in StudioMatrixEditorLayout.BuildUniform(matrix.Rows, matrix.Columns))
        {
            MatrixCells.Add(new RazerHidMatrixCellViewModel(cell.DevicePositions));
        }
    }

    private void DisposeMatrixEditor()
    {
        _matrixLayoutCancellation.Cancel();
        _matrixLayoutCancellation.Dispose();
    }

    public Task ApplyMatrixAsync(CancellationToken cancellationToken = default) =>
        RunWriteAsync(RazerHidBackendCapability.MatrixFrameWrite,
            () => IsMatrixBusy, value => IsMatrixBusy = value,
            async () =>
            {
                var matrix = Connection.Definition.MatrixDimensions
                    ?? throw new NotSupportedException("Matrix dimensions are not declared.");
                var frame = new RazerHidColor[matrix.Rows * matrix.Columns];
                var touchedRows = new SortedSet<byte>();
                foreach (var cell in MatrixCells)
                {
                    var color = ToRazerHidColor(cell.Color);
                    foreach (var (deviceRow, deviceColumn) in cell.DevicePositions)
                    {
                        if (deviceRow < 0 || deviceRow >= matrix.Rows ||
                            deviceColumn < 0 || deviceColumn >= matrix.Columns)
                        {
                            continue;
                        }

                        frame[deviceRow * matrix.Columns + deviceColumn] = color;
                        touchedRows.Add((byte)deviceRow);
                    }
                }

                foreach (var row in touchedRows)
                {
                    var colors = frame.AsSpan(row * matrix.Columns, matrix.Columns).ToArray();
                    await _service.SetCustomRowAsync(Connection, row, 0, colors, cancellationToken);
                }
            }, cancellationToken);

    /// <summary>
    /// Replaces the packed device grid with the manifest's visual matrix once it loads.
    /// The uniform grid stays in place when the manifest is unavailable.
    /// </summary>
    private async Task LoadMatrixLayoutAsync()
    {
        if (_matrixLayoutLoader is null ||
            Connection.Definition.MatrixDimensions is not { } matrix ||
            !Connection.Capabilities.Contains(RazerHidBackendCapability.MatrixFrameWrite))
        {
            return;
        }

        try
        {
            var layout = await _matrixLayoutLoader(
                Connection.Definition.ProductId,
                _matrixLayoutCancellation.Token).ConfigureAwait(false);
            if (layout is null || layout.Leds.Count == 0) return;
            if (layout.DeviceRows != matrix.Rows || layout.DeviceColumns != matrix.Columns) return;
            var cells = StudioMatrixEditorLayout.Build(layout);
            if (!cells.Any(cell => !cell.IsPlaceholder)) return;

            void Apply()
            {
                _matrixColumns = layout.MatrixColumns > 0 ? layout.MatrixColumns : matrix.Columns;
                MatrixCells.Clear();
                foreach (var cell in cells)
                {
                    MatrixCells.Add(new RazerHidMatrixCellViewModel(cell.DevicePositions));
                }

                OnPropertyChanged(nameof(MatrixColumns));
            }

            if (_dispatcherQueue is { } dispatcher)
            {
                if (!dispatcher.TryEnqueue(Apply)) return;
            }
            else
            {
                Apply();
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception) when (exception is HttpRequestException or IOException or
            InvalidDataException or FormatException or NotSupportedException or ArgumentException or
            ObjectDisposedException)
        {
            // Keep the uniform grid; the editor remains usable without a manifest.
        }
    }
}
