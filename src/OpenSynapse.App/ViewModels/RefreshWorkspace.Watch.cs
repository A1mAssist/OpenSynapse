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
                var interval = Volatile.Read(ref _deviceWatchActive) != 0 &&
                               IsDisplayAvailable
                    ? LiteModePolicy.DeviceReadoutInterval(_liteMode)
                    : Profile.ApplicationBindings.Count > 0
                        ? TimeSpan.FromSeconds(10)
                        : Timeout.InfiniteTimeSpan;
                await _deviceWatchSignal.WaitAsync(interval, cancellationToken);
                try
                {
                    var powerState = _currentPowerSource();
                    var executablePath = _activeApplicationProvider.ExecutablePath;
                    await SwitchDeviceProfilesAsync(executablePath, cancellationToken);
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
                        await _pollHeadsetPower(cancellationToken);
                        await RefreshDeviceReadoutsAsync(cancellationToken);
                        RefreshBladeTouchpadState();
                        await _razerHid.KeepAliveDriverModeAsync(cancellationToken);
                    }
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
                // The main device watch already refreshes this while the window is visible
                // and the display is available. It sleeps in the tray, so this loop covers
                // the hidden-window and display-off cases without running twice.
                if (Volatile.Read(ref _deviceWatchActive) != 0 && IsDisplayAvailable)
                {
                    continue;
                }

                // The main device watch stops when the window is hidden. The
                // touchpad state is an OS setting, so keep mirroring it onto
                // the EC while the app is in the tray.
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
