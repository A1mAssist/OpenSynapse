using System.ComponentModel;
using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.Windows.Devices;

/// <summary>
/// Audio controls for the AudioValkyrie / AudioBillie 64-byte report-id-2 channel.
/// The three bundle profiles share the fixed 0x60 transaction and zero direction byte;
/// they differ in which EQ-band tuple their chunk selects.
/// </summary>
public sealed class RazerValkyrieHeadsetService : IAsyncDisposable
{
    internal enum HeadsetProfile
    {
        Valkyrie,
        BillieT1Wired,
        BillieT2,
    }

    private readonly IRazerHeadsetHidTransport _transport;
    private readonly Action<string>? _diagnostic;
    private readonly HeadsetProfile _profile;
    private readonly bool _eqBandsWithProfile;

    public RazerValkyrieHeadsetService(
        ushort productId,
        string devicePath,
        Action<string>? diagnostic = null)
        : this(new RazerHeadsetHidTransport(devicePath), productId, diagnostic)
    {
    }

    internal RazerValkyrieHeadsetService(
        IRazerHeadsetHidTransport transport,
        ushort productId,
        Action<string>? diagnostic = null)
    {
        _transport = transport ?? throw new ArgumentNullException(nameof(transport));
        _profile = GetProfile(productId);
        _eqBandsWithProfile = UsesProfilePrefixedEqBands(productId);
        _diagnostic = diagnostic;
    }

    public static bool IsSupported(ushort productId) => productId is
        0x0579 or 0x0A4E or 0x0A51 or 0x101E or 0x101F or
        0x0567 or 0x056B or 0x0587;

    private static HeadsetProfile GetProfile(ushort productId) => productId switch
    {
        0x0567 => HeadsetProfile.BillieT1Wired,
        0x056B => HeadsetProfile.BillieT2,
        0x0587 => HeadsetProfile.BillieT2,
        0x0579 or 0x0A4E or 0x0A51 or 0x101E or 0x101F => HeadsetProfile.Valkyrie,
        _ => throw new NotSupportedException(
            $"AudioValkyrie is not supported on product 0x{productId:X4}."),
    };

    // The shared `bo`/`fU` helpers send Ph7 [15,00,01] with a one-byte selector and
    // jX9 [95,00,0B] with the selector before the ten bands, so every current
    // AudioValkyrie/Billie bundle uses the profile-prefixed shape.
    private static bool UsesProfilePrefixedEqBands(ushort productId) =>
        IsSupported(productId);

    public async Task<RazerHeadsetAudioState> ReadStateAsync(
        CancellationToken cancellationToken = default)
    {
        var errors = new List<string>();
        var battery = await ProbeAsync(
            "battery",
            RazerValkyrieHeadsetProtocol.CreateGetBattery(),
            RazerValkyrieHeadsetProtocol.BatteryHeader,
            RazerValkyrieHeadsetProtocol.ReadBatteryPercent,
            errors,
            cancellationToken).ConfigureAwait(false);
        var charging = await ProbeAsync(
            "charging",
            RazerValkyrieHeadsetProtocol.CreateGetCharging(),
            RazerValkyrieHeadsetProtocol.ChargingHeader,
            RazerValkyrieHeadsetProtocol.ReadChargingStatus,
            errors,
            cancellationToken).ConfigureAwait(false);
        var preset = await ProbeAsync(
            "EQ preset",
            RazerValkyrieHeadsetProtocol.CreateGetEqPreset(),
            RazerValkyrieHeadsetProtocol.EqPresetHeader,
            RazerValkyrieHeadsetProtocol.ReadEqPreset,
            errors,
            cancellationToken).ConfigureAwait(false);
        var bands = await ProbeAsync(
            "EQ bands",
            RazerValkyrieHeadsetProtocol.CreateGetEqBands(_eqBandsWithProfile),
            _eqBandsWithProfile
                ? RazerValkyrieHeadsetProtocol.EqBandsWithProfileHeader
                : RazerValkyrieHeadsetProtocol.EqBandsBareHeader,
            value => RazerValkyrieHeadsetProtocol.ReadEqBands(value, _eqBandsWithProfile),
            errors,
            cancellationToken).ConfigureAwait(false);
        var micBands = await ProbeAsync(
            "microphone EQ bands",
            RazerValkyrieHeadsetProtocol.CreateGetMicBands(),
            RazerValkyrieHeadsetProtocol.MicBandsHeader,
            RazerValkyrieHeadsetProtocol.ReadMicBands,
            errors,
            cancellationToken).ConfigureAwait(false);
        var micMute = await ProbeAsync(
            "microphone mute",
            RazerValkyrieHeadsetProtocol.CreateGetMicMute(),
            RazerValkyrieHeadsetProtocol.MicMuteHeader,
            RazerValkyrieHeadsetProtocol.ReadMicMute,
            errors,
            cancellationToken).ConfigureAwait(false);

        return new RazerHeadsetAudioState(
            Firmware: null,
            BatteryPercent: battery.Ok ? battery.Value : null,
            Charging: charging.Ok ? charging.Value : null,
            EqPreset: preset.Ok ? preset.Value : null,
            EqBands: bands.Ok ? bands.Value : null,
            Bass: null,
            MicMuted: micMute.Ok ? micMute.Value : null,
            MicVolume: null,
            AudioSource: null,
            NoiseSuppression: null,
            AncState: null,
            AncLevel: null,
            Errors: errors) { MicBands = micBands.Ok ? micBands.Value : null };
    }

    public Task SetEqPresetAsync(byte preset, CancellationToken cancellationToken = default) =>
        WriteAsync(
            RazerValkyrieHeadsetProtocol.CreateGetEqPreset(),
            RazerValkyrieHeadsetProtocol.EqPresetHeader,
            RazerValkyrieHeadsetProtocol.ReadEqPreset,
            preset,
            RazerValkyrieHeadsetProtocol.CreateSetEqPreset,
            RazerValkyrieHeadsetProtocol.SetEqPresetHeader,
            value => value == preset,
            "EQ preset",
            cancellationToken);

    public Task SetMicMuteAsync(bool muted, CancellationToken cancellationToken = default) =>
        WriteAsync(
            RazerValkyrieHeadsetProtocol.CreateGetMicMute(),
            RazerValkyrieHeadsetProtocol.MicMuteHeader,
            RazerValkyrieHeadsetProtocol.ReadMicMute,
            muted,
            RazerValkyrieHeadsetProtocol.CreateSetMicMute,
            RazerValkyrieHeadsetProtocol.SetMicMuteHeader,
            value => value == muted,
            "microphone mute",
            cancellationToken);

    public async Task SetEqBandsAsync(
        IReadOnlyList<byte> bands,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(bands);
        if (bands.Count != RazerValkyrieHeadsetProtocol.TenBandCount)
        {
            throw new ArgumentException("Equalizer data must contain 10 bands.", nameof(bands));
        }

        var requested = bands.ToArray();
        await WriteAsync(
            RazerValkyrieHeadsetProtocol.CreateGetEqBands(_eqBandsWithProfile),
            _eqBandsWithProfile
                ? RazerValkyrieHeadsetProtocol.EqBandsWithProfileHeader
                : RazerValkyrieHeadsetProtocol.EqBandsBareHeader,
            value => RazerValkyrieHeadsetProtocol.ReadEqBands(value, _eqBandsWithProfile),
            requested,
            value => RazerValkyrieHeadsetProtocol.CreateSetEqBands(value, _eqBandsWithProfile),
            RazerValkyrieHeadsetProtocol.SetEqBandsHeader,
            actual => actual.SequenceEqual(requested),
            "EQ bands",
            cancellationToken).ConfigureAwait(false);
    }

    public async Task SetMicBandsAsync(
        IReadOnlyList<byte> bands,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(bands);
        if (bands.Count != RazerValkyrieHeadsetProtocol.TenBandCount)
        {
            throw new ArgumentException("Microphone equalizer data must contain 10 bands.", nameof(bands));
        }

        var requested = bands.ToArray();
        await WriteAsync(
            RazerValkyrieHeadsetProtocol.CreateGetMicBands(),
            RazerValkyrieHeadsetProtocol.MicBandsHeader,
            RazerValkyrieHeadsetProtocol.ReadMicBands,
            requested,
            bands => RazerValkyrieHeadsetProtocol.CreateSetMicBands(bands),
            RazerValkyrieHeadsetProtocol.SetMicBandsHeader,
            actual => actual.SequenceEqual(requested),
            "microphone EQ bands",
            cancellationToken).ConfigureAwait(false);
    }

    private async Task WriteAsync<T>(
        byte[] readRequest,
        byte[] readHeader,
        Func<ReadOnlySpan<byte>, T> parse,
        T value,
        Func<T, byte[]> writeRequest,
        byte[] writeHeader,
        Func<T, bool> matches,
        string label,
        CancellationToken cancellationToken)
    {
        var previous = parse(await QueryAsync(
            readRequest, readHeader, $"read {label}", cancellationToken).ConfigureAwait(false));
        if (matches(previous))
        {
            return;
        }

        try
        {
            await QueryAsync(
                writeRequest(value), writeHeader, $"set {label}", cancellationToken).ConfigureAwait(false);
            var actual = parse(await QueryAsync(
                readRequest, readHeader, $"read back {label}", cancellationToken).ConfigureAwait(false));
            if (!matches(actual))
            {
                throw new InvalidOperationException(
                    $"{label} readback did not match the requested value.");
            }
        }
        catch
        {
            await TryRestoreAsync(writeRequest(previous), writeHeader, $"restore {label}")
                .ConfigureAwait(false);
            throw;
        }
    }

    private async Task<(bool Ok, T Value)> ProbeAsync<T>(
        string label,
        byte[] request,
        byte[] expectedHeader,
        Func<ReadOnlySpan<byte>, T> parse,
        List<string> errors,
        CancellationToken cancellationToken)
    {
        try
        {
            return (true, parse(await QueryAsync(
                request, expectedHeader, $"read {label}", cancellationToken).ConfigureAwait(false)));
        }
        catch (Exception exception) when (IsExpected(exception))
        {
            errors.Add($"{label}: {exception.Message}");
            return (false, default!);
        }
    }

    private async Task TryRestoreAsync(byte[] request, byte[] expectedHeader, string label)
    {
        try
        {
            await QueryAsync(request, expectedHeader, label, CancellationToken.None)
                .ConfigureAwait(false);
        }
        catch (Exception exception) when (IsExpected(exception))
        {
            _diagnostic?.Invoke($"{_profile} {label} failed: {exception.Message}");
        }
    }

    private async Task<byte[]> QueryAsync(
        byte[] report,
        byte[] expectedHeader,
        string label,
        CancellationToken cancellationToken)
    {
        _diagnostic?.Invoke(
            $"{_profile} {label}: command=0x{report[10]:X2} 0x{report[11]:X2} 0x{report[12]:X2}");
        for (var attempt = 0; attempt < 3; attempt++)
        {
            var response = await _transport.QueryAsync(
                report,
                candidate => RazerValkyrieHeadsetProtocol.TryReadResponse(
                    candidate, expectedHeader, out _, out _),
                cancellationToken).ConfigureAwait(false);
            if (response is null)
            {
                throw new IOException($"AudioValkyrie {label} returned no matching response.");
            }

            if (!RazerValkyrieHeadsetProtocol.TryReadResponse(
                response, expectedHeader, out var status, out var data))
            {
                throw new InvalidDataException(
                    $"AudioValkyrie {label} response did not match the request.");
            }

            if (RazerValkyrieHeadsetProtocol.IsSuccess(status))
            {
                return data;
            }

            if (RazerValkyrieHeadsetProtocol.IsResend(status))
            {
                continue;
            }

            throw new InvalidDataException(
                $"AudioValkyrie {label} failed with status {status}.");
        }

        throw new InvalidOperationException(
            $"AudioValkyrie {label} requested a resend three times.");
    }

    private static bool IsExpected(Exception exception) =>
        exception is IOException or InvalidDataException or InvalidOperationException or
            NotSupportedException or Win32Exception or UnauthorizedAccessException;

    public async ValueTask DisposeAsync()
    {
        await _transport.DisposeAsync().ConfigureAwait(false);
    }
}
