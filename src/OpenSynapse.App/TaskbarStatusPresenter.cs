using System.Collections.Specialized;
using Microsoft.UI.Dispatching;
using OpenSynapse.App.ViewModels;
using OpenSynapse.Core.Diagnostics;
using OpenSynapse.Core.Sensors;
using OpenSynapse.Core.Storage;
using OpenSynapse.Windows.Lifecycle;

namespace OpenSynapse.App;

internal sealed class TaskbarStatusPresenter : IDisposable
{
    private static readonly TimeSpan UiRefreshInterval = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan BatteryReadInterval = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan EmptyGrace = TimeSpan.FromSeconds(30);
    private readonly MainViewModel _viewModel;
    private readonly AppBehaviorSettings _settings;
    private readonly LocalDiagnosticLog _diagnosticLog;
    private readonly Action _showMainWindow;
    private readonly DispatcherQueue? _dispatcherQueue;
    private readonly DispatcherQueueTimer? _timer;
    private readonly Dictionary<string, int?> _batteryReadings = new(StringComparer.OrdinalIgnoreCase);
    private TaskbarStatusWindow? _statusWindow;
    private CancellationTokenSource? _batteryReadCancellation;
    private string _lastBatterySummary = string.Empty;
    private TaskbarStatusSnapshot? _lastSnapshot;
    private string _lastAutomationText = string.Empty;
    private DateTimeOffset? _emptySince;
    private DateTimeOffset _lastBatteryReadAt = DateTimeOffset.MinValue;
    private bool _batteryReadRequested = true;
    private bool _enabled;
    private bool _disposed;

    public TaskbarStatusPresenter(
        MainViewModel viewModel,
        AppBehaviorSettings settings,
        LocalDiagnosticLog diagnosticLog,
        Action showMainWindow)
    {
        _viewModel = viewModel;
        _settings = settings;
        _diagnosticLog = diagnosticLog;
        _showMainWindow = showMainWindow;
        _viewModel.Devices.CollectionChanged += OnDevicesChanged;
        _viewModel.RazerHid.Devices.CollectionChanged += OnDevicesChanged;
        _viewModel.Accessories.HeadsetAudioDevices.CollectionChanged += OnDevicesChanged;
        _viewModel.Accessories.KeyboardDevices.CollectionChanged += OnDevicesChanged;
        _viewModel.BlackSharkDevices.CollectionChanged += OnDevicesChanged;
        _viewModel.BleDevices.CollectionChanged += OnDevicesChanged;
        _viewModel.AwBleHeadsetDevices.CollectionChanged += OnDevicesChanged;
        _dispatcherQueue = DispatcherQueue.GetForCurrentThread();
        _timer = _dispatcherQueue?.CreateTimer();
        if (_timer is not null)
        {
            _timer.Interval = UiRefreshInterval;
            _timer.Tick += (_, _) => Refresh();
        }
        SetEnabled(settings.TaskbarStatusEnabled);
    }

    public void SetEnabled(bool enabled)
    {
        if (_disposed || _enabled == enabled) return;
        _enabled = enabled;
        if (!enabled)
        {
            _timer?.Stop();
            CancelBatteryRead();
            _batteryReadings.Clear();
            _lastSnapshot = null;
            _lastAutomationText = string.Empty;
            _emptySince = null;
            _statusWindow?.Dispose();
            _statusWindow = null;
            return;
        }

        _batteryReadRequested = true;
        _timer?.Start();
        Refresh();
    }

    public void RefreshNow()
    {
        _batteryReadRequested = true;
        Refresh();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _timer?.Stop();
        CancelBatteryRead();
        _statusWindow?.Dispose();
        _statusWindow = null;
        _viewModel.Devices.CollectionChanged -= OnDevicesChanged;
        _viewModel.RazerHid.Devices.CollectionChanged -= OnDevicesChanged;
        _viewModel.Accessories.HeadsetAudioDevices.CollectionChanged -= OnDevicesChanged;
        _viewModel.Accessories.KeyboardDevices.CollectionChanged -= OnDevicesChanged;
        _viewModel.BlackSharkDevices.CollectionChanged -= OnDevicesChanged;
        _viewModel.BleDevices.CollectionChanged -= OnDevicesChanged;
        _viewModel.AwBleHeadsetDevices.CollectionChanged -= OnDevicesChanged;
        GC.SuppressFinalize(this);
    }

    private void OnDevicesChanged(object? sender, NotifyCollectionChangedEventArgs args)
    {
        _batteryReadRequested = true;
        Refresh();
    }

    private void Refresh()
    {
        if (!_enabled || _disposed) return;
        _statusWindow?.SetClickEnabled(_settings.TaskbarStatusClickOpensApp);
        var available = CurrentBatteries()
            .Where(item => item.Percent is not null)
            .Select(item => new TaskbarBatteryStatus(
                item.Key,
                item.Percent ?? 0,
                item.IsCharging,
                item.Glyph,
                item.Name))
            .ToArray();
        var selected = TaskbarStatusPolicy.SelectBatteries(
            available, _settings.TaskbarBatteryDisplay);
        RequestBatteryReadIfDue();

        if (selected.Count == 0)
        {
            // Device rescans empty the source list for a few seconds; keep the last frame.
            _emptySince ??= DateTimeOffset.UtcNow;
            if (_lastSnapshot is { } previous &&
                DateTimeOffset.UtcNow - _emptySince < EmptyGrace)
            {
                SetTimerInterval(BatteryReadInterval);
                _statusWindow?.Update(previous, _lastAutomationText);
                return;
            }

            _lastBatterySummary = string.Empty;
            _lastSnapshot = null;
            _lastAutomationText = string.Empty;
            // Nothing to draw: keep the battery retry, no faster UI tick for a
            // window that is not there.
            SetTimerInterval(BatteryReadInterval);
            _statusWindow?.Clear();
            return;
        }

        _emptySince = null;
        var summary = string.Join(
            ", ", selected.Select(item =>
                $"{item.Name} {item.Percent}%{(item.IsCharging == true ? " charging" : string.Empty)}"));
        var summaryChanged = !StringComparer.Ordinal.Equals(_lastBatterySummary, summary);
        if (summaryChanged)
        {
            _lastBatterySummary = summary;
            _diagnosticLog.TryWrite("taskbar-status", $"strip shows {summary}");
        }

        SetTimerInterval(UiRefreshInterval);
        if (!summaryChanged && _statusWindow is not null && _lastSnapshot is not null)
        {
            return;
        }

        var snapshot = new TaskbarStatusSnapshot(selected);
        if (!TaskbarStatusPolicy.ShouldShow(snapshot, enabled: true))
        {
            _lastSnapshot = null;
            _statusWindow?.Clear();
            return;
        }

        var automationText = TaskbarStatusPolicy.FormatDescription(snapshot, string.Empty);
        try
        {
            if (_statusWindow is null)
            {
                _statusWindow = new TaskbarStatusWindow(
                    message => _diagnosticLog.TryWrite("taskbar-status", message), _showMainWindow);
                _statusWindow.SetClickEnabled(_settings.TaskbarStatusClickOpensApp);
            }
            _statusWindow.Update(snapshot, automationText);
            _lastSnapshot = snapshot;
            _lastAutomationText = automationText;
        }
        catch (Exception exception) when (
            exception is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            _diagnosticLog.TryWrite("taskbar-status", $"window unavailable: {exception.Message}");
            _statusWindow?.Dispose();
            _statusWindow = null;
            _timer?.Stop();
        }
    }

    private IReadOnlyList<PeripheralBattery> CurrentBatteries()
    {
        var batteries = _viewModel.Tray.ConnectedBatteries;
        if (_batteryReadings.Count == 0) return batteries;
        return batteries.Select(battery =>
            battery.Percent is null &&
            _batteryReadings.TryGetValue(battery.Key, out var percent) &&
            percent is not null
                ? battery with { Percent = percent }
                : battery).ToArray();
    }

    private void RequestBatteryReadIfDue()
    {
        if (!_enabled || _disposed || _batteryReadCancellation is not null) return;
        var now = DateTimeOffset.UtcNow;
        if (!_batteryReadRequested && now - _lastBatteryReadAt < BatteryReadInterval) return;
        _batteryReadRequested = false;
        _lastBatteryReadAt = now;
        var cancellation = new CancellationTokenSource();
        _batteryReadCancellation = cancellation;
        _ = ReadBatteriesAsync(cancellation);
    }

    private void SetTimerInterval(TimeSpan interval)
    {
        if (_timer is not null && _timer.Interval != interval)
        {
            _timer.Interval = interval;
        }
    }

    private async Task ReadBatteriesAsync(CancellationTokenSource cancellation)
    {
        try
        {
            var readings = await _viewModel.Tray.ReadBatteriesAsync(cancellation.Token);
            if (cancellation.IsCancellationRequested || _disposed) return;
            // Keep the last good value per device. A failed or sleeping device
            // must not blank the strip until the device actually disappears.
            foreach (var reading in readings)
            {
                if (reading.Percent is { } percent)
                {
                    _batteryReadings[reading.Key] = percent;
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            _diagnosticLog.TryWrite("taskbar-battery", exception.ToString());
        }
        finally
        {
            if (ReferenceEquals(_batteryReadCancellation, cancellation))
            {
                _batteryReadCancellation = null;
                cancellation.Dispose();
                if (!_disposed && _enabled)
                {
                    _dispatcherQueue?.TryEnqueue(Refresh);
                }
            }
        }
    }

    private void CancelBatteryRead()
    {
        var cancellation = _batteryReadCancellation;
        _batteryReadCancellation = null;
        cancellation?.Cancel();
        cancellation?.Dispose();
    }
}
