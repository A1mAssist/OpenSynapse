#if DEBUG
using System.Collections.Specialized;
using System.Diagnostics;
using System.Diagnostics.Tracing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using OpenSynapse.App.ViewModels;
using OpenSynapse.Core.Lighting;
using OpenSynapse.Core.Storage;

namespace OpenSynapse.App.Views;

/// <summary>DEBUG-only load timing. Enable with OPENSSYNAPSE_STUDIO_TIMING=&lt;file&gt;.</summary>
public sealed partial class LightingStudioPage
{
    private static readonly Stopwatch StudioTimingClock = new();
    private static readonly object StudioTimingGate = new();
    private string? _studioTimingPath;
    private bool _studioTimingAttached;
    private bool _studioTimingRealFrameSeen;
    private bool _studioTimingVisibleFrameSeen;
    private bool _studioTimingFrameApplied;
    private bool _studioTimingFrameJudged;
    private long _studioTimingLastRender;
    private double _studioTimingRealFrameMs;
    private string _studioTimingRealFrameColor = "none";
    private StudioHttpTimingListener? _studioHttpTiming;

    private void StartStudioTimingIfRequested()
    {
        if (_studioTimingPath is not null) return;
        var path = Environment.GetEnvironmentVariable("OPENSSYNAPSE_STUDIO_TIMING");
        if (string.IsNullOrWhiteSpace(path)) return;
        _studioTimingPath = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(_studioTimingPath)!);
        StudioTimingClock.Restart();
        File.WriteAllText(_studioTimingPath, string.Empty);
        StudioTimingMark("timing-start",
            $"processStart={Process.GetCurrentProcess().StartTime:O};pid={Environment.ProcessId};exe={Environment.ProcessPath}");
        var cache = AppDataPaths.Combine("lighting-studio", "manifests", "710_0.json");
        StudioTimingMark("manifest-cache-file",
            File.Exists(cache)
                ? $"path={cache};bytes={new FileInfo(cache).Length};utc={File.GetLastWriteTimeUtc(cache):O}"
                : $"path={cache};missing");
        CompositionTarget.Rendering += StudioTimingOnRendering;
        _studioHttpTiming = new StudioHttpTimingListener(StudioTimingMark);
    }

    private void StudioTimingAttachViewModel()
    {
        if (_studioTimingPath is null || _studioTimingAttached || _viewModel is null) return;
        _studioTimingAttached = true;
        _viewModel.Devices.CollectionChanged += StudioTimingDevicesChanged;
        _viewModel.DeviceVisuals.CollectionChanged += StudioTimingVisualsChanged;
        StudioTimingMark("view-model-attached", $"devices={_viewModel.Devices.Count};visuals={_viewModel.DeviceVisuals.Count}");
    }

    private void RefreshDevicesWithTiming(string phase)
    {
        if (_viewModel is null) return;
        var started = Stopwatch.GetTimestamp();
        StudioTimingMark($"{phase}-refresh-enter", $"devices={_viewModel.Devices.Count};visuals={_viewModel.DeviceVisuals.Count}");
        _viewModel.RefreshDevices();
        StudioTimingMark($"{phase}-refresh-exit",
            $"ms={StudioTimingElapsed(started):0.0};devices={_viewModel.Devices.Count};visuals={_viewModel.DeviceVisuals.Count}");
    }

    private void StudioTimingDevicesChanged(object? sender, NotifyCollectionChangedEventArgs args)
    {
        if (args.NewItems is { } added)
        {
            foreach (StudioDeviceOption option in added)
            {
                StudioTimingMark("device-option-add",
                    $"name={option.Name};instance={option.EffectiveInstanceId};product={option.EffectiveProductId};rows={option.Rows};columns={option.Columns};argb={option.IsArgb}");
            }
        }

        if (args.OldItems is { } removed && args.Action != NotifyCollectionChangedAction.Reset)
        {
            foreach (StudioDeviceOption option in removed)
            {
                StudioTimingMark("device-option-remove",
                    $"name={option.Name};instance={option.EffectiveInstanceId};product={option.EffectiveProductId}");
            }
        }

        StudioTimingMark("devices-collection", $"action={args.Action};count={_viewModel?.Devices.Count ?? 0}");
    }

    private void StudioTimingVisualsChanged(object? sender, NotifyCollectionChangedEventArgs args)
    {
        if (args.NewItems is { } added)
        {
            foreach (StudioDeviceVisual visual in added)
            {
                StudioTimingMark("visual-add",
                    $"{StudioTimingDescribeVisual(visual)};hosts={DeviceCanvas.DebugHostCount};leds={DeviceCanvas.DebugLedCount};state={DeviceCanvas.DebugHostState()}");
            }
        }

        if (args.OldItems is { } removed && args.Action != NotifyCollectionChangedAction.Reset)
        {
            foreach (StudioDeviceVisual visual in removed)
            {
                StudioTimingMark("visual-remove", StudioTimingDescribeVisual(visual));
            }
        }

        StudioTimingMark("visuals-collection", $"action={args.Action};count={_viewModel?.DeviceVisuals.Count ?? 0}");
    }

    private void StudioTimingOnPropertyChanged(string? propertyName)
    {
        if (_studioTimingPath is null || propertyName is not (
                nameof(LightingStudioViewModel.DeviceVisual) or
                nameof(LightingStudioViewModel.DeviceVisuals) or
                nameof(LightingStudioViewModel.SelectedDevice))) return;
        var selected = _viewModel?.SelectedDevice;
        StudioTimingMark("property",
            $"name={propertyName};selected={selected?.Name};visuals={_viewModel?.DeviceVisuals.Count ?? 0};svg={_viewModel?.DeviceVisuals.Any(visual => visual.UsesSvg)}");
    }

    private void StudioTimingMarkRefresh(long started, IReadOnlyList<StudioDeviceVisual> visuals, string reason)
    {
        if (_studioTimingPath is null) return;
        var first = visuals.FirstOrDefault();
        StudioTimingMark("refresh-canvas",
            $"ms={StudioTimingElapsed(started):0.0};reason={reason};count={visuals.Count};svg={visuals.Any(visual => visual.UsesSvg)};first={(first is null ? "none" : StudioTimingDescribeVisual(first))};canvas={DeviceCanvas.Visibility};hosts={DeviceCanvas.DebugHostCount};leds={DeviceCanvas.DebugLedCount};color={DeviceCanvas.DebugFirstLedColor};fill={DeviceCanvas.DebugFirstLedFill};opacity={DeviceCanvas.DebugFirstLedOpacity};state={DeviceCanvas.DebugHostState()}");
    }

    private void StudioTimingMarkFrameApplied()
    {
        if (_studioTimingPath is null || _studioTimingFrameApplied) return;
        if (_viewModel?.DeviceVisuals.FirstOrDefault(visual => visual.UsesSvg) is not { } visual) return;
        _studioTimingFrameApplied = true;
        StudioTimingMark("frame-applied-to-real",
            $"{StudioTimingDescribeVisual(visual)};hosts={DeviceCanvas.DebugHostCount};leds={DeviceCanvas.DebugLedCount};color={DeviceCanvas.DebugFirstLedColor};fill={DeviceCanvas.DebugFirstLedFill}");
    }

    private void StudioTimingOnRendering(object? sender, object args)
    {
        if (_studioTimingPath is null) return;
        var now = Stopwatch.GetTimestamp();
        if (_studioTimingLastRender != 0)
        {
            var gap = StudioTimingElapsed(_studioTimingLastRender, now);
            if (gap > 250)
            {
                StudioTimingMark("render-gap", $"ms={gap:0.0}");
            }
        }

        _studioTimingLastRender = now;
        if (_viewModel?.DeviceVisuals.FirstOrDefault(visual => visual.UsesSvg) is not { } real) return;
        if (DeviceCanvas.Visibility != Visibility.Visible || DeviceCanvas.DebugHostCount == 0) return;
        var elapsed = StudioTimingClock.Elapsed.TotalMilliseconds;
        if (!_studioTimingRealFrameSeen)
        {
            _studioTimingRealFrameSeen = true;
            _studioTimingRealFrameMs = elapsed;
            _studioTimingRealFrameColor = DeviceCanvas.DebugFirstLedColor;
            StudioTimingMark("first-rendered-real-host",
                $"elapsedMs={elapsed:0.0};{StudioTimingDescribeVisual(real)};color={_studioTimingRealFrameColor};fill={DeviceCanvas.DebugFirstLedFill};frameApplied={_studioTimingFrameApplied};state={DeviceCanvas.DebugHostState()}");
        }

        if (!_studioTimingVisibleFrameSeen && DeviceCanvas.DebugFirstHostVisible)
        {
            _studioTimingVisibleFrameSeen = true;
            StudioTimingMark("first-visible-real-host",
                $"elapsedMs={elapsed:0.0};frameApplied={_studioTimingFrameApplied};color={DeviceCanvas.DebugFirstLedColor};state={DeviceCanvas.DebugHostState()}");
        }

        if (_studioTimingFrameJudged || elapsed < _studioTimingRealFrameMs + 1500) return;
        _studioTimingFrameJudged = true;
        var color = DeviceCanvas.DebugFirstLedColor;
        StudioTimingMark(
            _studioTimingFrameApplied ? "frame-color-applied" : "frame-color-stale",
            $"elapsedMs={elapsed:0.0};initial={_studioTimingRealFrameColor};now={color};changed={!string.Equals(color, _studioTimingRealFrameColor, StringComparison.Ordinal)};frameApplied={_studioTimingFrameApplied};hosts={DeviceCanvas.DebugHostCount};leds={DeviceCanvas.DebugLedCount};state={DeviceCanvas.DebugHostState()}");
    }

    private static string StudioTimingDescribeVisual(StudioDeviceVisual visual)
    {
        var layout = visual.Layout;
        return $"name={visual.Name};instance={visual.InstanceId};product={visual.ProductId};svg={visual.UsesSvg};paths={visual.Paths.Count};leds={visual.Leds.Count};size={visual.Width:0}x{visual.Height:0};layout={layout?.MatrixRows}x{layout?.MatrixColumns}";
    }

    private static double StudioTimingElapsed(long started) =>
        (Stopwatch.GetTimestamp() - started) * 1000d / Stopwatch.Frequency;

    private static double StudioTimingElapsed(long started, long ended) =>
        (ended - started) * 1000d / Stopwatch.Frequency;

    private void StudioTimingMark(string phase, string details = "")
    {
        var path = _studioTimingPath;
        if (path is null) return;
        lock (StudioTimingGate)
        {
            try
            {
                File.AppendAllText(path, FormattableString.Invariant(
                    $"{DateTimeOffset.Now:O} +{StudioTimingClock.Elapsed.TotalMilliseconds:0.0}ms tid={Environment.CurrentManagedThreadId} {phase} {details}{Environment.NewLine}"));
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    private sealed class StudioHttpTimingListener(Action<string, string> write) : EventListener
    {
        protected override void OnEventSourceCreated(EventSource eventSource)
        {
            if (eventSource.Name == "System.Net.Http")
            {
                EnableEvents(eventSource, EventLevel.Verbose, EventKeywords.All);
            }
        }

        protected override void OnEventWritten(EventWrittenEventArgs args)
        {
            if (args.EventName is null) return;
            var payload = args.Payload is null || args.PayloadNames is null
                ? string.Empty
                : string.Join(';', args.Payload.Select((value, index) =>
                    $"{args.PayloadNames[index]}={value}"));
            write($"http {args.EventName}", $"at={args.TimeStamp:O};activity={args.ActivityId};{payload}");
        }
    }
}
#endif
