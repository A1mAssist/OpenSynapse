using System.ComponentModel;
using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.Windows.Devices;

public sealed record RazerMixer053EState(
    bool Available,
    Version? Firmware,
    string? SerialNumber,
    byte? DeviceMode,
    byte? Brightness,
    RazerMixer053EHardwareStatus? Hardware,
    IReadOnlyList<string> Errors);

/// <summary>
/// Safe 053E HID subset: firmware, serial, device mode, brightness, channel mute and
/// microphone mute. Faders, routing, EQ, peak reads and the native mixer DLL are absent.
/// The frame layouts are bundle-aligned; no live 053E hardware was available to verify them.
/// </summary>
public sealed class RazerMixer053EService : IAsyncDisposable
{
    private static readonly TimeSpan ReadbackDelay = TimeSpan.FromMilliseconds(20);
    private static readonly TimeSpan BrightnessFrameDelay = TimeSpan.FromMilliseconds(5);
    private const int ReadbackAttempts = 3;

    private readonly IRazerMixer053ETransport _transport;
    private readonly Action<string>? _diagnostic;
    private readonly SemaphoreSlim _connectGate = new(1, 1);
    private int _connected;

    public RazerMixer053EService(string devicePath, Action<string>? diagnostic = null)
        : this(new RazerMixer053ETransport(devicePath), diagnostic)
    {
    }

    internal RazerMixer053EService(
        IRazerMixer053ETransport transport,
        Action<string>? diagnostic = null)
    {
        _transport = transport ?? throw new ArgumentNullException(nameof(transport));
        _diagnostic = diagnostic;
    }

    public static bool IsSupported(ushort productId) =>
        productId == RazerMixer053EProtocol.ProductId;

    /// <summary>
    /// Returns false when either the 64-byte feature channel or the report-9 status path
    /// does not answer. No fallback data is synthesized.
    /// </summary>
    public async Task<bool> ProbeAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            _ = await ReadHardwareStatusCoreAsync(cancellationToken).ConfigureAwait(false);
            _ = await ReadFirmwareCoreAsync(cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (Exception exception) when (IsExpected(exception))
        {
            _diagnostic?.Invoke($"053E capability probe failed: {exception.Message}");
            return false;
        }
    }

    public async Task<RazerMixer053EState> ReadStateAsync(
        CancellationToken cancellationToken = default)
    {
        var errors = new List<string>();
        var hardware = await TryReadHardwareStatusAsync(errors, cancellationToken).ConfigureAwait(false);
        var firmware = await TryReadAsync(
            "firmware",
            RazerMixer053EProtocol.CreateGetFirmware(),
            response => RazerMixer053EProtocol.MatchesFirmwareResponse(response),
            response => RazerMixer053EProtocol.ReadFirmware(response),
            errors,
            cancellationToken).ConfigureAwait(false);

        if (hardware is null || !firmware.Ok)
        {
            errors.Add("053E 64-byte feature/report-9 path is unavailable.");
            return new RazerMixer053EState(
                Available: false,
                Firmware: null,
                SerialNumber: null,
                DeviceMode: null,
                Brightness: null,
                Hardware: null,
                Errors: errors);
        }

        var serial = await TryReadAsync(
            "serial number",
            RazerMixer053EProtocol.CreateGetSerialNumber(),
            response => RazerMixer053EProtocol.MatchesSerialNumberResponse(response),
            response => RazerMixer053EProtocol.ReadSerialNumber(response),
            errors,
            cancellationToken).ConfigureAwait(false);
        var mode = await TryReadAsync(
            "device mode",
            RazerMixer053EProtocol.CreateGetDeviceMode(),
            response => RazerMixer053EProtocol.MatchesDeviceModeResponse(response),
            response => RazerMixer053EProtocol.ReadDeviceMode(response),
            errors,
            cancellationToken).ConfigureAwait(false);
        var brightness = await TryReadAsync(
            "brightness",
            CreateGetBrightnessRequest(0, 0),
            response => RazerMixer053EProtocol.MatchesBrightnessResponse(response),
            response => RazerMixer053EProtocol.ReadBrightness(response),
            errors,
            cancellationToken).ConfigureAwait(false);

        return new RazerMixer053EState(
            Available: true,
            Firmware: firmware.Value,
            SerialNumber: serial.Ok ? serial.Value : null,
            DeviceMode: mode.Ok ? mode.Value : null,
            Brightness: brightness.Ok ? brightness.Value : null,
            Hardware: hardware,
            Errors: errors);
    }

    public async Task<Version?> ReadFirmwareAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            return await ReadFirmwareCoreAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (IsExpected(exception))
        {
            Diagnose("firmware", exception);
            return null;
        }
    }

    public async Task<string?> ReadSerialNumberAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            return await ReadSerialNumberCoreAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (IsExpected(exception))
        {
            Diagnose("serial number", exception);
            return null;
        }
    }

    public async Task<byte?> ReadDeviceModeAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            return await ReadDeviceModeCoreAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (IsExpected(exception))
        {
            Diagnose("device mode", exception);
            return null;
        }
    }

    public async Task<byte?> ReadBrightnessAsync(
        byte profileId = 0,
        byte regionId = 0,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return await ReadBrightnessCoreAsync(profileId, regionId, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (IsExpected(exception))
        {
            Diagnose("brightness", exception);
            return null;
        }
    }

    public async Task<RazerMixer053EHardwareStatus?> ReadHardwareStatusAsync(
        CancellationToken cancellationToken = default)
    {
        try
        {
            return await ReadHardwareStatusCoreAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (IsExpected(exception))
        {
            Diagnose("hardware status", exception);
            return null;
        }
    }

    public async Task SetChannelMuteAsync(
        int channel,
        bool muted,
        CancellationToken cancellationToken = default)
    {
        if (channel is < 1 or > 4)
        {
            throw new ArgumentOutOfRangeException(nameof(channel), "053E fader channel must be 1..4.");
        }

        var previous = await ReadHardwareStatusCoreAsync(cancellationToken).ConfigureAwait(false);
        var wasMuted = previous.IsMuted(channel);
        if (wasMuted == muted)
        {
            return;
        }

        try
        {
            await SendFeatureAsync(
                RazerMixer053EProtocol.CreateSetChannelMute(channel, muted),
                cancellationToken).ConfigureAwait(false);
            var actual = await ReadHardwareStatusAfterWriteAsync(cancellationToken).ConfigureAwait(false);
            if (actual is null || actual.IsMuted(channel) != muted)
            {
                throw new InvalidOperationException("053E channel mute readback did not match.");
            }
        }
        catch
        {
            await TryRestoreChannelMuteAsync(channel, wasMuted).ConfigureAwait(false);
            throw;
        }
    }

    public async Task SetMicMuteAsync(
        bool muted,
        CancellationToken cancellationToken = default)
    {
        var previous = await ReadHardwareStatusCoreAsync(cancellationToken).ConfigureAwait(false);
        if (previous.MicMute == muted)
        {
            return;
        }

        try
        {
            await SendFeatureAsync(
                RazerMixer053EProtocol.CreateSetChannelMute(5, muted),
                cancellationToken).ConfigureAwait(false);
            await SendOutputAsync(
                RazerMixer053EProtocol.CreateMicMuteCompanion(muted),
                cancellationToken).ConfigureAwait(false);

            var actual = await ReadHardwareStatusAfterWriteAsync(cancellationToken).ConfigureAwait(false);
            if (actual is null || actual.MicMute != muted)
            {
                throw new InvalidOperationException("053E microphone mute readback did not match.");
            }
        }
        catch
        {
            await TryRestoreMicMuteAsync(previous.MicMute).ConfigureAwait(false);
            throw;
        }
    }

    public async Task SetBrightnessAsync(
        byte brightness,
        byte profileId = 0,
        byte regionId = 0,
        CancellationToken cancellationToken = default)
    {
        var previous = await ReadBrightnessCoreAsync(profileId, regionId, cancellationToken).ConfigureAwait(false);
        if (previous == brightness)
        {
            return;
        }

        try
        {
            await SendBrightnessAsync(brightness, profileId, regionId, cancellationToken).ConfigureAwait(false);
            var actual = await ReadBrightnessAfterWriteAsync(profileId, regionId, cancellationToken).ConfigureAwait(false);
            if (actual is null || actual.Value != brightness)
            {
                throw new InvalidOperationException("053E brightness readback did not match.");
            }
        }
        catch
        {
            await TryRestoreBrightnessAsync(previous, profileId, regionId).ConfigureAwait(false);
            throw;
        }
    }

    private async Task<Version> ReadFirmwareCoreAsync(CancellationToken cancellationToken)
    {
        var response = await QueryFeatureAsync(
            RazerMixer053EProtocol.CreateGetFirmware(),
            response => RazerMixer053EProtocol.MatchesFirmwareResponse(response),
            cancellationToken).ConfigureAwait(false)
            ?? throw new IOException("053E firmware query returned no matching response.");
        return RazerMixer053EProtocol.ReadFirmware(response);
    }

    private async Task<string> ReadSerialNumberCoreAsync(CancellationToken cancellationToken)
    {
        var response = await QueryFeatureAsync(
            RazerMixer053EProtocol.CreateGetSerialNumber(),
            response => RazerMixer053EProtocol.MatchesSerialNumberResponse(response),
            cancellationToken).ConfigureAwait(false)
            ?? throw new IOException("053E serial query returned no matching response.");
        return RazerMixer053EProtocol.ReadSerialNumber(response);
    }

    private async Task<byte> ReadDeviceModeCoreAsync(CancellationToken cancellationToken)
    {
        var response = await QueryFeatureAsync(
            RazerMixer053EProtocol.CreateGetDeviceMode(),
            response => RazerMixer053EProtocol.MatchesDeviceModeResponse(response),
            cancellationToken).ConfigureAwait(false)
            ?? throw new IOException("053E device-mode query returned no matching response.");
        return RazerMixer053EProtocol.ReadDeviceMode(response);
    }

    private async Task<byte> ReadBrightnessCoreAsync(
        byte profileId,
        byte regionId,
        CancellationToken cancellationToken)
    {
        var response = await QueryFeatureAsync(
            CreateGetBrightnessRequest(profileId, regionId),
            response => RazerMixer053EProtocol.MatchesBrightnessResponse(response),
            cancellationToken).ConfigureAwait(false)
            ?? throw new IOException("053E brightness query returned no matching response.");
        return RazerMixer053EProtocol.ReadBrightness(response);
    }

    private async Task<RazerMixer053EHardwareStatus> ReadHardwareStatusCoreAsync(
        CancellationToken cancellationToken)
    {
        await EnsureConnectedAsync(cancellationToken).ConfigureAwait(false);
        var report = await _transport.ReadStatusAsync(cancellationToken).ConfigureAwait(false);
        if (report is null ||
            !RazerMixer053EProtocol.TryReadHardwareStatus(report, out var status) ||
            status is null)
        {
            throw new InvalidDataException("053E report-9 hardware status is unavailable.");
        }

        return status;
    }

    private async Task<byte[]?> QueryFeatureAsync(
        byte[] request,
        Func<byte[], bool> isMatch,
        CancellationToken cancellationToken)
    {
        await EnsureConnectedAsync(cancellationToken).ConfigureAwait(false);
        var response = await _transport.QueryFeatureAsync(request, isMatch, cancellationToken)
            .ConfigureAwait(false);
        return response is not null && isMatch(response) ? response : null;
    }

    private async Task SendFeatureAsync(byte[] report, CancellationToken cancellationToken)
    {
        await EnsureConnectedAsync(cancellationToken).ConfigureAwait(false);
        await _transport.SendFeatureAsync(report, cancellationToken).ConfigureAwait(false);
    }

    private async Task SendOutputAsync(byte[] report, CancellationToken cancellationToken)
    {
        await EnsureConnectedAsync(cancellationToken).ConfigureAwait(false);
        await _transport.SendOutputAsync(report, cancellationToken).ConfigureAwait(false);
    }

    private async Task EnsureConnectedAsync(CancellationToken cancellationToken)
    {
        if (Volatile.Read(ref _connected) != 0)
        {
            return;
        }

        await _connectGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (Volatile.Read(ref _connected) != 0)
            {
                return;
            }

            await _transport.SendOutputAsync(
                RazerMixer053EProtocol.CreateConnectHandshake(),
                cancellationToken).ConfigureAwait(false);
            Volatile.Write(ref _connected, 1);
        }
        finally
        {
            _connectGate.Release();
        }
    }

    private async Task<RazerMixer053EHardwareStatus?> TryReadHardwareStatusAsync(
        List<string> errors,
        CancellationToken cancellationToken)
    {
        try
        {
            return await ReadHardwareStatusCoreAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (IsExpected(exception))
        {
            errors.Add($"hardware status: {exception.Message}");
            return null;
        }
    }

    private async Task<(bool Ok, T Value)> TryReadAsync<T>(
        string label,
        byte[] request,
        Func<byte[], bool> isMatch,
        Func<byte[], T> parse,
        List<string> errors,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await QueryFeatureAsync(request, isMatch, cancellationToken)
                .ConfigureAwait(false)
                ?? throw new IOException($"053E {label} query returned no matching response.");
            return (true, parse(response));
        }
        catch (Exception exception) when (IsExpected(exception))
        {
            errors.Add($"{label}: {exception.Message}");
            return (false, default!);
        }
    }

    private async Task<RazerMixer053EHardwareStatus?> ReadHardwareStatusAfterWriteAsync(
        CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < ReadbackAttempts; attempt++)
        {
            try
            {
                return await ReadHardwareStatusCoreAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception) when (IsExpected(exception))
            {
                if (attempt + 1 == ReadbackAttempts)
                {
                    Diagnose("hardware status readback", exception);
                    return null;
                }
            }

            await Task.Delay(ReadbackDelay, cancellationToken).ConfigureAwait(false);
        }

        return null;
    }

    private async Task<byte?> ReadBrightnessAfterWriteAsync(
        byte profileId,
        byte regionId,
        CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < ReadbackAttempts; attempt++)
        {
            try
            {
                return await ReadBrightnessCoreAsync(profileId, regionId, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (Exception exception) when (IsExpected(exception))
            {
                if (attempt + 1 == ReadbackAttempts)
                {
                    Diagnose("brightness readback", exception);
                    return null;
                }
            }

            await Task.Delay(ReadbackDelay, cancellationToken).ConfigureAwait(false);
        }

        return null;
    }

    private async Task SendBrightnessAsync(
        byte brightness,
        byte profileId,
        byte regionId,
        CancellationToken cancellationToken)
    {
        var frames = RazerMixer053EProtocol.CreateSetBrightnessFrames(profileId, regionId, brightness);
        for (var index = 0; index < frames.Count; index++)
        {
            await SendFeatureAsync(frames[index], cancellationToken).ConfigureAwait(false);
            if (frames.Count > 1 && index + 1 < frames.Count)
            {
                await Task.Delay(BrightnessFrameDelay, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private async Task TryRestoreChannelMuteAsync(int channel, bool muted)
    {
        try
        {
            await SendFeatureAsync(
                RazerMixer053EProtocol.CreateSetChannelMute(channel, muted),
                CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception exception) when (IsExpected(exception))
        {
            Diagnose($"restore channel {channel} mute", exception);
        }
    }

    private async Task TryRestoreMicMuteAsync(bool muted)
    {
        try
        {
            await SendFeatureAsync(
                RazerMixer053EProtocol.CreateSetChannelMute(5, muted),
                CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception exception) when (IsExpected(exception))
        {
            Diagnose("restore microphone mute channel", exception);
        }

        try
        {
            await SendOutputAsync(
                RazerMixer053EProtocol.CreateMicMuteCompanion(muted),
                CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception exception) when (IsExpected(exception))
        {
            Diagnose("restore microphone mute companion", exception);
        }
    }

    private async Task TryRestoreBrightnessAsync(
        byte brightness,
        byte profileId,
        byte regionId)
    {
        try
        {
            await SendBrightnessAsync(
                brightness,
                profileId,
                regionId,
                CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception exception) when (IsExpected(exception))
        {
            Diagnose("restore brightness", exception);
        }
    }

    private static byte[] CreateGetBrightnessRequest(byte profileId, byte regionId)
    {
        var request = RazerMixer053EProtocol.CreateGetBrightness();
        request[9] = profileId;
        request[10] = regionId;
        return request;
    }

    private void Diagnose(string label, Exception exception) =>
        _diagnostic?.Invoke($"053E {label} failed: {exception.Message}");

    private static bool IsExpected(Exception exception) =>
        exception is IOException or InvalidDataException or InvalidOperationException or
            NotSupportedException or Win32Exception or UnauthorizedAccessException or TimeoutException;

    public async ValueTask DisposeAsync()
    {
        _connectGate.Dispose();
        await _transport.DisposeAsync().ConfigureAwait(false);
    }
}
