using OpenSynapse.Core.Devices;
using OpenSynapse.Core.Lighting;
using OpenSynapse.Windows.Devices;
using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.Windows.Lighting;

/// <summary>How a Blade source turns its logical canvas into the HID frame.</summary>
public enum StudioBladeFrameMode
{
    /// <summary>7 x 16 logical canvas mapped through the sparse 710 layout to 6 x 17.</summary>
    Sparse710,

    /// <summary>7 x 15 logical canvas mapped through the 2024 sparse layout to 6 x 16.</summary>
    Sparse16,

    /// <summary>Render the manifest matrix and use its exact LED table for the device frame.</summary>
    Sparse,

    /// <summary>Render the manifest matrix directly; no 710 sparse map.</summary>
    Direct,
}

/// <summary>The matrix size and frame mapping one Blade lighting dialect needs.</summary>
public readonly record struct StudioBladeFrameProfile(
    StudioBladeFrameMode Mode,
    int Rows,
    int Columns);

/// <summary>Resolves a connected Blade descriptor to its Studio frame contract.</summary>
public static class StudioBladeDevices
{
    /// <summary>
    /// Returns null for a device without a software matrix (FirmwareEffect/Unknown) or
    /// without an available manifest, so Studio never lists a device it cannot write.
    /// </summary>
    public static StudioBladeFrameProfile? Resolve(DeviceDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        if (descriptor.Access != DeviceAccessState.Available) return null;
        var manifest = RazerDeviceRegistry.BuiltIn.Find(descriptor.VendorId, descriptor.ProductId);
        if (manifest is null) return null;

        // Match BladeLightingController.FindReadyBlade: a firmware downgrade or a
        // mismatched HID collection would make Apply fail, so do not list the device.
        if (descriptor.Capability != DeviceCapabilityState.PendingValidation ||
            descriptor.FeatureReportByteLength != manifest.Collection.FeatureReportLength ||
            descriptor.UsagePage != manifest.Collection.UsagePage ||
            descriptor.Usage != manifest.Collection.Usage)
        {
            return null;
        }

        var dialect = BladeLightingRuntimeDialect.Get(descriptor.ProductId, manifest.LightingDialect);

        if (dialect == BladeLightingDialect.LedMatrix710)
        {
            if (manifest.MatrixColumns == BladeLightingLayout16.DeviceColumns)
            {
                return new(
                    StudioBladeFrameMode.Sparse16,
                    BladeLightingLayout16.LogicalRows,
                    BladeLightingLayout16.LogicalColumns);
            }

            if (manifest.MatrixColumns == 19)
            {
                return new(StudioBladeFrameMode.Sparse, 8, 18);
            }

            return new(
                StudioBladeFrameMode.Sparse710,
                BladeLightingLayout.LogicalRows,
                BladeLightingLayout.LogicalColumns);
        }

        if (dialect == BladeLightingDialect.LedMatrix2026)
        {
            if (manifest.MatrixColumns == 17)
            {
                return new(StudioBladeFrameMode.Sparse, 7, 16);
            }

            if (manifest.MatrixColumns == 19)
            {
                return new(StudioBladeFrameMode.Sparse, 6, 19);
            }
        }

        if (dialect == BladeLightingDialect.LedMatrixBlade)
        {
            // Older Blade canvases are 6 x 15 or 7 x 15 with a 6 x 16 device
            // frame. The runtime manifest only carries the device geometry.
            var (logicalRows, logicalColumns) = descriptor.ProductId switch
            {
                0x0234 or 0x023A or 0x024B or 0x024C or 0x024D => (6, 15),
                0x0253 or 0x0256 or 0x026D or 0x026E or 0x0270 or 0x0276 or 0x0279 => (7, 15),
                _ => (manifest.MatrixRows, manifest.MatrixColumns),
            };
            return logicalColumns == manifest.MatrixColumns
                ? new(StudioBladeFrameMode.Direct, logicalRows, logicalColumns)
                : new(StudioBladeFrameMode.Sparse, logicalRows, logicalColumns);
        }

        if (!IsSoftwareMatrix(dialect)) return null;
        var rows = manifest.MatrixRows;
        var columns = manifest.MatrixColumns;
        if (rows == 0 || columns == 0)
        {
            // Some 023A manifests omit geometry; its protocol is the authority for
            // those reports, matching the controller's own fallback.
            if (dialect != BladeLightingDialect.LedMatrixBlade) return null;
            rows = BladeLightingLedMatrixBladeProtocol.Rows;
            columns = BladeLightingLedMatrixBladeProtocol.Columns;
        }

        return new(StudioBladeFrameMode.Direct, rows, columns);
    }

    private static bool IsSoftwareMatrix(BladeLightingDialect dialect) =>
        dialect is BladeLightingDialect.LedMatrix710 or BladeLightingDialect.LedMatrixBlade or
            BladeLightingDialect.LedMatrixBlade2 or BladeLightingDialect.LedMatrix2026;
}

/// <summary>
/// V2 frame source: every layer keeps its own playback clock, mask and effect
/// parameters, then the composite is mapped onto the Blade frame. Manifests with
/// a live LED table use that exact map; the 710 fallback keeps 7 x 16 -> 6 x 17.
/// </summary>
public sealed class StudioBladeFrameSourceV2 : ISoftwareLightingFrameSource
{
    private readonly object _gate = new();
    private readonly Dictionary<Guid, StudioPlaybackClock> _clocks = new();
    private readonly Func<int, int, StudioEffectInput>? _inputProvider;
    private readonly StudioPlaybackTrigger _trigger;
    private readonly StudioBladeFrameMode _mode;
    private readonly int _rows;
    private readonly int _columns;
    private readonly StudioLedLayout? _layout;
    private StudioLayerRender[] _layers = [];

    public StudioBladeFrameSourceV2()
        : this(
            StudioBladeFrameMode.Sparse710,
            BladeLightingLayout.LogicalRows,
            BladeLightingLayout.LogicalColumns)
    {
    }

    public StudioBladeFrameSourceV2(Func<int, int, StudioEffectInput>? inputProvider)
        : this(
            StudioBladeFrameMode.Sparse710,
            BladeLightingLayout.LogicalRows,
            BladeLightingLayout.LogicalColumns,
            inputProvider)
    {
    }

    public StudioBladeFrameSourceV2(
        StudioBladeFrameMode mode,
        int rows,
        int columns,
        Func<int, int, StudioEffectInput>? inputProvider = null,
        StudioLedLayout? layout = null)
    {
        if (!Enum.IsDefined(mode)) throw new ArgumentOutOfRangeException(nameof(mode));
        if (rows < 1) throw new ArgumentOutOfRangeException(nameof(rows));
        if (columns < 1) throw new ArgumentOutOfRangeException(nameof(columns));
        if ((long)rows * columns > StudioCompositor.MaxPixels)
        {
            throw new ArgumentOutOfRangeException(nameof(rows), "The frame is outside the supported range.");
        }

        if (mode == StudioBladeFrameMode.Sparse710 &&
            (rows != BladeLightingLayout.LogicalRows || columns != BladeLightingLayout.LogicalColumns))
        {
            throw new ArgumentException("The sparse 710 canvas is 7 x 16.", nameof(rows));
        }
        if (mode == StudioBladeFrameMode.Sparse16 &&
            (rows != BladeLightingLayout16.LogicalRows || columns != BladeLightingLayout16.LogicalColumns))
        {
            throw new ArgumentException("The sparse 2024 canvas is 7 x 15.", nameof(rows));
        }
        if (layout is not null &&
            (layout.MatrixRows != rows || layout.MatrixColumns != columns))
        {
            throw new ArgumentException("The manifest layout does not match the canvas.", nameof(layout));
        }
        if (mode == StudioBladeFrameMode.Sparse && layout is null)
        {
            throw new ArgumentException("Sparse Blade frames require the manifest LED table.", nameof(layout));
        }

        _mode = mode;
        _rows = rows;
        _columns = columns;
        _layout = layout;
        _inputProvider = inputProvider;
        _trigger = new StudioPlaybackTrigger(GetClock);
    }

    public void Update(IEnumerable<StudioLayerRender> layers)
    {
        ArgumentNullException.ThrowIfNull(layers);
        var snapshot = layers.ToArray();
        _trigger.Update(snapshot, _rows, _columns);
        Volatile.Write(ref _layers, snapshot);
    }

    /// <summary>
    /// Applies one key transition in matrix coordinates. The trigger shares this
    /// source's clocks, so rendered frames see the same playback state.
    /// </summary>
    public int HandleInput(StudioTriggerInput input, double nowSeconds) =>
        _trigger.Handle(input, nowSeconds);

    public ValueTask<IReadOnlyList<RazerRgb>> RenderAsync(
        TimeSpan elapsed,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var layers = Volatile.Read(ref _layers);
        var now = Math.Max(0, elapsed.TotalSeconds);
        var timed = new (StudioLayerRender Layer, double Seconds)[layers.Length];
        for (var index = 0; index < layers.Length; index++)
        {
            var layer = layers[index];
            var clock = GetClock(layer.Id);
            var state = clock.Sample(now, layer.Params, _rows, _columns);
            // Speed is an effect parameter interpreted by the renderer; time stays absolute.
            timed[index] = (layer with { Opacity = state.Running ? layer.Opacity : 0 }, state.LocalSeconds);
        }

        var input = _inputProvider?.Invoke(_rows, _columns) ?? default;
        var logical = StudioCompositor.RenderTimedLayers(timed, _rows, _columns, input);
        var colors = new RazerRgb[logical.Length];
        for (var index = 0; index < logical.Length; index++)
        {
            colors[index] = new RazerRgb(logical[index].R, logical[index].G, logical[index].B);
        }

        if (_layout is not null)
        {
            var device = new RazerRgb[_layout.DeviceRows * _layout.DeviceColumns];
            foreach (var led in _layout.Leds)
            {
                if ((uint)led.MatrixRow >= (uint)_rows ||
                    (uint)led.MatrixCol >= (uint)_columns ||
                    (uint)led.DeviceRow >= (uint)_layout.DeviceRows ||
                    (uint)led.DeviceCol >= (uint)_layout.DeviceColumns)
                {
                    continue;
                }

                device[led.DeviceRow * _layout.DeviceColumns + led.DeviceCol] =
                    colors[led.MatrixRow * _columns + led.MatrixCol];
            }

            return ValueTask.FromResult<IReadOnlyList<RazerRgb>>(device);
        }

        return ValueTask.FromResult<IReadOnlyList<RazerRgb>>(_mode switch
        {
            StudioBladeFrameMode.Sparse710 => BladeLightingLayout.MapToDeviceFrame(colors),
            StudioBladeFrameMode.Sparse16 => BladeLightingLayout16.MapToDeviceFrame(colors),
            _ => colors,
        });
    }

    private StudioPlaybackClock GetClock(Guid id)
    {
        lock (_gate)
        {
            if (_clocks.TryGetValue(id, out var clock)) return clock;
            var phase = id == Guid.Empty ? 0 : (id.GetHashCode() & 0x7FFF) / 32768d;
            clock = new StudioPlaybackClock(phase);
            _clocks[id] = clock;
            return clock;
        }
    }
}
