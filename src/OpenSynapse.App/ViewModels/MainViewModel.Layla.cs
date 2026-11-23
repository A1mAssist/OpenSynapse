using OpenSynapse.Core.Sensors;
using OpenSynapse.Core.Profiles;
using OpenSynapse.Windows.Devices;
using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.App.ViewModels;

public sealed partial class MainViewModel
{
    private readonly Dictionary<string, RazerBleDeviceViewModel> _laylaDevices = new(StringComparer.OrdinalIgnoreCase);
    private readonly RazerLaylaLifetime _laylaLifetime = new();

    private Task<bool> SyncLaylaAsync(IReadOnlyList<RazerBleDevice> devices, CancellationToken token) =>
        _laylaLifetime.RunAsync(async lifetime =>
    {
        if (Volatile.Read(ref _disposed) != 0) return;
        var pods = devices.Where(device => device.ProductId == RazerLaylaProtocol.ProductId).ToArray();
        foreach (var stale in _laylaDevices.Keys.Where(id => !pods.Any(device => StringComparer.OrdinalIgnoreCase.Equals(device.Id, id))).ToArray())
        {
            await _laylaDevices[stale].DisposeAsync();
            _laylaDevices.Remove(stale);
        }
        foreach (var device in pods)
        {
            lifetime.ThrowIfCancellationRequested();
            if (!_laylaDevices.TryGetValue(device.Id, out var vm))
            {
                vm = new(device, message => _diagnosticLog.TryWrite("layla", message), () => _profile, SaveProfileAsync);
                _laylaDevices.Add(device.Id, vm);
            }
            if (vm.Layla?.IsRunning != true) await vm.LoadAsync(lifetime);
            BleDevices.FirstOrDefault(row => StringComparer.OrdinalIgnoreCase.Equals(row.Device.Id, device.Id))?.SetBatteryPercent(vm.BatteryPercent);
        }
    }, token);

    [NotProfileScoped]
    private async Task ApplyAllBleProfilesAsync(PowerSourceKind power, CancellationToken token)
    {
        if (!await _laylaLifetime.RunAsync(async lifetime =>
        {
            if (Volatile.Read(ref _disposed) != 0) return;
            foreach (var pod in _laylaDevices.Values.ToArray())
            {
                lifetime.ThrowIfCancellationRequested();
                await pod.ApplyPersistedProfileAsync(power, lifetime);
            }
        }, token) || Volatile.Read(ref _disposed) != 0) return;
        if (SelectedBle is { IsLayla: false } mouse) await mouse.ApplyPersistedProfileAsync(power, token);
    }

    private void OnLaylaMacrosChanged()
    {
        if (Volatile.Read(ref _disposed) != 0) return;
        // Invalidate before waiting for discovery/profile work: no stale clone may run in that gap.
        foreach (var pod in _laylaDevices.Values.ToArray()) pod.Layla?.InvalidateMacros();
        _ = RefreshLaylaMacrosAsync();
    }

    private async Task RefreshLaylaMacrosAsync()
    {
        try
        {
            await _laylaLifetime.RunAsync(async lifetime =>
            {
                if (Volatile.Read(ref _disposed) != 0) return;
                foreach (var pod in _laylaDevices.Values.ToArray())
                {
                    lifetime.ThrowIfCancellationRequested();
                    if (pod.Layla is { } layla) await layla.ApplyProfileAsync(lifetime);
                }
            });
        }
        catch (Exception exception) { _diagnosticLog.TryWrite("layla", $"macro refresh failed: {exception.Message}"); }
    }

    private Task DisposeLaylaAsync() => _laylaLifetime.StopAsync(async () =>
    {
        foreach (var pod in _laylaDevices.Values.ToArray()) await pod.DisposeAsync();
        _laylaDevices.Clear();
    });

    private void BeginStopLayla()
    {
        _laylaLifetime.BeginStop();
        foreach (var pod in _laylaDevices.Values.ToArray()) pod.StopLaylaOperations();
    }
}
