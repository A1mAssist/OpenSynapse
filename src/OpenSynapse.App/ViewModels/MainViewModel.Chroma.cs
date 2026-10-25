using System.Collections.ObjectModel;
using OpenSynapse.Windows.Devices;

namespace OpenSynapse.App.ViewModels;

public sealed partial class MainViewModel
{
    private RazerChromaDeviceRowViewModel? _selectedChromaDevice;
    private RazerChromaViewModel? _selectedChroma;

    public ObservableCollection<RazerChromaDeviceRowViewModel> ChromaDevices { get; } = new();

    public RazerChromaViewModel? SelectedChroma
    {
        get => _selectedChroma;
        private set => SetField(ref _selectedChroma, value);
    }

    private async Task RefreshChromaAsync(CancellationToken cancellationToken)
    {
        RazerChromaDeviceConnection? connection;
        try
        {
            connection = await RazerChromaStandardService.FindAsync(
                message => _diagnosticLog.TryWrite("chroma", message), cancellationToken);
        }
        catch (Exception exception) when (IsExpectedRuntimeException(exception))
        {
            _diagnosticLog.TryWrite("chroma", $"discovery failed: {exception}");
            return;
        }

        if (connection is null)
        {
            if (ChromaDevices.Count == 0 && SelectedChroma is null) return;
            ChromaDevices.Clear();
            _selectedChromaDevice = null;
            SelectedChroma = null;
            return;
        }

        if (ChromaDevices.Count == 1 &&
            StringComparer.OrdinalIgnoreCase.Equals(
                ChromaDevices[0].Connection.DevicePath, connection.DevicePath) &&
            ChromaDevices[0].Connection.ProductId == connection.ProductId)
        {
            return;
        }

        ChromaDevices.Clear();
        ChromaDevices.Add(new RazerChromaDeviceRowViewModel(connection));
        if (_selectedChromaDevice is null ||
            !StringComparer.OrdinalIgnoreCase.Equals(
                _selectedChromaDevice.Connection.DevicePath, connection.DevicePath))
        {
            _selectedChromaDevice = null;
            SelectedChroma = null;
        }
    }

    public async Task SelectChromaAsync(
        RazerChromaDeviceRowViewModel row,
        CancellationToken cancellationToken = default)
    {
        if (_selectedChromaDevice is not null &&
            SelectedChroma is not null &&
            StringComparer.OrdinalIgnoreCase.Equals(
                _selectedChromaDevice.Connection.DevicePath, row.Connection.DevicePath))
        {
            return;
        }

        _selectedChromaDevice = row;
        SelectedChroma = new RazerChromaViewModel(
            row.Connection, message => _diagnosticLog.TryWrite("chroma", message));
        await SelectedChroma.LoadAsync(cancellationToken);
    }
}
