using System.ComponentModel;
using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.Windows.Devices;

/// <summary>
/// Audio controls for the AudioValkyrie / AudioBillie 64-byte report-id-2 channel.
/// The bundle profiles share the fixed 0x60 transaction. The wired Billie/Valkyrie
/// profiles leave the direction byte zero; the Odessa T2 dongle sets 0x80.
/// </summary>
public sealed partial class RazerValkyrieHeadsetService : IAsyncDisposable
{
    internal enum HeadsetProfile
    {
        Valkyrie,
        BillieT1Wired,
        BillieT2,
        OdessaT2Wireless,
    }

    private readonly IRazerHeadsetHidTransport _transport;
    private readonly Action<string>? _diagnostic;
    private readonly HeadsetProfile _profile;
    private readonly ushort _productId;
    private readonly byte _direction;
    private readonly bool _eqBandsWithProfile;
    private readonly bool _usesRollingTransaction;
    private int _transactionCounter;

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
        _productId = productId;
        // Dongle-forwarded commands set byte 9; wired cables leave it zero.
        _direction = productId is
            BlackSharkV3ProProtocol.HyperSpeedWirelessProductId or
            0x0568 or 0x056C or 0x0588 or 0x057A or 0x0A55 or 0x0A58
            ? (byte)0x80
            : (byte)0x00;
        _eqBandsWithProfile = UsesProfilePrefixedEqBands(productId);
        _usesRollingTransaction = _profile == HeadsetProfile.OdessaT2Wireless;
        _diagnostic = diagnostic;
    }

    public static bool IsSupported(ushort productId) => productId is
        0x0579 or 0x057A or 0x0A4E or 0x0A51 or 0x0A55 or 0x0A58 or 0x101E or 0x101F or
        0x0567 or 0x0568 or 0x056B or 0x056C or 0x0574 or 0x0587 or 0x0588 or
        BlackSharkV3ProProtocol.HyperSpeedWirelessProductId or
        BlackSharkV3ProProtocol.HyperSpeedUsbProductId;

    // Only a dongle answers the radio-link query; hiding a wired headset because it
    // ignored an unsupported command would remove a working device.
    public static bool SupportsWirelessConnectionStatus(ushort productId) => productId is
        BlackSharkV3ProProtocol.HyperSpeedWirelessProductId or
        0x0568 or 0x056C or 0x0588 or 0x057A or 0x0A55 or 0x0A58;

    private static HeadsetProfile GetProfile(ushort productId) => productId switch
    {
        0x0567 => HeadsetProfile.BillieT1Wired,
        0x0568 => HeadsetProfile.BillieT1Wired,
        0x056B => HeadsetProfile.BillieT2,
        0x056C => HeadsetProfile.BillieT2,
        0x0587 => HeadsetProfile.BillieT2,
        0x0588 => HeadsetProfile.BillieT2,
        // AudioMabelT3C (Barracuda X Chroma) extends AudioBillieT2 without
        // overriding an audio command: same report id 2, 64-byte frame,
        // transaction 0x60 and zero direction byte.
        0x0574 => HeadsetProfile.BillieT2,
        BlackSharkV3ProProtocol.HyperSpeedWirelessProductId or
            BlackSharkV3ProProtocol.HyperSpeedUsbProductId => HeadsetProfile.OdessaT2Wireless,
        0x0579 or 0x057A or 0x0A4E or 0x0A51 or 0x0A55 or 0x0A58 or 0x101E or 0x101F =>
            HeadsetProfile.Valkyrie,
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

    public async Task<bool?> TryReadWirelessConnectionAsync(
        CancellationToken cancellationToken = default)
    {
        if (!SupportsWirelessConnectionStatus(_productId))
        {
            return null;
        }

        try
        {
            var value = await QueryAsync(
                RazerValkyrieHeadsetProtocol.CreateGetWirelessConnectionStatus(),
                RazerValkyrieHeadsetProtocol.WirelessConnectionStatusHeader,
                "read wireless connection",
                cancellationToken).ConfigureAwait(false);
            return RazerValkyrieHeadsetProtocol.ReadWirelessConnectionStatus(value);
        }
        catch (Exception exception) when (IsExpected(exception))
        {
            _diagnostic?.Invoke($"{_profile} wireless status failed: {exception.Message}");
            return null;
        }
    }

    private Task SetEqPresetCoreAsync(byte preset, CancellationToken cancellationToken = default) =>
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

    private async Task SetEqBandsCoreAsync(
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
        // AudioOdessaT2Wireless advances 1..31 on top of 0x60; every other profile
        // keeps the bundle's fixed 0x60 transaction.
        var transactionId = _usesRollingTransaction
            ? (byte)(0x61 + _transactionCounter)
            : RazerValkyrieHeadsetProtocol.TransactionId;
        if (_usesRollingTransaction)
        {
            _transactionCounter = (_transactionCounter + 1) % 31;
            report[2] = transactionId;
        }
        if (_direction != 0)
        {
            report[9] = _direction;
        }
        if (_usesRollingTransaction || _direction != 0)
        {
            report[62] = RazerValkyrieHeadsetProtocol.Checksum(report);
        }

        _diagnostic?.Invoke(
            $"{_profile} {label}: command=0x{report[10]:X2} 0x{report[11]:X2} 0x{report[12]:X2}");
        for (var attempt = 0; attempt < 3; attempt++)
        {
            var response = await _transport.QueryAsync(
                report,
                candidate => RazerValkyrieHeadsetProtocol.TryReadResponse(
                    candidate, expectedHeader, transactionId, out _, out _),
                cancellationToken).ConfigureAwait(false);
            if (response is null)
            {
                throw new IOException($"AudioValkyrie {label} returned no matching response.");
            }

            if (!RazerValkyrieHeadsetProtocol.TryReadResponse(
                response, expectedHeader, transactionId, out var status, out var data))
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
