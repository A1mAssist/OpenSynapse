using OpenSynapse.Core.Devices;

namespace OpenSynapse.App.ViewModels;

public sealed partial class MainViewModel
{
    private static readonly TimeSpan ForegroundDeviceScanInterval = TimeSpan.FromSeconds(10);
    private string? _lastObservedForegroundExecutablePath;
    private string? _lastObservedApplicationBindings;
    private string? _lastObservedActiveProfileName;

    public void RequestDeviceRefresh()
    {
        Interlocked.Exchange(ref _deviceRefreshRequested, 1);
        try
        {
            _deviceWatchSignal.Release();
        }
        catch (SemaphoreFullException)
        {
            // One pending signal is enough; the refresh flag retains the request.
        }
    }

    internal void SetDeviceWatchActive(bool active)
    {
        Volatile.Write(ref _deviceWatchActive, active ? 1 : 0);
        if (active)
        {
            try
            {
                _deviceWatchSignal.Release();
            }
            catch (SemaphoreFullException)
            {
            }
        }
    }

    public async Task RunDeviceWatchLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (true)
            {
                var interval = Volatile.Read(ref _deviceWatchActive) != 0 &&
                               Volatile.Read(ref _displayAvailable) != 0
                    ? TimeSpan.FromSeconds(3)
                    : _profile.ApplicationBindings.Count > 0
                        ? TimeSpan.FromSeconds(10)
                        : Timeout.InfiniteTimeSpan;
                await _deviceWatchSignal.WaitAsync(interval, cancellationToken);
                try
                {
                    var powerState = _powerSourceProvider.IsPluggedIn;
                    var executablePath = _profile.ApplicationBindings.Count == 0
                        ? null
                        : _activeApplicationProvider.ExecutablePath;
                    var applicationBindings = _profile.ApplicationBindings.Count == 0
                        ? string.Empty
                        : string.Join(
                            '\n',
                            _profile.ApplicationBindings
                                .OrderBy(binding => binding.Key, StringComparer.OrdinalIgnoreCase)
                                .Select(binding => $"{binding.Key}\0{binding.Value}"));
                    var profileChanged = false;
                    if (_lastObservedApplicationBindings is null ||
                        !StringComparer.OrdinalIgnoreCase.Equals(
                            _lastObservedForegroundExecutablePath, executablePath) ||
                        !StringComparer.Ordinal.Equals(
                            _lastObservedApplicationBindings, applicationBindings) ||
                        !StringComparer.OrdinalIgnoreCase.Equals(
                            _lastObservedActiveProfileName, _profile.ActiveProfileName))
                    {
                        var previousProfile = _profile.Clone();
                        var previousProfileSwitcher = _applicationProfileSwitcher.Clone();
                        profileChanged = _applicationProfileSwitcher.Update(_profile, executablePath);
                        _lastObservedForegroundExecutablePath = executablePath;
                        _lastObservedApplicationBindings = applicationBindings;
                        _lastObservedActiveProfileName = _profile.ActiveProfileName;
                        if (profileChanged)
                        {
                            RefreshProfileState();
                            try
                            {
                                await _profileStore.SaveAsync(_profile, cancellationToken);
                                ProfileStatusText = AppStrings.FormatText("ProfileAutoSwitched", ActiveProfileName);
                            }
                            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                            {
                                throw;
                            }
                            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                            {
                                _profile = previousProfile;
                                _applicationProfileSwitcher = previousProfileSwitcher;
                                _lastObservedApplicationBindings = null;
                                RefreshProfileState();
                                SetDeviceOperationError(AppStrings.FormatText("AutomaticProfileSaveError",
                                    exception.Message));
                                profileChanged = false;
                            }
                        }
                    }

                    if (Volatile.Read(ref _displayAvailable) == 0)
                    {
                        if (profileChanged)
                        {
                            Interlocked.Exchange(ref _deviceRefreshRequested, 1);
                        }
                        continue;
                    }

                    var powerChanged = _lastPowerState != powerState;
                    var refreshRequested = Volatile.Read(ref _deviceRefreshRequested) != 0;
                    var displayProfileRequested =
                        Interlocked.Exchange(ref _displayProfileApplyRequested, 0) != 0;
                    var periodicRefreshDue = Volatile.Read(ref _deviceWatchActive) != 0 &&
                        DateTimeOffset.UtcNow >= _nextFullDeviceRefresh;
                    if (refreshRequested ||
                        powerChanged ||
                        profileChanged ||
                        displayProfileRequested ||
                        periodicRefreshDue)
                    {
                        var snapshot = refreshRequested || periodicRefreshDue
                            ? await _discovery.DiscoverAsync(cancellationToken)
                            : new DeviceSnapshot(_deviceDescriptors, DateTimeOffset.UtcNow);
                        if (periodicRefreshDue)
                        {
                            _nextFullDeviceRefresh = DateTimeOffset.UtcNow + ForegroundDeviceScanInterval;
                        }
                        if (periodicRefreshDue && !refreshRequested && !powerChanged &&
                            !profileChanged && !displayProfileRequested &&
                            StringComparer.Ordinal.Equals(
                                _deviceFingerprint, CreateDeviceFingerprint(snapshot)))
                        {
                            continue;
                        }
                        await RefreshDevicesCoreAsync(
                            snapshot,
                            cancellationToken,
                            applyDisplayProfile: powerChanged || profileChanged || displayProfileRequested);
                    }
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception exception) when (IsExpectedRuntimeException(exception))
                {
                    SetDeviceQueryError(AppStrings.FormatText("DeviceWatchError", exception.Message));
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    public Task RefreshDevicesAsync(CancellationToken cancellationToken = default) =>
        RefreshDevicesCoreAsync(null, cancellationToken);
}
