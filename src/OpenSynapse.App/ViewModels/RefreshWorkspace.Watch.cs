using OpenSynapse.Core.Devices;
using OpenSynapse.Core.Profiles;

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
                    ? TimeSpan.FromSeconds(1)
                    : Profile.ApplicationBindings.Count > 0
                        ? TimeSpan.FromSeconds(10)
                        : Timeout.InfiniteTimeSpan;
                await _deviceWatchSignal.WaitAsync(interval, cancellationToken);
                try
                {
                    var powerState = _currentPowerSource();
                    var executablePath = Profile.ApplicationBindings.Count == 0
                        ? null
                        : _activeApplicationProvider.ExecutablePath;
                    var applicationBindings = Profile.ApplicationBindings.Count == 0
                        ? string.Empty
                        : string.Join(
                            '\n',
                            Profile.ApplicationBindings
                                .OrderBy(binding => binding.Key, StringComparer.OrdinalIgnoreCase)
                                .Select(binding => $"{binding.Key}\0{binding.Value}"));
                    var profileChanged = false;
                    if (_lastObservedApplicationBindings is null ||
                        !StringComparer.OrdinalIgnoreCase.Equals(
                            _lastObservedForegroundExecutablePath, executablePath) ||
                        !StringComparer.Ordinal.Equals(
                            _lastObservedApplicationBindings, applicationBindings) ||
                        !StringComparer.OrdinalIgnoreCase.Equals(
                            _lastObservedActiveProfileName, Profile.ActiveProfileName))
                    {
                        var previousProfile = Profile.Clone();
                        var previousProfileSwitcher = _applicationProfileSwitcher.Clone();
                        profileChanged = _applicationProfileSwitcher.Update(Profile, executablePath);
                        _lastObservedForegroundExecutablePath = executablePath;
                        _lastObservedApplicationBindings = applicationBindings;
                        _lastObservedActiveProfileName = Profile.ActiveProfileName;
                        if (profileChanged)
                        {
                            _profiles.RefreshProfileState();
                            try
                            {
                                await _profileStore.SaveAsync(Profile, cancellationToken);
                                _profiles.ProfileStatusText = AppStrings.FormatText("ProfileAutoSwitched", _profiles.ActiveProfileName);
                            }
                            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                            {
                                throw;
                            }
                            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                            {
                                _setProfile(previousProfile);
                                _applicationProfileSwitcher = previousProfileSwitcher;
                                _lastObservedApplicationBindings = null;
                                _profiles.RefreshProfileState();
                                _setDeviceOperationError(AppStrings.FormatText("AutomaticProfileSaveError",
                                    exception.Message));
                                profileChanged = false;
                            }
                        }
                    }

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
}
