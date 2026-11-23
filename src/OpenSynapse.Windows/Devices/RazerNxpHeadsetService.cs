using System.ComponentModel;
using System.Collections.Concurrent;
using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.Windows.Devices;

public sealed class RazerNxpHeadsetService : IAsyncDisposable
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> WriteGates = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _writeGate;
    private readonly IRazerNxpSerialTransport _transport;
    private readonly Action<string>? _diagnostic;

    public RazerNxpHeadsetService(string devicePath, Action<string>? diagnostic = null)
        : this(new RazerNxpSerialTransport(devicePath), diagnostic,
            WriteGates.GetOrAdd(devicePath, _ => new SemaphoreSlim(1, 1))) { }

    internal RazerNxpHeadsetService(IRazerNxpSerialTransport transport, Action<string>? diagnostic = null,
        SemaphoreSlim? writeGate = null)
    {
        _transport = transport;
        _diagnostic = diagnostic;
        _writeGate = writeGate ?? new SemaphoreSlim(1, 1);
    }

    public async Task<RazerHeadsetAudioState> ReadStateAsync(CancellationToken cancellationToken = default)
    {
        var errors = new List<string>();
        var link = await ProbeAsync("link", async () => RazerNxpHeadsetProtocol.ReadLinkStatus(
            await QueryAsync(RazerNxpHeadsetProtocol.GetLinkStatus(), cancellationToken)), errors);
        // A receiver can answer dock information while its headset is powered off.
        var dockFirmware = await ProbeAsync("dock firmware", async () => RazerNxpHeadsetProtocol.ReadFirmware(
            await QueryAsync(RazerNxpHeadsetProtocol.GetFirmware(true), cancellationToken), true), errors);
        var state = new RazerHeadsetAudioState(null, null, null, null, null, null, null, null,
            null, null, null, null, errors) { LinkStatus = link.Ok ? link.Value : null, DockFirmware = dockFirmware.Value };
        if (link.Ok && link.Value == 0) return state;
        var firmware = await ProbeAsync("firmware", async () => RazerNxpHeadsetProtocol.ReadFirmware(
            await QueryAsync(RazerNxpHeadsetProtocol.GetFirmware(), cancellationToken)), errors);
        var serial = await ProbeAsync("serial", async () => RazerNxpHeadsetProtocol.ReadSerial(
            await QueryAsync(RazerNxpHeadsetProtocol.GetSerial(), cancellationToken)), errors);
        var edition = await ProbeAsync("edition", async () => RazerNxpHeadsetProtocol.ReadEdition(
            await QueryAsync(RazerNxpHeadsetProtocol.GetEdition(), cancellationToken)), errors);
        var battery = await ProbeAsync("battery", () => ReadBatteryAsync(cancellationToken), errors);
        var sidetone = await ProbeAsync("sidetone", () => ReadSidetoneAsync(cancellationToken), errors);
        var silence = await ProbeAsync("silence detection", () => ReadSilenceDetectionAsync(cancellationToken), errors);
        var haptics = await ProbeAsync("haptics", () => ReadHapticsAsync(cancellationToken), errors);
        return state with
        {
            Firmware = firmware.Value,
            SerialNumber = serial.Value,
            Edition = edition.Ok ? edition.Value : null,
            BatteryPercent = battery.Ok ? battery.Value.Level : null,
            Charging = battery.Ok ? battery.Value.Charging : null,
            MicReturnSwitch = sidetone.Ok ? sidetone.Value.Enabled : null,
            SidetonePercent = sidetone.Ok ? sidetone.Value.Percent : null,
            AutoPause = silence.Ok ? silence.Value.Enabled : null,
            SilenceDetectionPeriod = silence.Ok ? silence.Value.Seconds : null,
            Haptics = haptics.Ok ? haptics.Value : null,
        };
    }

    public async Task<bool?> TryReadLinkStatusAsync(CancellationToken cancellationToken = default)
    {
        var link = await ProbeAsync("link", async () => RazerNxpHeadsetProtocol.ReadLinkStatus(
            await QueryAsync(RazerNxpHeadsetProtocol.GetLinkStatus(), cancellationToken)), []);
        return link.Ok ? link.Value is 1 or 2 : null;
    }

    public async Task<int?> TryReadBatteryPercentAsync(CancellationToken cancellationToken = default)
    {
        var battery = await ProbeAsync("battery", () => ReadBatteryAsync(cancellationToken), []);
        return battery.Ok ? battery.Value.Level : null;
    }

    private async Task<(byte Level, bool Charging)> ReadBatteryAsync(CancellationToken cancellationToken) =>
        RazerNxpHeadsetProtocol.ReadBattery(await QueryAsync(RazerNxpHeadsetProtocol.GetBattery(), cancellationToken));

    public async Task<(bool Enabled, byte Percent)> ReadSidetoneAsync(CancellationToken cancellationToken = default) =>
        RazerNxpHeadsetProtocol.ReadSidetone(await QueryAsync(RazerNxpHeadsetProtocol.GetSidetone(), cancellationToken));

    public async Task<(bool Enabled, ushort Seconds)> ReadSilenceDetectionAsync(CancellationToken cancellationToken = default) =>
        RazerNxpHeadsetProtocol.ReadSilenceDetection(await QueryAsync(RazerNxpHeadsetProtocol.GetSilenceDetection(), cancellationToken));

    public async Task<RazerHapticsControlState> ReadHapticsAsync(CancellationToken cancellationToken = default) =>
        RazerNxpHeadsetProtocol.ReadHaptics(await QueryAsync(RazerNxpHeadsetProtocol.GetHaptics(), cancellationToken));

    public Task SetSidetoneAsync(bool enabled, int percent, CancellationToken cancellationToken = default) =>
        UpdateSidetoneAsync(enabled, percent, cancellationToken);

    public Task<(bool Enabled, byte Percent)> UpdateSidetoneAsync(bool? enabled, int? percent,
        CancellationToken cancellationToken = default) =>
        UpdateAsync(ReadSidetoneAsync,
            (previous, token) => SetSidetoneCoreAsync(enabled ?? previous.Enabled, percent ?? previous.Percent, token),
            cancellationToken);

    private async Task SetSidetoneCoreAsync(bool enabled, int percent, CancellationToken cancellationToken)
    {
        await QueryAsync(RazerNxpHeadsetProtocol.SetSidetone(enabled, percent), cancellationToken);
        var actual = await ReadSidetoneAsync(cancellationToken);
        if (actual != (enabled, RazerNxpHeadsetProtocol.QuantizeSidetone(percent)))
            throw new IOException("NXP sidetone read-back did not match the requested value.");
    }

    public Task SetAutoPauseAsync(bool enabled, ushort seconds, CancellationToken cancellationToken = default) =>
        UpdateAutoPauseAsync(enabled, seconds, cancellationToken);

    public Task<(bool Enabled, ushort Seconds)> UpdateAutoPauseAsync(bool? enabled, ushort? seconds,
        CancellationToken cancellationToken = default) =>
        UpdateAsync(ReadSilenceDetectionAsync,
            (previous, token) => SetAutoPauseCoreAsync(enabled ?? previous.Enabled, seconds ?? previous.Seconds, token),
            cancellationToken);

    private async Task SetAutoPauseCoreAsync(bool enabled, ushort seconds, CancellationToken cancellationToken)
    {
        await QueryAsync(RazerNxpHeadsetProtocol.SetSilenceDetection(enabled, seconds), cancellationToken);
        if (await ReadSilenceDetectionAsync(cancellationToken) != (enabled, seconds))
            throw new IOException("NXP silence detection read-back did not match the requested value.");
    }

    public Task SetHapticsAsync(bool enabled, ushort intensity, CancellationToken cancellationToken = default) =>
        UpdateHapticsAsync(enabled, intensity, cancellationToken);

    public Task<RazerHapticsControlState> UpdateHapticsAsync(bool? enabled, int? intensity,
        CancellationToken cancellationToken = default)
    {
        if (intensity is { } value)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(value);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(value, byte.MaxValue);
        }
        return UpdateAsync(ReadHapticsAsync,
            (previous, token) => SetHapticsCoreAsync(enabled ?? previous.Enabled,
                (ushort)(intensity ?? previous.Strength), token), cancellationToken);
    }

    private async Task SetHapticsCoreAsync(bool enabled, ushort intensity, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfGreaterThan(intensity, byte.MaxValue);
        await QueryAsync(RazerNxpHeadsetProtocol.SetHaptics(enabled, (byte)intensity), cancellationToken);
        if (await ReadHapticsAsync(cancellationToken) != new RazerHapticsControlState(enabled, intensity))
            throw new IOException("NXP haptics read-back did not match the requested value.");
    }

    private async Task<T> UpdateAsync<T>(Func<CancellationToken, Task<T>> read,
        Func<T, CancellationToken, Task> write, CancellationToken cancellationToken)
    {
        // Only writes own this gate: preserve unspecified fields across concurrent page/profile updates.
        await _writeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var previous = await read(cancellationToken).ConfigureAwait(false);
            await write(previous, cancellationToken).ConfigureAwait(false);
            return previous;
        }
        finally { _writeGate.Release(); }
    }

    private async Task<byte[]> QueryAsync(byte[] request, CancellationToken cancellationToken)
    {
        var response = await _transport.QueryAsync(request, cancellationToken).ConfigureAwait(false);
        RazerNxpHeadsetProtocol.ValidateResponse(request, response);
        return response;
    }

    private async Task<(bool Ok, T Value)> ProbeAsync<T>(string label, Func<Task<T>> read, List<string> errors)
    {
        try { return (true, await read().ConfigureAwait(false)); }
        catch (Exception exception) when (exception is IOException or InvalidDataException or Win32Exception or UnauthorizedAccessException)
        {
            errors.Add($"{label}: {exception.Message}");
            _diagnostic?.Invoke($"NXP {label}: {exception.Message}");
            return (false, default!);
        }
    }

    public ValueTask DisposeAsync() => _transport.DisposeAsync();
}
