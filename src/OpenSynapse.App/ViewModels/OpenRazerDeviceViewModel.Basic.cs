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
    private static readonly TimeSpan BatteryRefreshInterval = TimeSpan.FromSeconds(10);
    private DateTimeOffset _lastBatteryRefresh;

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
            _lastBatteryRefresh = DateTimeOffset.UtcNow;
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
            await LoadHapticScrollAsync(cancellationToken);
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

    /// <summary>
    /// Keeps the battery and charging readout current while the device page is open.
    /// The device watch loop already refreshes live readouts; without this, a mouse
    /// plugged in after the page opened kept showing its old charging state.
    /// </summary>
    public async Task RefreshBatteryStateAsync(CancellationToken cancellationToken = default)
    {
        if (_basicState is null || !IsReady || RequiresRescan || IsBasicBusy ||
            DateTimeOffset.UtcNow - _lastBatteryRefresh < BatteryRefreshInterval)
        {
            return;
        }

        _lastBatteryRefresh = DateTimeOffset.UtcNow;
        int? battery = null;
        bool? charging = null;
        try
        {
            if (Has(OpenRazerBackendCapability.BatteryRead))
            {
                battery = await _service.GetBatteryPercentAsync(Connection, cancellationToken);
            }
            if (Has(OpenRazerBackendCapability.ChargingRead))
            {
                charging = await _service.GetChargingAsync(Connection, cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is Win32Exception or IOException or
            InvalidOperationException or NotSupportedException or InvalidDataException)
        {
            // Keep the last known values; the next poll can recover without turning the
            // whole device page into an error.
            return;
        }

        if (battery is null && charging is null)
        {
            return;
        }

        _basicState = _basicState with
        {
            BatteryPercent = battery ?? _basicState.BatteryPercent,
            IsCharging = charging ?? _basicState.IsCharging,
        };
        OnBasicStateChanged();
    }

    public Task LoadAsync(CancellationToken cancellationToken = default) =>
        LoadBasicStateAsync(cancellationToken);

}
