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
        IReadOnlyList<RazerChromaDeviceConnection> connections;
        try
        {
            connections = await RazerChromaStandardService.FindAllAsync(
                message => _diagnosticLog.TryWrite("chroma", message), cancellationToken);
        }
        catch (Exception exception) when (IsExpectedRuntimeException(exception))
        {
            _diagnosticLog.TryWrite("chroma", $"discovery failed: {exception}");
            return;
        }

        if (connections.Count == 0)
        {
            if (ChromaDevices.Count == 0 && SelectedChroma is null) return;
            ChromaDevices.Clear();
            _selectedChromaDevice = null;
            SelectedChroma = null;
            return;
        }

        if (ChromaDevices.Count == connections.Count &&
            ChromaDevices.Zip(connections).All(pair =>
                StringComparer.OrdinalIgnoreCase.Equals(
                    pair.First.Connection.DevicePath, pair.Second.DevicePath) &&
                pair.First.Connection.ProductId == pair.Second.ProductId))
        {
            return;
        }

        var selectedPath = _selectedChromaDevice?.Connection.DevicePath;
        ChromaDevices.Clear();
        foreach (var connection in connections)
        {
            ChromaDevices.Add(new RazerChromaDeviceRowViewModel(connection));
        }

        _selectedChromaDevice = ChromaDevices.FirstOrDefault(row =>
            StringComparer.OrdinalIgnoreCase.Equals(row.Connection.DevicePath, selectedPath));
        if (_selectedChromaDevice is null)
        {
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
