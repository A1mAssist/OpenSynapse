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
            else if (state.UnsupportedCapabilities.Count > 0)
            {
                OnCapabilityChanged();
            }
            OnBasicStateChanged();
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

    public Task LoadAsync(CancellationToken cancellationToken = default) =>
        LoadBasicStateAsync(cancellationToken);

}
