using System.Collections.ObjectModel;
using OpenSynapse.Windows.Devices;

namespace OpenSynapse.App.ViewModels;

public sealed partial class MainViewModel
{
    private RazerBleDeviceRowViewModel? _selectedBleDevice;
    private RazerBleDeviceViewModel? _selectedBle;

    public ObservableCollection<RazerBleDeviceRowViewModel> BleDevices { get; } = new();

    public RazerBleDeviceViewModel? SelectedBle
    {
        get => _selectedBle;
        private set => SetField(ref _selectedBle, value);
    }

    /// <summary>Enumeration only: connecting waits until the user opens the row.</summary>
    private async Task RefreshBleAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<RazerBleDevice> devices;
        try
        {
            devices = await RazerBleDiscovery.FindAsync(cancellationToken);
        }
        catch (Exception exception) when (IsExpectedRuntimeException(exception))
        {
            _diagnosticLog.TryWrite("ble", $"discovery failed: {exception.Message}");
            return;
        }

        if (BleDevices.Count == devices.Count &&
            BleDevices.Zip(devices).All(pair =>
                StringComparer.OrdinalIgnoreCase.Equals(pair.First.Device.Id, pair.Second.Id)))
        {
            return;
        }

        var selectedId = _selectedBleDevice?.Device.Id;
        BleDevices.Clear();
        foreach (var device in devices)
        {
            BleDevices.Add(new RazerBleDeviceRowViewModel(device));
        }

        _selectedBleDevice = BleDevices.FirstOrDefault(row =>
            StringComparer.OrdinalIgnoreCase.Equals(row.Device.Id, selectedId));
        if (_selectedBleDevice is null && SelectedBle is { } stale)
        {
            SelectedBle = null;
            await stale.DisposeAsync();
        }
    }

    public async Task SelectBleAsync(
        RazerBleDeviceRowViewModel row,
        CancellationToken cancellationToken = default)
    {
        if (_selectedBleDevice is not null &&
            SelectedBle is not null &&
            StringComparer.OrdinalIgnoreCase.Equals(_selectedBleDevice.Device.Id, row.Device.Id))
        {
            await SelectedBle.LoadAsync(cancellationToken);
            return;
        }

        if (SelectedBle is { } previous)
        {
            await previous.DisposeAsync();
        }

        _selectedBleDevice = row;
        SelectedBle = new RazerBleDeviceViewModel(row.Device, message => _diagnosticLog.TryWrite("ble", message));
        await SelectedBle.LoadAsync(cancellationToken);
    }

    public Task ApplyBlePollingRateAsync(CancellationToken cancellationToken = default) =>
        SelectedBle?.ApplyPollingRateAsync(cancellationToken) ?? Task.CompletedTask;

    public Task ApplyBleDpiAsync(CancellationToken cancellationToken = default) =>
        SelectedBle?.ApplyDpiAsync(cancellationToken) ?? Task.CompletedTask;
}
