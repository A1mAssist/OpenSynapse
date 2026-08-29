namespace OpenSynapse.App.ViewModels;

public sealed partial class MainViewModel
{
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
                var interval = Volatile.Read(ref _deviceWatchActive) != 0
                    ? TimeSpan.FromSeconds(3)
                    : TimeSpan.FromSeconds(10);
                await _deviceWatchSignal.WaitAsync(interval, cancellationToken);
                try
                {
                    var snapshot = await _discovery.DiscoverAsync(cancellationToken);
                    var powerState = _powerSourceProvider.IsPluggedIn;
                    var refreshRequested = Volatile.Read(ref _deviceRefreshRequested) != 0;
                    var previousProfile = _profile.Clone();
                    var previousProfileSwitcher = _applicationProfileSwitcher.Clone();
                    var profileChanged = _applicationProfileSwitcher.Update(
                        _profile, _activeApplicationProvider.ExecutablePath);
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
                            RefreshProfileState();
                            SetDeviceOperationError(AppStrings.FormatText("AutomaticProfileSaveError",
                                exception.Message));
                            profileChanged = false;
                        }
                    }
                    var powerChanged = _lastPowerState != powerState;
                    var displayProfileRequested =
                        Interlocked.Exchange(ref _displayProfileApplyRequested, 0) != 0;
                    if (!StringComparer.Ordinal.Equals(_deviceFingerprint, CreateDeviceFingerprint(snapshot)) ||
                        powerChanged ||
                        profileChanged ||
                        displayProfileRequested ||
                        refreshRequested ||
                        DateTimeOffset.UtcNow >= _nextFullDeviceRefresh)
                    {
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
