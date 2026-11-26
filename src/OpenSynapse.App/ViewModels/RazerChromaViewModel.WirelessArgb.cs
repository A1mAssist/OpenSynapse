using System.Collections.ObjectModel;
using System.ComponentModel;
using Microsoft.UI.Xaml;
using OpenSynapse.Core.Profiles;
using OpenSynapse.Windows.Devices;
using OpenSynapse.Windows.Protocols;
using Windows.UI;

namespace OpenSynapse.App.ViewModels;

public sealed class WirelessArgbPortViewModel : INotifyPropertyChanged
{
    private Color _color;

    internal WirelessArgbPortViewModel(byte index, Color color)
    {
        Index = index;
        _color = color;
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public byte Index { get; }
    public string Label => AppStrings.FormatText("RazerWirelessArgbPortLabel", Index + 1);

    public Color Color
    {
        get => _color;
        set
        {
            if (_color == value) return;
            _color = value;
            PropertyChanged?.Invoke(this, new(nameof(Color)));
        }
    }

    public void RefreshLocalization() =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Label)));
}

public sealed partial class RazerChromaViewModel
{
    private RazerWirelessArgbService? _wirelessArgb;
    private RazerWirelessArgbConnection? _wirelessArgbConnection;

    public ObservableCollection<WirelessArgbPortViewModel> WirelessArgbPorts { get; } =
        new()
        {
            new(0, Color.FromArgb(255, 255, 0, 0)),
            new(1, Color.FromArgb(255, 0, 255, 0)),
            new(2, Color.FromArgb(255, 0, 0, 255)),
        };

    public Visibility WirelessArgbVisibility =>
        _wirelessArgbConnection is null ? Visibility.Collapsed : Visibility.Visible;

    public bool CanApplyWirelessArgb => !IsBusy && _wirelessArgbConnection is not null;

    private async Task LoadWirelessArgbAsync(CancellationToken cancellationToken)
    {
        if (!RazerChromaStandardService.IsWirelessArgbProduct(_connection.ProductId)) return;

        _wirelessArgb ??= new(_diagnostic);
        _wirelessArgbConnection = await RazerWirelessArgbService.FindAsync(
            _connection,
            _diagnostic,
            cancellationToken).ConfigureAwait(false);
        OnPropertyChanged(nameof(WirelessArgbVisibility));
        OnPropertyChanged(nameof(CanApplyWirelessArgb));
    }

    [NotProfileScoped]
    public async Task ApplyWirelessArgbAsync(CancellationToken cancellationToken = default)
    {
        if (_wirelessArgbConnection is not { } connection || !BeginOperation()) return;
        try
        {
            var service = _wirelessArgb!;
            foreach (var port in WirelessArgbPorts)
            {
                await service.SetPortColorAsync(
                    connection,
                    port.Index,
                    new RazerHidColor(port.Color.R, port.Color.G, port.Color.B),
                    cancellationToken).ConfigureAwait(false);
            }

            ErrorText = string.Empty;
            OperationStatusText = AppStrings.Text("BlackSharkSaved");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception) when (IsExpectedException(exception))
        {
            ErrorText = exception.Message;
            OperationStatusText = string.Empty;
        }
        finally
        {
            IsBusy = false;
        }
    }
}
