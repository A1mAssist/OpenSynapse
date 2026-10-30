using System.Collections.ObjectModel;
using OpenSynapse.Windows.Devices;

namespace OpenSynapse.App.ViewModels;

public sealed partial class MainViewModel
{
    private RazerAwBleHeadsetRowViewModel? _selectedAwBleHeadsetRow;
    private RazerAwBleHeadsetViewModel? _selectedAwBleHeadset;

    public ObservableCollection<RazerAwBleHeadsetRowViewModel> AwBleHeadsetDevices { get; } = new();

    public RazerAwBleHeadsetViewModel? SelectedAwBleHeadset
    {
        get => _selectedAwBleHeadset;
        private set => SetField(ref _selectedAwBleHeadset, value);
    }

    /// <summary>Enumeration only: connecting waits until the user opens the row.</summary>
    private async Task RefreshAwBleAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<RazerBleDevice> devices;
        try
        {
            devices = await RazerAwBleDiscovery.FindAsync(cancellationToken);
        }
        catch (Exception exception) when (IsExpectedRuntimeException(exception))
        {
            _diagnosticLog.TryWrite("aw-ble", $"discovery failed: {exception.Message}");
            return;
        }

        if (AwBleHeadsetDevices.Count == devices.Count &&
            AwBleHeadsetDevices.Zip(devices).All(pair =>
                StringComparer.OrdinalIgnoreCase.Equals(pair.First.Device.Id, pair.Second.Id)))
        {
            return;
        }

        var selectedId = _selectedAwBleHeadsetRow?.Device.Id;
        AwBleHeadsetDevices.Clear();
        foreach (var device in devices)
        {
            AwBleHeadsetDevices.Add(new RazerAwBleHeadsetRowViewModel(device));
        }

        _selectedAwBleHeadsetRow = AwBleHeadsetDevices.FirstOrDefault(row =>
            StringComparer.OrdinalIgnoreCase.Equals(row.Device.Id, selectedId));
        if (_selectedAwBleHeadsetRow is null && SelectedAwBleHeadset is { } stale)
        {
            SelectedAwBleHeadset = null;
            await stale.DisposeAsync();
        }
    }

    public async Task SelectAwBleHeadsetAsync(
        RazerAwBleHeadsetRowViewModel row,
        CancellationToken cancellationToken = default)
    {
        if (_selectedAwBleHeadsetRow is not null &&
            SelectedAwBleHeadset is not null &&
            StringComparer.OrdinalIgnoreCase.Equals(_selectedAwBleHeadsetRow.Device.Id, row.Device.Id))
        {
            await SelectedAwBleHeadset.LoadAsync(cancellationToken);
            return;
        }

        if (SelectedAwBleHeadset is { } previous)
        {
            await previous.DisposeAsync();
        }

        _selectedAwBleHeadsetRow = row;
        SelectedAwBleHeadset = new RazerAwBleHeadsetViewModel(
            row.Device,
            message => _diagnosticLog.TryWrite("aw-ble", message));
        await SelectedAwBleHeadset.LoadAsync(cancellationToken);
    }
}
