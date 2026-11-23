using System.Collections.Concurrent;
using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.Windows.Devices;

public sealed partial class RazerHeadsetAudioService
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> CarolWriteGates = new(StringComparer.OrdinalIgnoreCase);
    private SemaphoreSlim CarolWriteGate => CarolWriteGates.GetOrAdd(_devicePath, _ => new SemaphoreSlim(1, 1));
    public async Task<bool?> TryReadCarolLinkStatusAsync(CancellationToken cancellationToken = default)
    {
        RequireCarol();
        var link = await ProbeProtocol25Async("link", RazerCarolAudioProtocol.GetLink,
            RazerCarolAudioProtocol.ReadLink, [], cancellationToken).ConfigureAwait(false);
        return link.Ok ? link.Value != 0 : null;
    }

    private async Task<RazerHeadsetAudioState> ReadCarolStateAsync(RazerHeadsetAudioState state,
        List<string> errors, CancellationToken token)
    {
        var power = await ProbeCarolPowerAsync(errors, token).ConfigureAwait(false);
        var externalPower = _productId == 0x05A2
            ? await ProbeProtocol25Async("external power", RazerCarolAudioProtocol.GetExternalPower,
                RazerCarolAudioProtocol.ReadExternalPower, errors, token).ConfigureAwait(false)
            : (Ok: false, Value: (byte)0);
        var link = await ProbeProtocol25Async("link", RazerCarolAudioProtocol.GetLink,
            RazerCarolAudioProtocol.ReadLink, errors, token).ConfigureAwait(false);
        var battery = RazerCarolAudioProtocol.HasBattery(_productId) && link.Ok && link.Value != 0
            ? await ProbeProtocol25Async("battery", RazerHeadsetAudioProtocol.GetBatteryLevel,
                RazerHeadsetAudioProtocol.ReadBatteryPercent, errors, token).ConfigureAwait(false)
            : (Ok: false, Value: (byte)0);
        var charging = RazerCarolAudioProtocol.HasBattery(_productId) && link.Ok && link.Value != 0
            ? await ProbeProtocol25Async("charging", RazerHeadsetAudioProtocol.GetChargingStatus,
                RazerCarolAudioProtocol.ReadCharging, errors, token).ConfigureAwait(false)
            : (Ok: false, Value: false);
        return state with
        {
            LinkStatus = link.Ok ? link.Value : null,
            BatteryPercent = battery.Ok ? battery.Value : null,
            Charging = charging.Ok ? charging.Value : null,
            CarolPower = power,
            ExternalPowerStatus = externalPower.Ok ? externalPower.Value : null,
        };
    }

    private async Task<RazerCarolPowerState?> ProbeCarolPowerAsync(List<string> errors, CancellationToken token)
    {
        try { return await ReadCarolPowerAsync(token).ConfigureAwait(false); }
        catch (Exception exception) when (IsExpected(exception))
        {
            errors.Add($"auto sleep: {exception.Message}");
            return null;
        }
    }

    public async Task<int?> TryReadCarolBatteryPercentAsync(CancellationToken cancellationToken = default)
    {
        RequireCarol();
        if (!RazerCarolAudioProtocol.HasBattery(_productId)) return null;
        var errors = new List<string>();
        var link = await ProbeProtocol25Async("link", RazerCarolAudioProtocol.GetLink,
            RazerCarolAudioProtocol.ReadLink, errors, cancellationToken).ConfigureAwait(false);
        if (!link.Ok || link.Value == 0) return null;
        var battery = await ProbeProtocol25Async("battery", RazerHeadsetAudioProtocol.GetBatteryLevel,
            RazerHeadsetAudioProtocol.ReadBatteryPercent, errors, cancellationToken).ConfigureAwait(false);
        return battery.Ok ? battery.Value : null;
    }

    public async Task<RazerCarolPowerState> ReadCarolPowerAsync(CancellationToken cancellationToken = default)
    {
        RequireCarol();
        var sleep = RazerCarolAudioProtocol.ReadAutoSleep(await QueryProtocol25Async(
            RazerCarolAudioProtocol.GetAutoSleep, cancellationToken).ConfigureAwait(false));
        var seconds = RazerCarolAudioProtocol.ReadSleepSeconds(await QueryProtocol25Async(
            RazerCarolAudioProtocol.GetSleepSeconds, cancellationToken).ConfigureAwait(false));
        return new(sleep.Enabled, seconds, sleep.Status);
    }

    public async Task SetCarolPowerAsync(bool? enabled, int? minutes, CancellationToken cancellationToken = default)
    {
        RequireCarol();
        if (minutes is { } value && value is < 15 or > 60)
            throw new ArgumentOutOfRangeException(nameof(minutes), "Carol sleep time must be 15–60 minutes.");
        var gate = CarolWriteGate;
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try { await SetCarolPowerCoreAsync(enabled, minutes, cancellationToken).ConfigureAwait(false); }
        finally { gate.Release(); }
    }

    public async Task RestoreCarolPowerAsync(RazerCarolPowerState state, CancellationToken cancellationToken = default)
    {
        RequireCarol();
        var gate = CarolWriteGate;
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try { await WriteCarolPowerAsync(state.Enabled, state.Seconds, cancellationToken).ConfigureAwait(false); }
        finally { gate.Release(); }
    }

    private async Task SetCarolPowerCoreAsync(bool? enabled, int? minutes, CancellationToken cancellationToken)
    {
        var previous = await ReadCarolPowerAsync(cancellationToken).ConfigureAwait(false);
        var targetEnabled = enabled ?? previous.Enabled;
        var targetSeconds = minutes is { } duration ? (ushort)(duration * 60) : previous.Seconds;
        try
        {
            await WriteCarolPowerAsync(targetEnabled, targetSeconds, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            try { await WriteCarolPowerAsync(previous.Enabled, previous.Seconds, CancellationToken.None).ConfigureAwait(false); }
            catch (Exception exception) when (IsExpected(exception))
            { _diagnostic?.Invoke($"restore Carol power failed: {exception.Message}"); }
            throw;
        }
    }

    private async Task WriteCarolPowerAsync(bool enabled, ushort seconds, CancellationToken token)
    {
        await QueryProtocol25Async(txn => RazerCarolAudioProtocol.SetAutoSleep(txn, enabled), token).ConfigureAwait(false);
        await QueryProtocol25Async(txn => RazerCarolAudioProtocol.SetSleepSeconds(txn, seconds), token).ConfigureAwait(false);
        var actual = await ReadCarolPowerAsync(token).ConfigureAwait(false);
        if (actual.Enabled != enabled || actual.Seconds != seconds)
            throw new InvalidOperationException("Carol auto-sleep readback did not match the requested values.");
    }

    public Task KeepCarolAwakeAsync(CancellationToken cancellationToken = default)
    {
        RequireCarol();
        return QueryProtocol25Async(RazerCarolAudioProtocol.KeepAwake, cancellationToken);
    }

    public async Task MaintainCarolPowerAsync(CancellationToken cancellationToken = default)
    {
        RequireCarol();
        var gate = CarolWriteGate;
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var sleep = RazerCarolAudioProtocol.ReadAutoSleep(await QueryProtocol25Async(
                RazerCarolAudioProtocol.GetAutoSleep, cancellationToken).ConfigureAwait(false));
            if (!sleep.Enabled) await KeepCarolAwakeAsync(cancellationToken).ConfigureAwait(false);
        }
        finally { gate.Release(); }
    }

    private void RequireCarol() => RequireProtocol25Support(
        _protocol25Transport is not null && RazerCarolAudioProtocol.IsSupported(_productId), "Carol power");
}
