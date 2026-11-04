#if DEBUG
using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using Microsoft.UI.Xaml.Media;
using OpenSynapse.Core.Lighting;

namespace OpenSynapse.App.Views;

/// <summary>Debug-only stress entry for the real XAML LED canvas.</summary>
internal sealed class StudioCanvasBenchmark
{
    private const double CellSize = 10;
    private const double CellGap = 3;
    private const double Margin = 8;
    private const string InstanceId = "studio-benchmark";
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly StudioCanvas _canvas;
    private readonly int _ledCount;
    private readonly int _columns;
    private readonly int _rows;
    private readonly int _targetFps;
    private readonly int _durationSeconds;
    private readonly string _outputPath;
    private readonly Stopwatch _clock = new();
    private readonly List<double> _applyMilliseconds = new();
    private readonly List<double> _renderIntervalsMilliseconds = new();
    private readonly EventHandler<object> _renderingHandler;
    private StudioRgb[][] _frames = [];
    private double _setupMilliseconds;
    private double _lastRenderMilliseconds = -1;
    private long _allocatedBefore;
    private int _lastSlot;
    private int _appliedFrames;
    private int _missedSlots;
    private int _lateTicks;
    private bool _measuring;
    private bool _finished;
    private DateTimeOffset _startedAt;

    private StudioCanvasBenchmark(
        StudioCanvas canvas,
        int ledCount,
        int targetFps,
        int durationSeconds,
        string outputPath)
    {
        _canvas = canvas;
        _ledCount = ledCount;
        _columns = 50;
        _rows = (ledCount + _columns - 1) / _columns;
        _targetFps = targetFps;
        _durationSeconds = durationSeconds;
        _outputPath = outputPath;
        _renderingHandler = OnRendering;
    }

    public event EventHandler? Completed;

    public static bool TryCreate(StudioCanvas canvas, out StudioCanvasBenchmark? benchmark)
    {
        benchmark = null;
        var countText = Environment.GetEnvironmentVariable("OPENSSYNAPSE_STUDIO_BENCH");
        if (string.IsNullOrWhiteSpace(countText)) return false;
        if (!int.TryParse(countText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var count) ||
            count is < 100 or > 5000)
        {
            return false;
        }

        var fpsText = Environment.GetEnvironmentVariable("OPENSSYNAPSE_STUDIO_BENCH_FPS");
        var fps = int.TryParse(fpsText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var requestedFps)
            ? requestedFps
            : 60;
        if (fps is < 1 or > 240) return false;

        var secondsText = Environment.GetEnvironmentVariable("OPENSSYNAPSE_STUDIO_BENCH_SECONDS");
        var seconds = int.TryParse(secondsText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var requestedSeconds)
            ? requestedSeconds
            : 8;
        if (seconds is < 2 or > 1800) return false;

        var outputPath = Environment.GetEnvironmentVariable("OPENSSYNAPSE_STUDIO_BENCH_OUT");
        if (string.IsNullOrWhiteSpace(outputPath))
        {
            outputPath = Path.Combine(
                Path.GetTempPath(),
                $"opensynapse-studio-bench-{count}-{fps}.json");
        }

        benchmark = new StudioCanvasBenchmark(canvas, count, fps, seconds, Path.GetFullPath(outputPath));
        return true;
    }

    public void Start()
    {
        if (_measuring || _finished) return;
        try
        {
            var setup = Stopwatch.StartNew();
            var visual = CreateVisual();
            _frames = BuildFrames(_targetFps * 2);
            _canvas.Devices = new[] { visual };
            _canvas.Profile = null;
            _canvas.Refresh();
            _canvas.UpdateLayout();
            _canvas.FitToView();
            _canvas.SetFrames(CreateFrameSet(0));
            _setupMilliseconds = setup.Elapsed.TotalMilliseconds;
            _canvas.DispatcherQueue.TryEnqueue(() =>
            {
                _canvas.FitToView();
                _canvas.DebugCenterFirstHost();
                var warmup = _canvas.DispatcherQueue.CreateTimer();
                warmup.Interval = TimeSpan.FromSeconds(1);
                warmup.IsRepeating = false;
                warmup.Tick += (_, _) =>
                {
                    warmup.Stop();
                    _canvas.DispatcherQueue.TryEnqueue(BeginMeasurement);
                };
                warmup.Start();
            });
        }
        catch (Exception exception)
        {
            Finish("failed", exception.ToString());
        }
    }

    private void BeginMeasurement()
    {
        if (_measuring || _finished) return;
        _canvas.SetFrames(CreateFrameSet(0));
        _startedAt = DateTimeOffset.Now;
        _appliedFrames = 1;
        _clock.Restart();
        _allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        _measuring = true;
        CompositionTarget.Rendering += _renderingHandler;
    }

    public void Stop() => Finish("cancelled", null);

    private void OnRendering(object? sender, object args)
    {
        if (!_measuring) return;
        try
        {
            var milliseconds = _clock.Elapsed.TotalMilliseconds;
            if (_lastRenderMilliseconds >= 0)
            {
                _renderIntervalsMilliseconds.Add(milliseconds - _lastRenderMilliseconds);
            }

            _lastRenderMilliseconds = milliseconds;
            var seconds = milliseconds / 1000;
            if (seconds >= _durationSeconds)
            {
                Finish("completed", null);
                return;
            }

            var slot = (int)Math.Floor(seconds * _targetFps);
            if (slot <= _lastSlot) return;
            if (slot - _lastSlot > 1)
            {
                _missedSlots += slot - _lastSlot - 1;
                _lateTicks++;
            }

            var start = Stopwatch.GetTimestamp();
            _canvas.SetFrames(CreateFrameSet(slot));
            _applyMilliseconds.Add(Stopwatch.GetElapsedTime(start).TotalMilliseconds);
            _lastSlot = slot;
            _appliedFrames++;
        }
        catch (Exception exception)
        {
            Finish("failed", exception.ToString());
        }
    }

    private IReadOnlyDictionary<string, StudioDeviceFrame> CreateFrameSet(int slot)
    {
        var frame = new StudioDeviceFrame(
            _rows,
            _columns,
            _frames[(slot % _frames.Length + _frames.Length) % _frames.Length]);
        return new Dictionary<string, StudioDeviceFrame>(StringComparer.OrdinalIgnoreCase)
        {
            [InstanceId] = frame,
        };
    }

    private StudioDeviceVisual CreateVisual()
    {
        var width = Margin * 2 + _columns * CellSize + (_columns - 1) * CellGap;
        var height = Margin * 2 + _rows * CellSize + (_rows - 1) * CellGap;
        var paths = new StudioVisualPath[_ledCount];
        var leds = new StudioVisualLed[_ledCount];
        for (var index = 0; index < _ledCount; index++)
        {
            var row = index / _columns;
            var column = index % _columns;
            var x = Margin + column * (CellSize + CellGap);
            var y = Margin + row * (CellSize + CellGap);
            var id = $"bench-led-{index}";
            var data = FormattableString.Invariant(
                $"M {x},{y} H {x + CellSize} V {y + CellSize} H {x} Z");
            paths[index] = new StudioVisualPath(
                id,
                data,
                "#000000",
                null,
                1,
                StudioSvgRole.Led);
            leds[index] = new StudioVisualLed(row, column, id, null);
        }

        return new StudioDeviceVisual(
            InstanceId,
            0,
            $"Studio benchmark {_ledCount}",
            width,
            height,
            paths,
            leds,
            UsesSvg: false);
    }

    private StudioRgb[][] BuildFrames(int frameCount)
    {
        var count = Math.Max(2, frameCount);
        var frames = new StudioRgb[count][];
        for (var frameIndex = 0; frameIndex < count; frameIndex++)
        {
            var frame = new StudioRgb[_rows * _columns];
            for (var row = 0; row < _rows; row++)
            {
                for (var column = 0; column < _columns; column++)
                {
                    var hue = column / (double)_columns * 0.65 +
                              frameIndex / (double)count * 0.3 +
                              row / (double)_rows * 0.05;
                    frame[row * _columns + column] = StudioRgb.FromHsv(hue % 1, 1, 1);
                }
            }

            frames[frameIndex] = frame;
        }

        return frames;
    }

    private void Finish(string status, string? error)
    {
        if (_finished) return;
        _finished = true;
        _measuring = false;
        CompositionTarget.Rendering -= _renderingHandler;

        var elapsedSeconds = _clock.Elapsed.TotalSeconds;
        var expectedSlots = Math.Max(0, (int)Math.Floor(elapsedSeconds * _targetFps));
        var droppedFrames = Math.Max(_missedSlots, Math.Max(0, expectedSlots - _appliedFrames));
        var applyStats = TimingStats.From(_applyMilliseconds);
        var renderStats = TimingStats.From(_renderIntervalsMilliseconds);
        var allocated = Math.Max(0, GC.GetAllocatedBytesForCurrentThread() - _allocatedBefore);
        var measuredFrames = Math.Max(1, _appliedFrames - 1);
        var report = new
        {
            status,
            error,
            startedAt = _startedAt.ToString("o", CultureInfo.InvariantCulture),
            targetFps = _targetFps,
            durationSeconds = _durationSeconds,
            elapsedSeconds,
            ledCount = _ledCount,
            pathCount = _ledCount,
            matrixRows = _rows,
            matrixColumns = _columns,
            setupMilliseconds = _setupMilliseconds,
            expectedSlots,
            appliedFrames = _appliedFrames,
            droppedFrames,
            lateTicks = _lateTicks,
            deliveredFps = elapsedSeconds <= 0 ? 0 : _appliedFrames / elapsedSeconds,
            uiThreadCpuPercent = elapsedSeconds <= 0
                ? 0
                : applyStats.TotalMilliseconds / (elapsedSeconds * 1000) * 100,
            allocatedBytesPerMeasuredFrame = allocated / (double)measuredFrames,
            applyMilliseconds = applyStats,
            renderCallbacks = new
            {
                count = _renderIntervalsMilliseconds.Count,
                intervals = renderStats,
                raw = _renderIntervalsMilliseconds,
            },
            rawApplyMilliseconds = _applyMilliseconds,
            canvasWidth = _canvas.ActualWidth,
            canvasHeight = _canvas.ActualHeight,
            debugHostCount = _canvas.DebugHostCount,
            debugLedCount = _canvas.DebugLedCount,
            debugFirstLedColor = _canvas.DebugFirstLedColor,
            debugCanvasState = _canvas.DebugCanvasState(),
            zoomPercent = _canvas.ZoomPercent,
            runtime = Environment.Version.ToString(),
            processorCount = Environment.ProcessorCount,
        };

        try
        {
            var directory = Path.GetDirectoryName(_outputPath);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
            var temporary = _outputPath + ".tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(report, JsonOptions));
            File.Move(temporary, _outputPath, overwrite: true);
        }
        catch (Exception exception)
        {
            Debug.WriteLine($"Studio benchmark report failed: {exception}");
        }

        Completed?.Invoke(this, EventArgs.Empty);
    }

    private sealed record TimingStats(
        int Count,
        double TotalMilliseconds,
        double MeanMilliseconds,
        double P50Milliseconds,
        double P95Milliseconds,
        double P99Milliseconds,
        double MaxMilliseconds)
    {
        public static TimingStats From(IReadOnlyList<double> values)
        {
            if (values.Count == 0)
            {
                return new TimingStats(0, 0, 0, 0, 0, 0, 0);
            }

            var sorted = values.OrderBy(value => value).ToArray();
            return new TimingStats(
                sorted.Length,
                sorted.Sum(),
                sorted.Average(),
                Percentile(sorted, 0.50),
                Percentile(sorted, 0.95),
                Percentile(sorted, 0.99),
                sorted[^1]);
        }

        private static double Percentile(IReadOnlyList<double> sorted, double percentile)
        {
            var index = (int)Math.Ceiling(percentile * sorted.Count) - 1;
            return sorted[Math.Clamp(index, 0, sorted.Count - 1)];
        }
    }
}
#endif
