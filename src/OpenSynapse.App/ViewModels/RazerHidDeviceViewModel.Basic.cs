using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using Microsoft.UI.Xaml;
using OpenSynapse.Core.Devices;
using OpenSynapse.Core.Profiles;
using OpenSynapse.Windows.Devices;
using OpenSynapse.Windows.Protocols;
using Windows.UI;

namespace OpenSynapse.App.ViewModels;

public sealed partial class RazerHidDeviceViewModel : INotifyPropertyChanged
{
    private DateTimeOffset _lastSlowCardLoad;
    private bool _slowCardsLoading;

    public Task LoadBasicStateAsync(CancellationToken cancellationToken = default) =>
        !IsReady || RequiresRescan
            ? Task.CompletedTask
            : _service.RunExclusiveAsync(Connection, LoadBasicStateCoreAsync, cancellationToken);

    private async Task LoadBasicStateCoreAsync(CancellationToken cancellationToken)
    {
        if (!IsReady || RequiresRescan || IsBasicBusy)
        {
            return;
        }
        IsBasicBusy = true;
        try
        {
            var state = await _service.ReadBasicStateAsync(Connection, cancellationToken, _unsupported);
            _basicState = state;
            if (state.UnsupportedCapabilities.Count > 0)
            {
                _unsupported.UnionWith(state.UnsupportedCapabilities);
                if (state.UnsupportedCapabilities.Contains(RazerHidBackendCapability.BrightnessRead))
                    _unsupportedBrightnessReads.Add(SelectedLightingZone);
                if (state.UnsupportedCapabilities.Contains(RazerHidBackendCapability.SensorRotationRead))
                    ClearSensorRotation();
            }
            if (state.PollingRate is { } polling) SelectedPollingRate = polling;
            LoadFullscreenPollingState();
            if (state.DpiX is { } x) DpiX = x;
            if (state.DpiY is { } y) DpiY = y;
            if (state.IdleTimeoutSeconds is { } idle) IdleTimeoutSeconds = idle;
            if (state.LowBatteryThresholdPercent is { } threshold) LowBatteryThresholdPercent = threshold;
            if (state.AutoSleepEnabled is { } autoSleep) AutoSleepEnabled = autoSleep;
            if (state.BatteryChargeLimiterEnabled is { } limiterEnabled) ChargeLimiterEnabled = limiterEnabled;
            if (state.BatteryChargeLimiterPercent is { } limiter) ChargeLimiterPercent = limiter;
            ApplyMiddlewareState(state);
            if (state.Brightness is { } brightness &&
                SelectedLightingZone == Connection.Definition.DefaultLedZone)
            {
                _brightnessKnown = true;
                Brightness = brightness;
            }
            ErrorText = string.Empty;
            if (state.Errors.Count > 0 || state.UnsupportedCapabilities.Count > 0)
            {
                // A failed optional read is a partial device, not a lost transport.
                // Keeping the connection usable avoids turning one unsupported
                // capability into a device-wide "communication interrupted" state.
                OnCapabilityChanged();
            }
            OnBasicStateChanged();
            await LoadMappingIfNeededAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            HandleFailure(exception);
        }
        finally
        {
            IsBasicBusy = false;
        }
    }

    /// <summary>
    /// Optional cards are read on selection and then at most every ten seconds. The
    /// one-second refresh used to re-read every profile name and card and kept the
    /// keyboard busy while the user was writing a setting.
    /// </summary>
    internal async Task LoadSlowCardsAsync(
        bool force = false,
        CancellationToken cancellationToken = default)
    {
        if (!IsReady || RequiresRescan || _slowCardsLoading)
        {
            return;
        }

        var activeProfile = _activeOnboardProfileKnown ? ActiveOnboardProfileId : (byte?)null;
        _slowCardsLoading = true;
        try
        {
            await LoadSlowCardsCoreAsync(force, cancellationToken);
        }
        finally
        {
            _slowCardsLoading = false;
        }

        // The profile button changes the active slot outside the app; the mapping
        // rows above still belong to the previous slot until they are re-read.
        if (activeProfile is { } previous &&
            _activeOnboardProfileKnown &&
            ActiveOnboardProfileId != previous)
        {
            InvalidateMapping();
        }
    }

    private async Task LoadSlowCardsCoreAsync(
        bool force,
        CancellationToken cancellationToken)
    {
        if (!force &&
            DateTimeOffset.UtcNow - _lastSlowCardLoad < TimeSpan.FromSeconds(30))
        {
            return;
        }

        _lastSlowCardLoad = DateTimeOffset.UtcNow;
        await LoadOptionalCardsAsync(cancellationToken);
    }

    // Static so the poll that runs this every second allocates nothing.
    private static readonly Func<RazerHidDeviceViewModel, CancellationToken, Task>[] OptionalLoads =
    [
        static (viewModel, token) => viewModel.LoadOnboardProfilesAsync(token),
        static (viewModel, token) => viewModel.LoadWirelessAsync(token),
        static (viewModel, token) => viewModel.LoadKnobAsync(token),
        static (viewModel, token) => viewModel.LoadGameModeAsync(token),
        static (viewModel, token) => viewModel.LoadAnalogKeyAsync(token),
        static (viewModel, token) => viewModel.LoadAnalogJoystickAsync(token),
        static (viewModel, token) => viewModel.LoadOledAsync(token),
        static (viewModel, token) => viewModel.LoadHapticScrollAsync(token),
    ];

    /// <summary>
    /// Loads the optional cards one by one. A transient failure in one of them must
    /// not abort the remaining cards or escalate to the device-wide error banner.
    /// </summary>
    private async Task LoadOptionalCardsAsync(CancellationToken cancellationToken)
    {
        foreach (var load in OptionalLoads)
        {
            try
            {
                // One gate per card: a user write can land between cards instead of
                // waiting for all eight reads to finish.
                await _service.RunExclusiveAsync(
                    Connection,
                    token => load(this, token),
                    cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception)
            {
                // The card keeps its own status text; the next poll retries it.
            }
        }
    }

    /// <summary>
    /// Clears the paused-write state so a rescan can recover a device whose transport
    /// dropped. Without this the flag survives every rescan because every read path
    /// returns early while it is set.
    /// </summary>
    public void ClearRequiresRescan()
    {
        if (!RequiresRescan)
        {
            return;
        }

        RequiresRescan = false;
        _unsupported.Clear();
        ErrorText = string.Empty;
        OnCapabilityChanged();
        OnBasicStateChanged();
    }

    public Task LoadAsync(CancellationToken cancellationToken = default) =>
        LoadBasicStateAsync(cancellationToken);

}
