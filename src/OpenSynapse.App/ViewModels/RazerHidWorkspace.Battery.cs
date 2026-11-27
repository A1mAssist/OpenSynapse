using System.Runtime.CompilerServices;
using OpenSynapse.Core.Devices;
using OpenSynapse.Windows.Devices;

namespace OpenSynapse.App.ViewModels;

public sealed partial class RazerHidWorkspace
{
    private static readonly TimeSpan BatteryReadInterval = TimeSpan.FromSeconds(10);
    private readonly ConditionalWeakTable<RazerHidDeviceRowViewModel, BatteryReadState> _batteryReads = new();

    // The watch, taskbar and tray share one read per device; rescans retire the old row's state.
    private sealed class BatteryReadState
    {
        public readonly SemaphoreSlim Gate = new(1, 1);
        public DateTimeOffset LastAttempt;
        public DateTimeOffset LastDiagnostic;
        public bool Failed;
        public bool WakeDetected;
    }

    internal async Task<bool> RefreshBatteriesAsync(CancellationToken cancellationToken)
    {
        foreach (var row in Devices.Where(row => row.CanReadBattery &&
                     row.HardwareCategory != DeviceCategory.Laptop).ToArray())
        {
            await ReadBatteryPercentAsync(row, cancellationToken);
        }

        // Consume each successful wake read once so a failed discovery cannot spin.
        var needsDiscovery = false;
        foreach (var row in Devices)
        {
            if (!row.Connection.IsReady && _batteryReads.TryGetValue(row, out var state) && state.WakeDetected)
            {
                state.WakeDetected = false;
                needsDiscovery = true;
            }
        }
        return needsDiscovery;
    }

    internal async Task<int?> ReadBatteryPercentAsync(
        RazerHidDeviceRowViewModel row,
        CancellationToken cancellationToken)
    {
        var connection = row.BatteryReadConnection;
        if (_deviceService is null || !connection.IsReady) return row.BatteryPercent;
        var state = _batteryReads.GetOrCreateValue(row);
        await state.Gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (DateTimeOffset.UtcNow - state.LastAttempt < BatteryReadInterval)
                return row.BatteryPercent;

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(2));
            int? percent = null;
            bool? charging = null;
            string? failure = null;
            try
            {
                percent = await _deviceService.GetBatteryPercentAsync(connection, timeout.Token)
                    .ConfigureAwait(false);
            }
            catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
            {
                failure = exception.Message;
            }

            if (!timeout.IsCancellationRequested &&
                connection.Capabilities.Contains(RazerHidBackendCapability.ChargingRead))
            {
                try
                {
                    charging = await _deviceService.GetChargingAsync(connection, timeout.Token)
                        .ConfigureAwait(false);
                }
                catch (Exception) when (!cancellationToken.IsCancellationRequested)
                {
                    // An optional charging read must not discard a fresh battery percentage.
                }
            }

            cancellationToken.ThrowIfCancellationRequested();
            var now = DateTimeOffset.UtcNow;
            if (state.LastAttempt == default || state.Failed != (percent is null) ||
                percent is not null && percent != row.BatteryPercent ||
                now - state.LastDiagnostic >= TimeSpan.FromMinutes(1))
            {
                _diagnosticLog.TryWrite("battery-poll", percent is { } value
                    ? $"pid={row.Connection.Definition.ProductId:X4} read={value}% charging={charging}"
                    : $"pid={row.Connection.Definition.ProductId:X4} read failed; retaining last value and retrying: {failure}");
                state.LastDiagnostic = now;
            }
            state.Failed = percent is null;
            state.WakeDetected = percent is not null && !row.Connection.IsReady;
            state.LastAttempt = now;
            await RunOnDispatcherAsync(() =>
            {
                row.SetBatteryPercent(percent);
                row.SetCharging(charging);
                return Task.CompletedTask;
            }).ConfigureAwait(false);
            return percent ?? row.BatteryPercent;
        }
        finally
        {
            state.Gate.Release();
        }
    }
}
