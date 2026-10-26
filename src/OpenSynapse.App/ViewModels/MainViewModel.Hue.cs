using System.ComponentModel;
using OpenSynapse.Core.Sensors;

namespace OpenSynapse.App.ViewModels;

public sealed partial class MainViewModel
{
    private static readonly TimeSpan HueDiscoveryInterval = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan HueDiscoveryIntervalOnBattery = TimeSpan.FromMinutes(2);
    private PhilipsHueViewModel? _hue;

    public PhilipsHueViewModel Hue =>
        _hue ??= CreateHueViewModel();

    private PhilipsHueViewModel CreateHueViewModel()
    {
        var hue = new PhilipsHueViewModel(message => _diagnosticLog.TryWrite("hue", message));
        hue.PropertyChanged += OnHuePropertyChanged;
        return hue;
    }

    private void OnHuePropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName is nameof(PhilipsHueViewModel.DeviceStatusText) or
            nameof(PhilipsHueViewModel.DeviceIdentity) or
            nameof(PhilipsHueViewModel.IsReady))
        {
            RefreshDeviceOverviewVisibility();
            RefreshDeviceSelectorItems();
        }
    }

    private async Task ScanHueInBackgroundAsync(CancellationToken cancellationToken)
    {
        try
        {
            await Hue.ScanAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            _diagnosticLog.TryWrite("hue", $"background discovery failed: {exception}");
        }
    }

    public async Task RunHueDiscoveryLoopAsync(CancellationToken cancellationToken = default)
    {
        // A bridge can appear without a PC network change, so poll until one is known.
        while (!Hue.IsConfigured && !cancellationToken.IsCancellationRequested)
        {
            await ScanHueInBackgroundAsync(cancellationToken);
            if (Hue.IsConfigured) break;
            var interval = CurrentPowerSource == PowerSourceKind.Battery
                ? HueDiscoveryIntervalOnBattery
                : HueDiscoveryInterval;
            await Task.Delay(interval, cancellationToken);
        }
    }
}
