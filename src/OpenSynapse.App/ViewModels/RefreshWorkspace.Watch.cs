using OpenSynapse.Core.Devices;
using OpenSynapse.Core.Profiles;
using OpenSynapse.Core.Storage;

namespace OpenSynapse.App.ViewModels;

public sealed partial class RefreshWorkspace
{
    public async Task RunDeviceWatchLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (true)
            {
                var active = Volatile.Read(ref _deviceWatchActive) != 0 && IsDisplayAvailable;
                // Keep a slow heartbeat while the window is hidden: a mouse that slept
                // or re-paired loses driver mode, and only this loop re-arms it.
                var interval = active
                    ? LiteModePolicy.DeviceReadoutInterval(_liteMode)
                    : TimeSpan.FromSeconds(10);
                await _deviceWatchSignal.WaitAsync(interval, cancellationToken);
                try
                {
                    var powerState = _currentPowerSource();
                    if (active || Profile.ApplicationBindings.Count > 0)
                    {
                        var executablePath = _activeApplicationProvider.ExecutablePath;
                        await SwitchDeviceProfilesAsync(executablePath, cancellationToken);
                    }
                    var profileChanged = false;

                    // Discovery is event-driven: startup, device notifications, resume,
                    // and explicit refresh requests set this flag. A request arriving
                    // during a scan remains queued for the next loop iteration.
                    var refreshRequested = Interlocked.Exchange(ref _deviceRefreshRequested, 0) != 0;
                    if (!IsDisplayAvailable)
                    {
                        if (refreshRequested || profileChanged)
                        {
                            Interlocked.Exchange(ref _deviceRefreshRequested, 1);
                        }
                        continue;
                    }

                    var powerChanged = _lastPowerState != powerState;
                    var displayProfileRequested =
                        Interlocked.Exchange(ref _displayProfileApplyRequested, 0) != 0;
                    if (refreshRequested || powerChanged || profileChanged || displayProfileRequested)
                    {
                        if (refreshRequested)
                        {
                            _diagnosticLog.TryWrite(
                                "device-watch",
                                $"discovery requested ({Volatile.Read(ref _deviceRefreshReason)})");
                        }
                        var snapshot = refreshRequested
                            ? await _discovery.DiscoverAsync(cancellationToken)
                            : new DeviceSnapshot(ActiveDescriptors, DateTimeOffset.UtcNow);
                        await RefreshDevicesCoreAsync(
                            snapshot,
                            cancellationToken,
                            applyDisplayProfile: powerChanged || profileChanged || displayProfileRequested);
                    }
                    else
                    {
                        // Nothing asked for a rescan, but the readouts on the device page are
                        // live numbers such as fan speed, so they still need a periodic read.
                        // This path only reads; it never writes a setting back to the device.
                        if (active)
                        {
                            await _pollHeadsetPower(cancellationToken);
                        }
                        await RefreshDeviceReadoutsAsync(cancellationToken);
                        await _razerHid.KeepAliveDriverModeAsync(cancellationToken);
                    }
                    await _razerHid.RefreshBatteriesAsync(cancellationToken);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    _diagnosticLog.TryWrite("device-watch", exception.ToString());
                    _setDeviceQueryError(AppStrings.FormatText("DeviceWatchError", exception.Message));
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    public async Task RunBladeTouchpadWatchLoopAsync(CancellationToken cancellationToken)
    {
        if (_liteMode)
        {
            return;
        }

        try
        {
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
            while (await timer.WaitForNextTickAsync(cancellationToken))
            {
                // Mirror the OS touchpad state onto the EC here every second. The write
                // itself is gated in ApplyTouchpadState, so this is one registry read
                // unless a boot-time write still needs retrying.
                try
                {
                    RefreshBladeTouchpadState();
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    _diagnosticLog.TryWrite(
                        "blade-touchpad",
                        $"touchpad state refresh failed: {exception}");
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private void RefreshBladeTouchpadState()
    {
        if (ActiveDescriptors.Any(device =>
                DeviceProtocolFamilies.IsBlade(device.ProtocolFamily) &&
                device.Access == DeviceAccessState.Available))
        {
            _blade.RefreshTouchpadState();
        }
    }
}
