using System.ComponentModel;
using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.Windows.Devices;

/// <summary>
/// Controls for the AudioAvnera 65-byte family (0528, Nari). The command set follows
/// the bundle's AudioAvneraDevice class: identity, link, battery, sidetone, silence
/// detection, haptics and the single-zone Chroma commands.
/// </summary>
public sealed class RazerAvneraHeadsetService : IAsyncDisposable
{
    private readonly IRazerAvnera65Transport _transport;
    private readonly Action<string>? _diagnostic;

    public RazerAvneraHeadsetService(string devicePath, Action<string>? diagnostic = null)
        : this(new RazerAvnera65Transport(devicePath), diagnostic)
    {
    }

    internal RazerAvneraHeadsetService(
        IRazerAvnera65Transport transport,
        Action<string>? diagnostic = null)
    {
        _transport = transport ?? throw new ArgumentNullException(nameof(transport));
        _diagnostic = diagnostic;
    }

    public async Task<RazerHeadsetAudioState> ReadStateAsync(
        CancellationToken cancellationToken = default)
    {
        var errors = new List<string>();
        var firmware = await ProbeAsync(
            "firmware",
            RazerAvnera65Protocol.CreateFirmwareRequest(),
            RazerAvnera65Protocol.FirmwareHeader,
            RazerAvnera65Protocol.ReadFirmware,
            errors,
            cancellationToken).ConfigureAwait(false);
        var serial = await ProbeManyAsync(
            "serial number",
            RazerAvnera65Protocol.CreateSerialRequests(),
            RazerAvnera65Protocol.SerialHeader,
            RazerAvnera65Protocol.ReadSerialNumber,
            errors,
            cancellationToken).ConfigureAwait(false);
        var edition = await ProbeManyAsync(
            "edition",
            RazerAvnera65Protocol.CreateEditionRequests(),
            RazerAvnera65Protocol.EditionHeader,
            RazerAvnera65Protocol.ReadEdition,
            errors,
            cancellationToken).ConfigureAwait(false);
        var link = await ProbeAsync(
            "link status",
            RazerAvnera65Protocol.CreateLinkStatusRequest(),
            RazerAvnera65Protocol.LinkStatusHeader,
            RazerAvnera65Protocol.ReadLinkStatus,
            errors,
            cancellationToken).ConfigureAwait(false);
        var battery = await ProbeAsync(
            "battery",
            RazerAvnera65Protocol.CreateBatteryRequest(),
            RazerAvnera65Protocol.BatteryHeader,
            RazerAvnera65Protocol.ReadBattery,
            errors,
            cancellationToken).ConfigureAwait(false);
        var sidetone = await ProbeAsync(
            "sidetone",
            RazerAvnera65Protocol.CreateSidetoneRequest(),
            RazerAvnera65Protocol.SidetoneHeader,
            RazerAvnera65Protocol.ReadSidetone,
            errors,
            cancellationToken).ConfigureAwait(false);
        var silence = await ProbeAsync(
            "silence detection",
            RazerAvnera65Protocol.CreateSilenceDetectionRequest(),
            RazerAvnera65Protocol.SilenceDetectionHeader,
            RazerAvnera65Protocol.ReadSilenceDetection,
            errors,
            cancellationToken).ConfigureAwait(false);
        var haptics = await ProbeAsync(
            "haptics",
            RazerAvnera65Protocol.CreateHapticsRequest(),
            RazerAvnera65Protocol.HapticsHeader,
            RazerAvnera65Protocol.ReadHaptics,
            errors,
            cancellationToken).ConfigureAwait(false);
        var chroma = await ProbeAsync(
            "chroma effect",
            RazerAvnera65Protocol.CreateChromaEffectRequest(),
            RazerAvnera65Protocol.ChromaEffectHeader,
            RazerAvnera65Protocol.ReadChromaEffect,
            errors,
            cancellationToken).ConfigureAwait(false);
        var chromaBrightness = await ProbeAsync(
            "chroma brightness",
            RazerAvnera65Protocol.CreateChromaBrightnessRequest(),
            RazerAvnera65Protocol.ChromaBrightnessHeader,
            RazerAvnera65Protocol.ReadChromaBrightness,
            errors,
            cancellationToken).ConfigureAwait(false);
        var dockFirmware = await ProbeManyAsync(
            "dock firmware",
            [RazerAvnera65Protocol.CreateDockFirmwareRequest()],
            RazerAvnera65Protocol.DockFirmwareHeader,
            RazerAvnera65Protocol.ReadDockFirmware,
            errors,
            cancellationToken,
            optional: true).ConfigureAwait(false);
        var dockSerial = await ProbeManyAsync(
            "dock serial number",
            RazerAvnera65Protocol.CreateDockSerialRequests(),
            RazerAvnera65Protocol.DockSerialHeader,
            RazerAvnera65Protocol.ReadDockSerialNumber,
            errors,
            cancellationToken,
            optional: true).ConfigureAwait(false);
        var dockEdition = await ProbeManyAsync(
            "dock edition",
            RazerAvnera65Protocol.CreateDockEditionRequests(),
            RazerAvnera65Protocol.DockEditionHeader,
            RazerAvnera65Protocol.ReadDockEdition,
            errors,
            cancellationToken,
            optional: true).ConfigureAwait(false);
        return new RazerHeadsetAudioState(
            Firmware: firmware.Ok ? firmware.Value : null,
            BatteryPercent: battery.Ok ? battery.Value.Level : null,
            Charging: battery.Ok ? battery.Value.Charging : null,
            EqPreset: null,
            EqBands: null,
            Bass: null,
            MicMuted: null,
            MicVolume: null,
            AudioSource: null,
            NoiseSuppression: null,
            AncState: null,
            AncLevel: null,
            Errors: errors)
        {
            SerialNumber = serial.Ok ? serial.Value : null,
            Edition = edition.Ok ? edition.Value : null,
            LinkStatus = link.Ok ? link.Value : null,
            SidetonePercent = sidetone.Ok ? sidetone.Value : null,
            MicReturnSwitch = sidetone.Ok ? sidetone.Value > 0 : null,
            AutoPause = silence.Ok ? silence.Value.Enabled : null,
            SilenceDetectionPeriod = silence.Ok ? silence.Value.Period : null,
            Haptics = haptics.Ok ? haptics.Value : null,
            DockFirmware = dockFirmware.Ok ? dockFirmware.Value : null,
            DockSerialNumber = dockSerial.Ok ? dockSerial.Value : null,
            DockEdition = dockEdition.Ok ? dockEdition.Value : null,
            ChromaEffect = chroma.Ok ? chroma.Value.Effect : null,
            ChromaBrightness = chromaBrightness.Ok ? chromaBrightness.Value : null,
            ChromaColors = chroma.Ok ? [chroma.Value.Red, chroma.Value.Green, chroma.Value.Blue] : null,
        };
    }

    public Task SetSidetoneAsync(byte percent, CancellationToken cancellationToken = default) =>
        _transport.SendAsync(
            RazerAvnera65Protocol.CreateSetSidetoneRequest(percent), cancellationToken);

    public Task SetAutoPauseAsync(
        bool enabled,
        ushort period,
        CancellationToken cancellationToken = default) =>
        _transport.SendAsync(
            RazerAvnera65Protocol.CreateSetSilenceDetectionRequest(enabled, period), cancellationToken);

    public Task SetHapticsAsync(
        bool enabled,
        ushort strength,
        CancellationToken cancellationToken = default) =>
        _transport.SendAsync(
            RazerAvnera65Protocol.CreateSetHapticsRequest(enabled, strength), cancellationToken);

    public Task SetChromaEffectAsync(
        RazerChromaEffect effect,
        byte red,
        byte green,
        byte blue,
        CancellationToken cancellationToken = default) =>
        _transport.SendAsync(
            RazerAvnera65Protocol.CreateSetChromaEffectRequest(effect, red, green, blue),
            cancellationToken);

    public Task SetChromaBrightnessAsync(
        byte percent,
        CancellationToken cancellationToken = default) =>
        _transport.SendAsync(
            RazerAvnera65Protocol.CreateSetChromaBrightnessRequest(percent),
            cancellationToken);

    public Task SetChromaFrameAsync(
        byte red,
        byte green,
        byte blue,
        CancellationToken cancellationToken = default) =>
        _transport.SendAsync(
            RazerAvnera65Protocol.CreateSetChromaFrameRequest(red, green, blue),
            cancellationToken);

    private async Task<(bool Ok, T Value)> ProbeAsync<T>(
        string label,
        byte[] request,
        byte[] expectedHeader,
        Func<ReadOnlySpan<byte>, T> parse,
        List<string> errors,
        CancellationToken cancellationToken,
        bool optional = false)
        => await ProbeManyAsync(
            label,
            [request],
            expectedHeader,
            parse,
            errors,
            cancellationToken,
            optional).ConfigureAwait(false);

    private async Task<(bool Ok, T Value)> ProbeManyAsync<T>(
        string label,
        IReadOnlyList<byte[]> requests,
        byte[] expectedHeader,
        Func<ReadOnlySpan<byte>, T> parse,
        List<string> errors,
        CancellationToken cancellationToken,
        bool optional = false)
    {
        try
        {
            var response = await QueryManyAsync(requests, expectedHeader, label, cancellationToken)
                .ConfigureAwait(false);
            return (true, parse(response));
        }
        catch (Exception exception) when (IsExpected(exception))
        {
            if (!optional)
            {
                errors.Add($"{label}: {exception.Message}");
            }
            return (false, default!);
        }
    }

    private async Task<byte[]> QueryManyAsync(
        IReadOnlyList<byte[]> requests,
        byte[] expectedHeader,
        string label,
        CancellationToken cancellationToken)
    {
        if (requests.Count == 0)
        {
            throw new ArgumentException("An AudioAvnera query needs at least one request.", nameof(requests));
        }

        _diagnostic?.Invoke(
            $"avnera {label}: request=0x{Convert.ToHexString(requests[^1].AsSpan(3, 5))}");
        for (var index = 0; index < requests.Count - 1; index++)
        {
            await _transport.SendAsync(requests[index], cancellationToken).ConfigureAwait(false);
        }

        return await _transport.QueryAsync(requests[^1], expectedHeader, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException($"AudioAvnera {label} returned no matching response.");
    }

    private static bool IsExpected(Exception exception) =>
        exception is IOException or InvalidDataException or InvalidOperationException or
            NotSupportedException or Win32Exception or UnauthorizedAccessException;

    public ValueTask DisposeAsync() => _transport.DisposeAsync();
}
