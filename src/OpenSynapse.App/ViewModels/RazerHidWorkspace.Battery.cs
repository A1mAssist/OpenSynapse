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
    }

    internal async Task RefreshBatteriesAsync(CancellationToken cancellationToken)
    {
        foreach (var row in Devices.Where(row => row.Connection.IsReady &&
                     row.HardwareCategory != DeviceCategory.Laptop &&
                     row.Connection.Capabilities.Contains(RazerHidBackendCapability.BatteryRead)).ToArray())
        {
            await ReadBatteryPercentAsync(row, cancellationToken);
        }
    }

    internal async Task<int?> ReadBatteryPercentAsync(
        RazerHidDeviceRowViewModel row,
        CancellationToken cancellationToken)
    {
        if (_deviceService is null || !row.Connection.IsReady) return row.BatteryPercent;
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
                percent = await _deviceService.GetBatteryPercentAsync(row.Connection, timeout.Token)
                    .ConfigureAwait(false);
            }
            catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
            {
                failure = exception.Message;
            }

            if (!timeout.IsCancellationRequested &&
                row.Connection.Capabilities.Contains(RazerHidBackendCapability.ChargingRead))
            {
                try
                {
                    charging = await _deviceService.GetChargingAsync(row.Connection, timeout.Token)
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
