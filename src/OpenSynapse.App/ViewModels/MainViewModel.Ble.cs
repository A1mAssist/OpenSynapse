using System.Collections.ObjectModel;
using OpenSynapse.Core.Macros;
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

    /// <summary>Mice connect on selection; Pod mappings need a persistent input session.</summary>
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

        if (!await SyncLaylaAsync(devices, cancellationToken) || Volatile.Read(ref _disposed) != 0) return;

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
            var row = new RazerBleDeviceRowViewModel(device);
            if (_laylaDevices.TryGetValue(device.Id, out var pod)) row.SetBatteryPercent(pod.BatteryPercent);
            BleDevices.Add(row);
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
        if (Volatile.Read(ref _disposed) != 0) return;
        if (row.Device.ProductId == OpenSynapse.Windows.Protocols.RazerLaylaProtocol.ProductId)
        {
            await _laylaLifetime.RunAsync(async lifetime =>
            {
                if (Volatile.Read(ref _disposed) != 0 || !_laylaDevices.TryGetValue(row.Device.Id, out var cachedPod)) return;
                if (SelectedBle is { IsLayla: false } previousMouse) await previousMouse.DisposeAsync();
                lifetime.ThrowIfCancellationRequested();
                _selectedBleDevice = row;
                SelectedBle = cachedPod;
                await cachedPod.LoadAsync(lifetime);
                row.SetBatteryPercent(cachedPod.BatteryPercent);
            }, cancellationToken);
            return;
        }
        if (_selectedBleDevice is not null &&
            SelectedBle is not null &&
            StringComparer.OrdinalIgnoreCase.Equals(_selectedBleDevice.Device.Id, row.Device.Id))
        {
            await SelectedBle.LoadAsync(cancellationToken);
            row.SetBatteryPercent(SelectedBle.BatteryPercent);
            return;
        }

        if (SelectedBle is { IsLayla: false } previous)
        {
            await previous.DisposeAsync();
        }

        _selectedBleDevice = row;
        SelectedBle = new RazerBleDeviceViewModel(
            row.Device,
            message => _diagnosticLog.TryWrite("ble", message),
            () => _profile,
            SaveProfileAsync,
            () => Macros.Items.Select(macro => macro.Definition).ToArray(),
            Macros.TryApplyMacroBindingAsync,
            Macros.TryClearMacroTriggerAsync,
            () => SystemMacroCatalog.Bindings(GetActiveProfile().SystemMacros));
        await SelectedBle.LoadAsync(cancellationToken);
        row.SetBatteryPercent(SelectedBle.BatteryPercent);
    }

    public Task ApplyBlePollingRateAsync(CancellationToken cancellationToken = default) =>
        SelectedBle?.ApplyPollingRateAsync(cancellationToken) ?? Task.CompletedTask;

    public Task ApplyBleDpiAsync(CancellationToken cancellationToken = default) =>
        SelectedBle?.ApplyDpiAsync(cancellationToken) ?? Task.CompletedTask;
}
