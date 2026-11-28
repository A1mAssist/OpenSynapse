using OpenSynapse.Core.Devices;

namespace OpenSynapse.App.ViewModels;

public sealed partial class BladeWorkspace
{
    private readonly object _touchpadSyncGate = new();
    private readonly CancellationTokenSource _touchpadSyncStop = new();
    private Task _touchpadSyncTask = Task.CompletedTask;
    private (DeviceDescriptor[] Devices, bool Enabled, long Version)? _touchpadPending;
    private long _touchpadSyncVersion;
    private bool _touchpadSyncRunning;

    private void ApplyTouchpadState()
    {
        if (_touchpadController?.GetEnabled() is bool touchpadEnabled)
        {
            var firstRead = !_touchpadStateRead;
            var changed = !firstRead &&
                _blade._confirmedBladeTouchpadEnabled != touchpadEnabled;
            _touchpadStateRead = true;
            TouchpadEnabled = touchpadEnabled;
            _blade._confirmedBladeTouchpadEnabled = touchpadEnabled;
            TouchpadText = touchpadEnabled
                ? AppStrings.Text("Text_F55AD712")
                : AppStrings.Text("Text_7E3B0F3C");
            if (!_blade._canSetBladeTouchpad)
            {
                _blade._canSetBladeTouchpad = true;
                OnPropertyChanged(nameof(CanSetTouchpad));
            }
            var now = Environment.TickCount64;
            if (changed || firstRead)
            {
                _touchpadIndicatorRetryUntil = now + 60_000;
            }
            var retry = now < _touchpadIndicatorRetryUntil;
            var settle = retry && now - _touchpadIndicatorSyncedAt >= 5_000;
            if (changed || firstRead || settle)
            {
                _touchpadIndicatorSyncedAt = now;
                // Also write on the first read: the lighting take-over can reset
                // the EC indicator after the startup sync. A rejected write stays
                // pending so the next refresh retries it instead of giving up.
                _ = SyncObservedTouchpadIndicatorAsync(ActiveDescriptors, touchpadEnabled);
            }
            if (changed)
            {
                // Mirror an Fn-key change onto the EC and report it the same way
                // as a toggle that came through our mapping runtime.
                TouchpadChangedByUser?.Invoke(touchpadEnabled);
            }
        }
        else
        {
            TouchpadText = AppStrings.Text("Text_72C29287");
            if (_blade._canSetBladeTouchpad)
            {
                _blade._canSetBladeTouchpad = false;
                OnPropertyChanged(nameof(CanSetTouchpad));
            }
        }
    }

    private Task SyncObservedTouchpadIndicatorAsync(
        IReadOnlyList<DeviceDescriptor> devices,
        bool enabled)
    {
        lock (_touchpadSyncGate)
        {
            if (IsDisposed || _touchpadSyncStop.IsCancellationRequested) return Task.CompletedTask;
            _touchpadPending = (devices.ToArray(), enabled, ++_touchpadSyncVersion);
            if (_touchpadSyncRunning) return _touchpadSyncTask;
            _touchpadSyncRunning = true;
            return _touchpadSyncTask = DrainTouchpadIndicatorAsync();
        }
    }

    private async Task DrainTouchpadIndicatorAsync()
    {
        while (true)
        {
            (DeviceDescriptor[] Devices, bool Enabled, long Version) request;
            lock (_touchpadSyncGate)
            {
                if (_touchpadPending is not { } pending || IsDisposed || _touchpadSyncStop.IsCancellationRequested)
                {
                    _touchpadPending = null;
                    _touchpadSyncRunning = false;
                    return;
                }
                request = pending;
                _touchpadPending = null;
            }
            var confirmed = false;
            try
            {
                using var deadline = CancellationTokenSource.CreateLinkedTokenSource(_touchpadSyncStop.Token);
                deadline.CancelAfter(TimeSpan.FromSeconds(5));
                confirmed = await _deviceTelemetryReader.SetBladeTouchpadStatusAsync(
                    request.Devices, request.Enabled, deadline.Token).ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                // This background writer must drain the newest request even after a failed write.
                _diagnosticLog.TryWrite("device-operation", $"touchpad indicator follow-up failed: {exception}");
            }
            lock (_touchpadSyncGate)
            {
                var sameDevices = request.Devices.Select(device => device.Id).SequenceEqual(
                    ActiveDescriptors.Select(device => device.Id), StringComparer.OrdinalIgnoreCase);
                if (!IsDisposed && request.Version == _touchpadSyncVersion && sameDevices &&
                    _blade._confirmedBladeTouchpadEnabled == request.Enabled)
                    _touchpadIndicatorConfirmed = confirmed;
            }
            if (!confirmed)
                _diagnosticLog.TryWrite("device-operation", $"touchpad indicator did not acknowledge enabled={request.Enabled}");
        }
    }

    /// <summary>
    /// Re-sends the touchpad indicator after a Blade software-mode takeover. Entering
    /// that mode resets the function lighting, including the touchpad lamp.
    /// </summary>
    internal void ReassertTouchpadIndicator()
    {
        if (_touchpadController?.GetEnabled() is bool enabled)
        {
            _ = SyncObservedTouchpadIndicatorAsync(ActiveDescriptors, enabled);
        }
    }

    internal void RefreshTouchpadState() => ApplyTouchpadState();
}
