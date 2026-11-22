using OpenSynapse.Core.Devices;

namespace OpenSynapse.App.ViewModels;

public sealed partial class BladeWorkspace
{
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
            if (changed || firstRead || !_touchpadIndicatorConfirmed)
            {
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

    private async Task SyncObservedTouchpadIndicatorAsync(
        IReadOnlyList<DeviceDescriptor> devices,
        bool enabled)
    {
        try
        {
            _touchpadIndicatorConfirmed = await _deviceTelemetryReader.SetBladeTouchpadStatusAsync(
                devices, enabled, CancellationToken.None);
            if (!_touchpadIndicatorConfirmed)
            {
                _diagnosticLog.TryWrite(
                    "device-operation",
                    $"touchpad indicator did not follow the device state enabled={enabled}");
            }
            else
            {
                _diagnosticLog.TryWrite(
                    "blade-touchpad",
                    $"indicator synchronized enabled={enabled}");
            }
        }
        catch (Exception exception) when (RuntimeErrors.IsExpected(exception))
        {
            _touchpadIndicatorConfirmed = false;
            _diagnosticLog.TryWrite(
                "device-operation",
                $"touchpad indicator follow-up failed: {exception}");
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
