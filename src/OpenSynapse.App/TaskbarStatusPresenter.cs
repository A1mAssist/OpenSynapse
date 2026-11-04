using System.Collections.Specialized;
using System.ComponentModel;
using Microsoft.UI.Dispatching;
using OpenSynapse.App.ViewModels;
using OpenSynapse.Core.Devices;
using OpenSynapse.Core.Diagnostics;
using OpenSynapse.Core.Sensors;
using OpenSynapse.Core.Storage;
using OpenSynapse.Windows.Lifecycle;

namespace OpenSynapse.App;

internal sealed class TaskbarStatusPresenter : IDisposable
{
    private static readonly TimeSpan BladeGracePeriod = TimeSpan.FromSeconds(30);
    private readonly MainWindow _window;
    private readonly MainViewModel _viewModel;
    private readonly LocalDiagnosticLog _diagnosticLog;
    private readonly DispatcherQueueTimer? _timer;
    private TaskbarStatusWindow? _statusWindow;
    private DateTimeOffset? _lastBladeSeenAt;
    private bool _enabled;
    private bool _disposed;

    public TaskbarStatusPresenter(
        MainWindow window,
        MainViewModel viewModel,
        AppBehaviorSettings settings,
        LocalDiagnosticLog diagnosticLog)
    {
        _window = window;
        _viewModel = viewModel;
        _diagnosticLog = diagnosticLog;
        _viewModel.Telemetry.PropertyChanged += OnTelemetryChanged;
        _viewModel.Blade.PropertyChanged += OnBladeChanged;
        _viewModel.Devices.CollectionChanged += OnDevicesChanged;
        _timer = DispatcherQueue.GetForCurrentThread()?.CreateTimer();
        if (_timer is not null)
        {
            _timer.Interval = TimeSpan.FromSeconds(2);
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
            _statusWindow?.Dispose();
            _statusWindow = null;
            return;
        }

        _timer?.Start();
        Refresh();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _timer?.Stop();
        _statusWindow?.Dispose();
        _statusWindow = null;
        _viewModel.Telemetry.PropertyChanged -= OnTelemetryChanged;
        _viewModel.Blade.PropertyChanged -= OnBladeChanged;
        _viewModel.Devices.CollectionChanged -= OnDevicesChanged;
        GC.SuppressFinalize(this);
    }

    private void OnTelemetryChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName is nameof(TelemetryWorkspace.BatteryPercent)
            or nameof(TelemetryWorkspace.IsCharging)
            or nameof(TelemetryWorkspace.BatteryChargeState))
        {
            Refresh();
        }
    }

    private void OnBladeChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName is nameof(BladeWorkspace.BladeProductId)
            or nameof(BladeWorkspace.PerformanceModeText))
        {
            Refresh();
        }
    }

    private void OnDevicesChanged(object? sender, NotifyCollectionChangedEventArgs args) => Refresh();

    private void Refresh()
    {
        if (!_enabled || _disposed) return;
        var bladeProductId = _viewModel.Blade.BladeProductId;
        if (bladeProductId != 0)
        {
            _lastBladeSeenAt = DateTimeOffset.UtcNow;
        }

        var now = DateTimeOffset.UtcNow;
        var hasBlade = bladeProductId != 0 ||
            (_lastBladeSeenAt is { } seen && now - seen < BladeGracePeriod);
        var snapshot = new TaskbarStatusSnapshot(
            _viewModel.Telemetry.BatteryPercent,
            _viewModel.Telemetry.IsCharging,
            _viewModel.Blade.PerformanceModeText);
        if (!hasBlade || !TaskbarStatusPolicy.ShouldShow(snapshot, enabled: true))
        {
            _statusWindow?.Hide();
            return;
        }

        var tooltip = TaskbarStatusPolicy.FormatTooltip(
            snapshot,
            _viewModel.Telemetry.BatteryChargeState,
            snapshot.PerformanceMode);
        try
        {
            if (_statusWindow is null)
            {
                _statusWindow = new TaskbarStatusWindow(
                    message => _diagnosticLog.TryWrite("taskbar-status", message));
                _statusWindow.Activated += ActivateBlade;
            }
            _statusWindow.Update(snapshot, tooltip, tooltip);
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

    private void ActivateBlade()
    {
        var bladeProductId = _viewModel.Blade.BladeProductId;
        var row = _viewModel.Devices.FirstOrDefault(device =>
            device.ProductId == bladeProductId &&
            DeviceProtocolFamilies.IsBlade(device.ProtocolFamily));
        if (row is not null)
        {
            _window.RequestDeviceNavigation(row);
        }
        else
        {
            _window.RequestNavigation("devices");
        }
    }
}
