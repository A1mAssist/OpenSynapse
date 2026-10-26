using System.ComponentModel;

namespace OpenSynapse.App.ViewModels;

public sealed partial class MainViewModel
{
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
        }
    }

    public async Task RefreshHueAsync(CancellationToken cancellationToken = default)
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
            _diagnosticLog.TryWrite("hue", $"Hue discovery failed: {exception}");
        }
    }
}
