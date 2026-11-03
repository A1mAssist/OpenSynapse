using System.Diagnostics;
using OpenSynapse.Core.Lighting;
using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.Windows.Lighting;

/// <summary>
/// Samples the display and system audio once per studio frame and exposes the
/// result as <see cref="StudioEffectInput"/>.
/// </summary>
internal sealed class StudioInputSampler : ILightingInputAdapter
{
    private const int FramePollMilliseconds = 16;
    private readonly WindowsDisplayCaptureAdapter _display;
    private readonly WasapiAudioMeterAdapter _audio;
    private readonly SemaphoreSlim _lifecycle = new(1, 1);
    private readonly object _audioGate = new();
    private CancellationTokenSource? _stop;
    private Task? _framePump;
    private AmbientCaptureFrame? _latestFrame;
    private bool _started;
    private long _lastAudioTimestamp;
    private double _audioLevel;
    private int _disposed;

    internal StudioInputSampler()
        : this(new WindowsDisplayCaptureAdapter(), new WasapiAudioMeterAdapter())
    {
    }

    internal StudioInputSampler(
        WindowsDisplayCaptureAdapter display,
        WasapiAudioMeterAdapter audio)
    {
        _display = display ?? throw new ArgumentNullException(nameof(display));
        _audio = audio ?? throw new ArgumentNullException(nameof(audio));
    }

    /// <summary>The last level produced by <see cref="Sample"/>; silent input is zero.</summary>
    public double AudioLevel => Volatile.Read(ref _audioLevel);

    public async ValueTask StartAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        await _lifecycle.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
            if (Volatile.Read(ref _started))
            {
                throw new InvalidOperationException("Studio input sampling is already running.");
            }

            var stop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _stop = stop;
            Volatile.Write(ref _latestFrame, null);
            lock (_audioGate)
            {
                _lastAudioTimestamp = 0;
                Volatile.Write(ref _audioLevel, 0);
            }
            Volatile.Write(ref _started, true);
            try
            {
                await _audio.StartAsync(stop.Token).ConfigureAwait(false);
                var displayStarted = false;
                try
                {
                    await _display.StartAsync(stop.Token).ConfigureAwait(false);
                    displayStarted = true;
                }
                catch (AmbientCaptureException)
                {
                    // Capture is optional; the screen sampler falls back to black.
                }

                if (displayStarted)
                {
                    _framePump = Task.Run(
                        () => PumpFramesAsync(stop.Token),
                        CancellationToken.None);
                }
            }
            catch
            {
                await StopCoreAsync().ConfigureAwait(false);
                throw;
            }
        }
        finally
        {
            _lifecycle.Release();
        }
    }

    public async ValueTask StopAsync()
    {
        // Cancel before waiting so a Start blocked on the capture permission prompt can finish.
        try
        {
            Volatile.Read(ref _stop)?.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // A concurrent stop already completed.
        }

        await _lifecycle.WaitAsync().ConfigureAwait(false);
        try
        {
            await StopCoreAsync().ConfigureAwait(false);
        }
        finally
        {
            _lifecycle.Release();
        }
    }

    /// <summary>
    /// Takes one screen and audio snapshot for a matrix. The returned sampler
    /// indexes the snapshot; it never touches the capture adapters.
    /// </summary>
    public StudioEffectInput Sample(int rows, int columns)
    {
        var pixels = CreateSnapshot(Volatile.Read(ref _latestFrame), rows, columns);
        var level = ReadAudioLevel();
        return new StudioEffectInput(
            level,
            (column, row) => pixels[row * columns + column],
            (u, v) => SampleUv(pixels, rows, columns, u, v));
    }

    /// <summary>
    /// Bilinearly samples a matrix snapshot at a normalized 0..1 position.
    /// The snapshot array is never mutated, so concurrent reads are safe.
    /// </summary>
    internal static StudioRgb SampleUv(
        StudioRgb[] pixels,
        int rows,
        int columns,
        double u,
        double v)
    {
        ArgumentNullException.ThrowIfNull(pixels);
        ValidateTarget(rows, columns);
        if (pixels.Length != rows * columns)
        {
            throw new ArgumentException(
                "The matrix dimensions do not match its pixel count.",
                nameof(pixels));
        }

        var sourceX = ClampUnit(u) * columns - 0.5;
        var sourceY = ClampUnit(v) * rows - 0.5;
        var x0 = (int)Math.Floor(sourceX);
        var y0 = (int)Math.Floor(sourceY);
        var fx = sourceX - x0;
        var fy = sourceY - y0;
        var x1 = Math.Clamp(x0 + 1, 0, columns - 1);
        var y1 = Math.Clamp(y0 + 1, 0, rows - 1);
        x0 = Math.Clamp(x0, 0, columns - 1);
        y0 = Math.Clamp(y0, 0, rows - 1);

        var topLeft = pixels[y0 * columns + x0];
        var topRight = pixels[y0 * columns + x1];
        var bottomLeft = pixels[y1 * columns + x0];
        var bottomRight = pixels[y1 * columns + x1];
        return new StudioRgb(
            Blend(topLeft.R, topRight.R, bottomLeft.R, bottomRight.R, fx, fy),
            Blend(topLeft.G, topRight.G, bottomLeft.G, bottomRight.G, fx, fy),
            Blend(topLeft.B, topRight.B, bottomLeft.B, bottomRight.B, fx, fy));
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
        {
            await StopAsync().ConfigureAwait(false);
            await _display.DisposeAsync().ConfigureAwait(false);
            await _audio.DisposeAsync().ConfigureAwait(false);
        }
    }

    internal static StudioRgb[] CreateSnapshot(AmbientCaptureFrame? frame, int rows, int columns)
    {
        ValidateTarget(rows, columns);
        return frame is null
            ? new StudioRgb[rows * columns]
            : ResizeFrame(frame.Pixels, frame.Width, frame.Height, rows, columns);
    }

    internal static StudioRgb[] ResizeFrame(
        IReadOnlyList<RazerRgb> pixels,
        int width,
        int height,
        int rows,
        int columns)
    {
        ArgumentNullException.ThrowIfNull(pixels);
        ValidateTarget(rows, columns);
        if (width <= 0) throw new ArgumentOutOfRangeException(nameof(width));
        if (height <= 0) throw new ArgumentOutOfRangeException(nameof(height));
        if (pixels.Count != checked(width * height))
        {
            throw new ArgumentException("The frame dimensions do not match its pixel count.", nameof(pixels));
        }

        var result = new StudioRgb[rows * columns];
        for (var row = 0; row < rows; row++)
        {
            var sourceY = (row + 0.5) * height / rows - 0.5;
            var y0 = (int)Math.Floor(sourceY);
            var fy = sourceY - y0;
            var y1 = Math.Clamp(y0 + 1, 0, height - 1);
            y0 = Math.Clamp(y0, 0, height - 1);
            for (var column = 0; column < columns; column++)
            {
                var sourceX = (column + 0.5) * width / columns - 0.5;
                var x0 = (int)Math.Floor(sourceX);
                var fx = sourceX - x0;
                var x1 = Math.Clamp(x0 + 1, 0, width - 1);
                x0 = Math.Clamp(x0, 0, width - 1);

                var topLeft = pixels[y0 * width + x0];
                var topRight = pixels[y0 * width + x1];
                var bottomLeft = pixels[y1 * width + x0];
                var bottomRight = pixels[y1 * width + x1];
                result[row * columns + column] = new StudioRgb(
                    Blend(topLeft.Red, topRight.Red, bottomLeft.Red, bottomRight.Red, fx, fy),
                    Blend(topLeft.Green, topRight.Green, bottomLeft.Green, bottomRight.Green, fx, fy),
                    Blend(topLeft.Blue, topRight.Blue, bottomLeft.Blue, bottomRight.Blue, fx, fy));
            }
        }

        return result;
    }

    internal static double SmoothLevel(double previous, double current, TimeSpan elapsed) =>
        QuickLightingEngine.SmoothAudioLevel(
            WasapiAudioMeterAdapter.NormalizeLevel(previous),
            WasapiAudioMeterAdapter.NormalizeLevel(current),
            elapsed);

    private async Task PumpFramesAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var frame = await _display.ReadFrameAsync(cancellationToken).ConfigureAwait(false);
                if (!ReferenceEquals(frame, Volatile.Read(ref _latestFrame)))
                {
                    Volatile.Write(ref _latestFrame, frame);
                    continue;
                }

                await Task.Delay(FramePollMilliseconds, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception)
        {
            // Screen capture is optional and must not fail the lighting loop.
            Volatile.Write(ref _latestFrame, null);
        }
    }

    private double ReadAudioLevel()
    {
        lock (_audioGate)
        {
            if (!Volatile.Read(ref _started))
            {
                return 0;
            }

            double current;
            try
            {
                current = _audio.ReadLevel();
            }
            catch (InvalidOperationException)
            {
                return 0;
            }

            var now = Stopwatch.GetTimestamp();
            var elapsed = _lastAudioTimestamp == 0
                ? TimeSpan.Zero
                : Stopwatch.GetElapsedTime(_lastAudioTimestamp, now);
            _lastAudioTimestamp = now;
            var level = SmoothLevel(_audioLevel, current, elapsed);
            Volatile.Write(ref _audioLevel, level);
            return level;
        }
    }

    private async Task StopCoreAsync()
    {
        var stop = _stop;
        var pump = _framePump;
        _stop = null;
        _framePump = null;
        Volatile.Write(ref _started, false);
        lock (_audioGate)
        {
            _lastAudioTimestamp = 0;
            Volatile.Write(ref _audioLevel, 0);
        }
        Volatile.Write(ref _latestFrame, null);
        stop?.Cancel();
        try
        {
            if (pump is not null)
            {
                try
                {
                    await pump.ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (stop?.IsCancellationRequested ?? false)
                {
                }
            }

            Volatile.Write(ref _latestFrame, null);
            try
            {
                await _display.StopAsync().ConfigureAwait(false);
            }
            catch (AmbientCaptureException)
            {
            }

            await _audio.StopAsync().ConfigureAwait(false);
        }
        finally
        {
            stop?.Dispose();
        }
    }

    private static void ValidateTarget(int rows, int columns)
    {
        if (rows <= 0) throw new ArgumentOutOfRangeException(nameof(rows));
        if (columns <= 0) throw new ArgumentOutOfRangeException(nameof(columns));
        if ((long)rows * columns > StudioCompositor.MaxPixels)
        {
            throw new ArgumentOutOfRangeException(
                nameof(columns),
                "The matrix is larger than the supported canvas.");
        }
    }

    private static double ClampUnit(double value) =>
        double.IsFinite(value) ? Math.Clamp(value, 0, 1) : 0;

    private static byte Blend(
        byte topLeft,
        byte topRight,
        byte bottomLeft,
        byte bottomRight,
        double x,
        double y)
    {
        var top = topLeft + (topRight - topLeft) * x;
        var bottom = bottomLeft + (bottomRight - bottomLeft) * x;
        return (byte)Math.Clamp(Math.Round(top + (bottom - top) * y), 0, 255);
    }
}