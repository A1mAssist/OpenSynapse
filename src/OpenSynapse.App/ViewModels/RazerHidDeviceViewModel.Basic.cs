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
                if (state.UnsupportedCapabilities.Contains(RazerHidBackendCapability.BrightnessRead))
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
        var battery = await TryReadAsync(
            RazerHidBackendCapability.BatteryRead,
            () => _service.GetBatteryPercentAsync(Connection, cancellationToken));
        var charging = await TryReadAsync(
            RazerHidBackendCapability.ChargingRead,
            () => _service.GetChargingAsync(Connection, cancellationToken));

        async Task<T?> TryReadAsync<T>(RazerHidBackendCapability capability, Func<Task<T>> read)
            where T : struct
        {
            if (!Has(capability))
            {
                return null;
            }
            try
            {
                return await read();
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception) when (exception is Win32Exception or IOException or
                InvalidOperationException or NotSupportedException or InvalidDataException)
            {
                // One side timing out must not discard the other side's fresh reading.
                return null;
            }
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
