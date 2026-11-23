using Microsoft.UI.Xaml;
using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.App.ViewModels;

public sealed partial class RazerBleDeviceViewModel
{
    private bool _laylaStopped;
    public bool IsLayla => _device.ProductId == RazerLaylaProtocol.ProductId;
    public RazerLaylaViewModel? Layla { get; private set; }
    public Visibility LaylaVisibility => IsLayla ? Visibility.Visible : Visibility.Collapsed;
    public Visibility MouseVisibility => IsLayla ? Visibility.Collapsed : Visibility.Visible;

    private async Task LoadLaylaAsync(CancellationToken token)
    {
        if (_laylaStopped) return;
        if (!BeginOperation()) return;
        try
        {
            if (_getProfile is null || _saveProfile is null) throw new InvalidOperationException("Control Pod profile storage is unavailable.");
            if (Layla is null)
            {
                Layla = new(_device, _getProfile, _saveProfile, _diagnostic);
                Layla.PropertyChanged += (_, _) =>
                {
                    if (Layla is not null) ErrorText = Layla.ErrorText;
                };
                OnPropertyChanged(nameof(Layla));
            }
            var layla = Layla;
            await layla.LoadAsync(token);
            if (_laylaStopped) return;
            if (layla.State is { } state)
            {
                _firmware = state.Firmware; _serial = state.Serial; _batteryPercent = state.BatteryPercent;
            }
            ErrorText = layla.ErrorText;
            OnPropertyChanged(string.Empty);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        finally { EndOperation(); }
    }

    internal void StopLaylaOperations()
    {
        _laylaStopped = true;
        Layla?.CancelOperations();
    }
}
