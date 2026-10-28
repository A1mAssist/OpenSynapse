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

public sealed partial class OpenRazerDeviceViewModel : INotifyPropertyChanged
{
    public async Task LoadBasicStateAsync(CancellationToken cancellationToken = default)
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
                if (state.UnsupportedCapabilities.Contains(OpenRazerBackendCapability.BrightnessRead))
                    _unsupportedBrightnessReads.Add(SelectedLightingZone);
            }
            if (state.PollingRate is { } polling) SelectedPollingRate = polling;
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
            if (state.Errors.Count > 0)
            {
                MarkRequiresRescan();
            }
            else
            {
                ErrorText = string.Empty;
                if (state.UnsupportedCapabilities.Count > 0)
                {
                    OnCapabilityChanged();
                }
            }
            OnBasicStateChanged();
            await LoadMappingIfNeededAsync(cancellationToken);
            await LoadOnboardProfilesAsync(cancellationToken);
            await LoadWirelessAsync(cancellationToken);
            await LoadKnobAsync(cancellationToken);
            await LoadAnalogKeyAsync(cancellationToken);
            await LoadOledAsync(cancellationToken);
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
