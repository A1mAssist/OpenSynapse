using System.Collections.ObjectModel;
using OpenSynapse.Windows.Devices;

namespace OpenSynapse.App.ViewModels;

public sealed partial class MainViewModel
{
    private RazerApBleHeadsetRowViewModel? _selectedApBleHeadsetRow;
    private RazerApBleHeadsetViewModel? _selectedApBleHeadset;

    public ObservableCollection<RazerApBleHeadsetRowViewModel> ApBleHeadsetDevices { get; } = new();

    public RazerApBleHeadsetViewModel? SelectedApBleHeadset
    {
        get => _selectedApBleHeadset;
        private set => SetField(ref _selectedApBleHeadset, value);
    }

    /// <summary>Enumeration only: connecting waits until the user opens the row.</summary>
    private async Task RefreshApBleAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<RazerBleDevice> devices;
        try
        {
            devices = await RazerApBleDiscovery.FindAsync(cancellationToken);
        }
        catch (Exception exception) when (IsExpectedRuntimeException(exception))
        {
            _diagnosticLog.TryWrite("ap-ble", $"discovery failed: {exception.Message}");
            return;
        }

        if (ApBleHeadsetDevices.Count == devices.Count &&
            ApBleHeadsetDevices.Zip(devices).All(pair =>
                StringComparer.OrdinalIgnoreCase.Equals(pair.First.Device.Id, pair.Second.Id)))
        {
            return;
        }

        var selectedId = _selectedApBleHeadsetRow?.Device.Id;
        ApBleHeadsetDevices.Clear();
        foreach (var device in devices)
        {
            ApBleHeadsetDevices.Add(new RazerApBleHeadsetRowViewModel(device));
        }

        _selectedApBleHeadsetRow = ApBleHeadsetDevices.FirstOrDefault(row =>
            StringComparer.OrdinalIgnoreCase.Equals(row.Device.Id, selectedId));
        if (_selectedApBleHeadsetRow is null && SelectedApBleHeadset is { } stale)
        {
            SelectedApBleHeadset = null;
            await stale.DisposeAsync();
        }
    }

    public async Task SelectApBleHeadsetAsync(
        RazerApBleHeadsetRowViewModel row,
        CancellationToken cancellationToken = default)
    {
        if (_selectedApBleHeadsetRow is not null &&
            SelectedApBleHeadset is not null &&
            StringComparer.OrdinalIgnoreCase.Equals(_selectedApBleHeadsetRow.Device.Id, row.Device.Id))
        {
            await SelectedApBleHeadset.LoadAsync(cancellationToken);
            row.SetBatteryPercent(SelectedApBleHeadset.BatteryPercent);
            return;
        }

        if (SelectedApBleHeadset is { } previous)
        {
            await previous.DisposeAsync();
        }

        _selectedApBleHeadsetRow = row;
        SelectedApBleHeadset = new RazerApBleHeadsetViewModel(row.Device);
        await SelectedApBleHeadset.LoadAsync(cancellationToken);
        row.SetBatteryPercent(SelectedApBleHeadset.BatteryPercent);
    }
}
