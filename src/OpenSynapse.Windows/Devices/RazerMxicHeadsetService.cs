using System.ComponentModel;
using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.Windows.Devices;

/// <summary>
/// Audio controls for the 64-byte MXIC / class-0x18 channels and the protocol40
/// headset channel (1532:0593). These models use report id 2 instead of the
/// 91-byte protocol 25 feature reports; protocol40 only exposes battery and
/// charging in its selected device class.
/// </summary>
public sealed partial class RazerMxicHeadsetService : IAsyncDisposable
{
    private enum MxicDialect
    {
        Legacy,
        MabelT1,
        Class18,
        ValkyrieT3,
        Joliet,
        Protocol40,
    }

    private readonly IRazerMxicHeadsetTransport _transport;
    private readonly Action<string>? _diagnostic;
    private readonly MxicDialect _dialect;
    private bool _remoteMode;

    public RazerMxicHeadsetService(ushort productId, string devicePath, Action<string>? diagnostic = null)
        : this(
            RazerMxicHeadsetProtocol.UsesValkyrieT3Commands(productId) ||
            RazerMxicHeadsetProtocol.UsesJolietCommands(productId)
                ? new RazerValkyrieT3Transport(devicePath)
                : new RazerMxicHeadsetTransport(
                    devicePath,
                    RazerMxicHeadsetProtocol.ReportLength,
                    useControlStatusReport:
                        !RazerProtocol40HeadsetProtocol.IsSupported(productId) &&
                        !RazerMxicHeadsetProtocol.UsesMabelT1(productId)),
            productId,
            diagnostic)
    {
    }

    internal RazerMxicHeadsetService(
        IRazerMxicHeadsetTransport transport,
        ushort productId,
        Action<string>? diagnostic = null)
    {
        _transport = transport ?? throw new ArgumentNullException(nameof(transport));
        _dialect = RazerProtocol40HeadsetProtocol.IsSupported(productId) ? MxicDialect.Protocol40
            : RazerMxicHeadsetProtocol.UsesMabelT1(productId) ? MxicDialect.MabelT1
            : RazerMxicHeadsetProtocol.UsesValkyrieT3Commands(productId) ? MxicDialect.ValkyrieT3
            : RazerMxicHeadsetProtocol.UsesJolietCommands(productId) ? MxicDialect.Joliet
            : RazerMxicHeadsetProtocol.UsesClass18Commands(productId) ? MxicDialect.Class18
            : MxicDialect.Legacy;
        _diagnostic = diagnostic;
    }

    public static bool IsSupported(ushort productId) =>
        RazerMxicHeadsetProtocol.IsSupported(productId) ||
        RazerProtocol40HeadsetProtocol.IsSupported(productId);

    private byte ReportId => _dialect switch
    {
        MxicDialect.ValkyrieT3 => RazerMxicHeadsetProtocol.ValkyrieReportId,
        MxicDialect.Joliet => RazerMxicHeadsetProtocol.ValkyrieReportId,
        MxicDialect.MabelT1 => RazerMxicHeadsetProtocol.MabelT1ReportId,
        _ => RazerMxicHeadsetProtocol.ReportId,
    };

    private byte[] CreateEqPresetRead() => _dialect switch
    {
        MxicDialect.ValkyrieT3 => RazerMxicHeadsetProtocol.CreateGetEqPresetValkyrie(),
        MxicDialect.Class18 => RazerMxicHeadsetProtocol.CreateGetEqPresetClass18(),
        MxicDialect.Joliet => RazerMxicHeadsetProtocol.CreateGetEqPresetJoliet(),
        _ => RazerMxicHeadsetProtocol.CreateRequest(RazerMxicHeadsetProtocol.GetEqPreset, reportId: ReportId),
    };

    private byte[] EqPresetHeader => _dialect switch
    {
        MxicDialect.ValkyrieT3 => RazerMxicHeadsetProtocol.EqHeaderValkyrie,
        MxicDialect.Class18 => RazerMxicHeadsetProtocol.EqPresetHeaderClass18,
        MxicDialect.Joliet => RazerMxicHeadsetProtocol.EqPresetHeaderClass18,
        _ => RazerMxicHeadsetProtocol.EqPresetHeader,
    };

    private byte ReadEqPreset(ReadOnlySpan<byte> value) => _dialect is MxicDialect.Class18 or MxicDialect.ValkyrieT3 or MxicDialect.Joliet
        ? RazerMxicHeadsetProtocol.ReadEqPresetClass18(value)
        : RazerMxicHeadsetProtocol.ReadEqPreset(value);

    private byte[] CreateEqBandsRead() => _dialect switch
    {
        MxicDialect.ValkyrieT3 => RazerMxicHeadsetProtocol.CreateGetEqBandsValkyrie(),
        MxicDialect.Class18 => RazerMxicHeadsetProtocol.CreateGetEqBandsClass18(),
        MxicDialect.Joliet => RazerMxicHeadsetProtocol.CreateGetEqBandsJoliet(),
        _ => RazerMxicHeadsetProtocol.CreateRequest(RazerMxicHeadsetProtocol.GetEqBands, reportId: ReportId),
    };

    private byte[] EqBandsHeader => _dialect switch
    {
        MxicDialect.ValkyrieT3 => RazerMxicHeadsetProtocol.EqHeaderValkyrie,
        MxicDialect.Class18 => RazerMxicHeadsetProtocol.EqBandsHeaderClass18,
        MxicDialect.Joliet => RazerMxicHeadsetProtocol.EqBandsHeaderClass18,
        _ => RazerMxicHeadsetProtocol.EqBandsHeader,
    };

    private byte[] CreateSetEqBands(byte[] bands) => _dialect switch
    {
        MxicDialect.ValkyrieT3 => RazerMxicHeadsetProtocol.CreateSetEqBandsValkyrie(bands),
        MxicDialect.Class18 => RazerMxicHeadsetProtocol.CreateSetEqBandsClass18(bands),
        MxicDialect.Joliet => throw new NotSupportedException("Joliet bands need the selected preset."),
        _ => RazerMxicHeadsetProtocol.CreateSetEqBands(bands, ReportId),
    };

    // Each dialect requires its own command acknowledgement before readback.
    private byte[] SetEqBandsHeader => _dialect switch
    {
        MxicDialect.Class18 => RazerMxicHeadsetProtocol.EqBandsHeaderClass18,
        MxicDialect.Joliet => RazerMxicHeadsetProtocol.EqBandsHeaderClass18,
        _ => RazerMxicHeadsetProtocol.SetEqBandsHeader,
    };

    private byte[] CreateMicMuteRead() => _dialect == MxicDialect.Class18
        ? RazerMxicHeadsetProtocol.CreateGetMicMuteClass18(ReportId)
        : RazerMxicHeadsetProtocol.CreateRequest(RazerMxicHeadsetProtocol.GetMicMute, reportId: ReportId);

    private byte[] MicMuteHeader => _dialect == MxicDialect.Class18
        ? RazerMxicHeadsetProtocol.MicMuteHeaderClass18
        : RazerMxicHeadsetProtocol.MicMuteHeader;

    private byte[] CreateSetMicMute(bool muted) => _dialect == MxicDialect.Class18
        ? RazerMxicHeadsetProtocol.CreateSetMicMuteClass18(muted, ReportId)
        : RazerMxicHeadsetProtocol.CreateSetMicMute(muted, ReportId);

    private byte[] SetMicMuteHeader => _dialect == MxicDialect.Class18
        ? RazerMxicHeadsetProtocol.MicMuteHeaderClass18
        : RazerMxicHeadsetProtocol.SetMicMuteHeader;

    private byte[] CreateSetEqPreset(byte preset) => _dialect switch
    {
        MxicDialect.ValkyrieT3 => RazerMxicHeadsetProtocol.CreateSetEqPresetValkyrie(preset),
        MxicDialect.Class18 => RazerMxicHeadsetProtocol.CreateSetEqPresetClass18(preset),
        MxicDialect.Joliet => RazerMxicHeadsetProtocol.CreateSetEqPresetJoliet(preset),
        _ => RazerMxicHeadsetProtocol.CreateSetEqPreset(preset, ReportId),
    };

    private byte[] SetEqPresetHeader => _dialect switch
    {
        MxicDialect.Class18 => RazerMxicHeadsetProtocol.EqPresetHeaderClass18,
        MxicDialect.Joliet => RazerMxicHeadsetProtocol.EqPresetHeaderClass18,
        _ => RazerMxicHeadsetProtocol.SetEqPresetHeader,
    };

    public async Task<RazerHeadsetAudioState> ReadStateAsync(
        CancellationToken cancellationToken = default)
    {
        if (_dialect == MxicDialect.Protocol40)
        {
            return await ReadProtocol40StateAsync(cancellationToken).ConfigureAwait(false);
        }

        if (_dialect == MxicDialect.Joliet)
        {
            return await ReadJolietStateAsync(cancellationToken).ConfigureAwait(false);
        }

        var errors = new List<string>();
        var firmware = _dialect == MxicDialect.ValkyrieT3 ? (Ok: false, Value: (Version)null!) : await ProbeAsync(
            "firmware",
            RazerMxicHeadsetProtocol.CreateRequest(RazerMxicHeadsetProtocol.GetFirmware, reportId: ReportId),
            RazerMxicHeadsetProtocol.FirmwareHeader,
            RazerMxicHeadsetProtocol.ReadFirmware,
            errors,
            cancellationToken).ConfigureAwait(false);
        var battery = _dialect == MxicDialect.ValkyrieT3 ? (Ok: false, Value: (byte)0) : await ProbeAsync(
            "battery",
            RazerMxicHeadsetProtocol.CreateRequest(RazerMxicHeadsetProtocol.GetBattery, reportId: ReportId),
            RazerMxicHeadsetProtocol.BatteryHeader,
            RazerMxicHeadsetProtocol.ReadBatteryPercent,
            errors,
            cancellationToken).ConfigureAwait(false);
        var charging = _dialect == MxicDialect.ValkyrieT3 ? (Ok: false, Value: false) : await ProbeAsync(
            "charging",
            RazerMxicHeadsetProtocol.CreateRequest(RazerMxicHeadsetProtocol.GetCharging, reportId: ReportId),
            RazerMxicHeadsetProtocol.ChargingHeader,
            RazerMxicHeadsetProtocol.ReadChargingStatus,
            errors,
            cancellationToken).ConfigureAwait(false);
        var preset = await ProbeAsync(
            "EQ preset",
            CreateEqPresetRead(),
            EqPresetHeader,
            ReadEqPreset,
            errors,
            cancellationToken).ConfigureAwait(false);
        var bands = await ProbeAsync(
            "EQ bands",
            CreateEqBandsRead(),
            EqBandsHeader,
            RazerMxicHeadsetProtocol.ReadEqBands,
            errors,
            cancellationToken).ConfigureAwait(false);
        // The Valkyrie T3 chunk defines Get Mute State for its own source ids but no setter,
        // so the page leaves the toggle out instead of offering a write it cannot confirm.
        var micMute = (Ok: false, Value: false);
        if (_dialect == MxicDialect.ValkyrieT3)
        {
            micMute = await ProbeAsync("microphone mute",
                RazerValkyrieT3Protocol.Create(0x84, [0, 2, 0]), [0, 2],
                value => value.Length >= 1 ? value[0] != 0 : throw new InvalidDataException("Missing mute state."),
                errors, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            micMute = await ProbeAsync(
                "microphone mute",
                CreateMicMuteRead(),
                MicMuteHeader,
                RazerMxicHeadsetProtocol.ReadMicMute,
                errors,
                cancellationToken).ConfigureAwait(false);
        }

        return new RazerHeadsetAudioState(
            Firmware: firmware.Ok ? firmware.Value : null,
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
            Errors: errors);
    }

    private async Task<RazerHeadsetAudioState> ReadProtocol40StateAsync(
        CancellationToken cancellationToken)
    {
        var errors = new List<string>();
        await _transport.SendAsync(
            RazerProtocol40HeadsetProtocol.CreateConnect(), cancellationToken).ConfigureAwait(false);
        var battery = await ProbeAsync(
            "battery",
            RazerProtocol40HeadsetProtocol.CreateGetBattery(),
            [],
            RazerProtocol40HeadsetProtocol.ReadBatteryPercent,
            errors,
            cancellationToken).ConfigureAwait(false);
        var charging = await ProbeAsync(
            "charging",
            RazerProtocol40HeadsetProtocol.CreateGetCharging(),
            [],
            RazerProtocol40HeadsetProtocol.ReadChargingStatus,
            errors,
            cancellationToken).ConfigureAwait(false);

        return new RazerHeadsetAudioState(
            Firmware: null,
            BatteryPercent: battery.Ok ? battery.Value : null,
            Charging: charging.Ok ? charging.Value : null,
            EqPreset: null,
            EqBands: null,
            Bass: null,
            MicMuted: null,
            MicVolume: null,
            AudioSource: null,
            NoiseSuppression: null,
            AncState: null,
            AncLevel: null,
            Errors: errors);
    }

    private async Task<RazerHeadsetAudioState> ReadJolietStateAsync(
        CancellationToken cancellationToken)
    {
        var errors = new List<string>();
        var preset = await ProbeAsync(
            "EQ preset",
            CreateEqPresetRead(),
            EqPresetHeader,
            ReadEqPreset,
            errors,
            cancellationToken).ConfigureAwait(false);
        var bands = await ProbeAsync(
            "EQ bands",
            CreateEqBandsRead(),
            EqBandsHeader,
            RazerMxicHeadsetProtocol.ReadEqBands,
            errors,
            cancellationToken).ConfigureAwait(false);
        var bass = await ProbeAsync(
            "bass",
            RazerMxicHeadsetProtocol.CreateGetBassJoliet(),
            RazerMxicHeadsetProtocol.JolietBassHeader,
            RazerMxicHeadsetProtocol.ReadBass,
            errors,
            cancellationToken).ConfigureAwait(false);

        return new RazerHeadsetAudioState(
            Firmware: null,
            BatteryPercent: null,
            Charging: null,
            EqPreset: preset.Ok ? preset.Value : null,
            EqBands: bands.Ok ? bands.Value : null,
            Bass: bass.Ok ? bass.Value : null,
            MicMuted: null,
            MicVolume: null,
            AudioSource: null,
            NoiseSuppression: null,
            AncState: null,
            AncLevel: null,
            Errors: errors);
    }

    private Task SetEqPresetCoreAsync(byte preset, CancellationToken cancellationToken = default) =>
        _dialect == MxicDialect.ValkyrieT3 && preset > 3
            ? Task.FromException(new ArgumentOutOfRangeException(nameof(preset), "T3 exposes presets 0..3."))
            :
        _dialect == MxicDialect.Protocol40
            ? Task.FromException(new InvalidOperationException(
                "The protocol40 headset bundle does not expose an EQ preset command."))
            : WriteAsync(
            CreateEqPresetRead(),
            EqPresetHeader,
            ReadEqPreset,
            preset,
            CreateSetEqPreset,
            SetEqPresetHeader,
            value => value == preset,
            "EQ preset",
            cancellationToken);

    private async Task SetEqBandsCoreAsync(
        IReadOnlyList<byte> bands,
        CancellationToken cancellationToken = default)
    {
        if (_dialect == MxicDialect.Protocol40)
        {
            throw new InvalidOperationException(
                "The protocol40 headset bundle does not expose an EQ band command.");
        }

        ArgumentNullException.ThrowIfNull(bands);
        if (bands.Count != RazerMxicHeadsetProtocol.TenBandCount)
        {
            throw new ArgumentException("Equalizer data must contain 10 bands.", nameof(bands));
        }

        if (_dialect == MxicDialect.ValkyrieT3 && bands.Any(value => value > 6 && value is not (>= 129 and <= 134)))
            throw new ArgumentOutOfRangeException(nameof(bands), "Valkyrie T3 bands must encode -6..6 dB.");
        var requested = bands.ToArray();

        if (_dialect == MxicDialect.Joliet)
        {
            if (requested.Any(value => value > 6 && value is not (>= 129 and <= 134)))
                throw new ArgumentOutOfRangeException(nameof(bands), "Joliet bands must encode -6..6 dB.");
            var preset = ReadEqPreset(await QueryReportAsync(
                CreateEqPresetRead(), EqPresetHeader, "read EQ preset", cancellationToken).ConfigureAwait(false));
            // setEQSetting already carries the preset and the new curve. Reading
            // the curve back here can return the previous values and make the
            // restore path undo a write the device accepted.
            await WriteReportAsync(
                RazerMxicHeadsetProtocol.CreateSetEqBandsJoliet(preset, requested),
                SetEqBandsHeader,
                "EQ bands",
                cancellationToken).ConfigureAwait(false);
            return;
        }

        await WriteAsync(
            CreateEqBandsRead(),
            EqBandsHeader,
            RazerMxicHeadsetProtocol.ReadEqBands,
            requested,
            CreateSetEqBands,
            SetEqBandsHeader,
            actual => actual.SequenceEqual(requested),
            "EQ bands",
            cancellationToken).ConfigureAwait(false);
    }

    public Task SetBassAsync(byte level, CancellationToken cancellationToken = default)
    {
        if (_dialect != MxicDialect.Joliet)
        {
            return Task.FromException(new NotSupportedException(
                "This audio dialect does not expose a hardware bass level."));
        }

        return WriteAsync(
            RazerMxicHeadsetProtocol.CreateGetBassJoliet(),
            RazerMxicHeadsetProtocol.JolietBassHeader,
            RazerMxicHeadsetProtocol.ReadBass,
            level,
            RazerMxicHeadsetProtocol.CreateSetBassJoliet,
            [],
            value => value == level,
            "bass",
            cancellationToken);
    }

    public Task SetMicMuteAsync(bool muted, CancellationToken cancellationToken = default) =>
        _dialect switch
        {
            MxicDialect.Protocol40 => Task.FromException(new InvalidOperationException(
                "The protocol40 headset bundle does not expose a microphone mute command.")),
            MxicDialect.ValkyrieT3 => Task.FromException(new InvalidOperationException(
                "The Valkyrie T3 bundle exposes Get Mute State but no microphone mute setter.")),
            _ => WriteAsync(
                CreateMicMuteRead(),
                MicMuteHeader,
                RazerMxicHeadsetProtocol.ReadMicMute,
                muted,
                CreateSetMicMute,
                SetMicMuteHeader,
                value => value == muted,
                "microphone mute",
                cancellationToken),
        };

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
        var previous = parse(await QueryReportAsync(
            readRequest, readHeader, $"read {label}", cancellationToken).ConfigureAwait(false));
        if (matches(previous))
        {
            return;
        }

        try
        {
            await WriteReportAsync(
                writeRequest(value), writeHeader, $"set {label}", cancellationToken).ConfigureAwait(false);
            var actual = parse(await QueryReportAsync(
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
            return (true, parse(await QueryReportAsync(
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
            await WriteReportAsync(request, expectedHeader, label, CancellationToken.None)
                .ConfigureAwait(false);
        }
        catch (Exception exception) when (IsExpected(exception))
        {
            _diagnostic?.Invoke($"mxic {label} failed: {exception.Message}");
        }
    }

    private async Task WriteReportAsync(
        byte[] report,
        byte[] expectedHeader,
        string label,
        CancellationToken cancellationToken)
    {
        await QueryReportAsync(report, expectedHeader, label, cancellationToken).ConfigureAwait(false);
    }

    private async Task<byte[]> QueryReportAsync(
        byte[] report,
        byte[] expectedHeader,
        string label,
        CancellationToken cancellationToken)
    {
        await EnsureRemoteModeAsync(cancellationToken).ConfigureAwait(false);
        _diagnostic?.Invoke(_dialect == MxicDialect.Protocol40
            ? $"protocol40 {label}: class=0x{report[6]:X2} id=0x{report[7]:X2} size=0x{report[8]:X2}"
            : $"mxic {label}: command=0x{report[10]:X2} 0x{report[11]:X2} 0x{report[12]:X2}");
        var value = await _transport.QueryAsync(report, expectedHeader, cancellationToken)
            .ConfigureAwait(false);
        return value ?? throw new InvalidOperationException(
            $"MXIC {label} returned no matching response.");
    }

    private async Task EnsureRemoteModeAsync(CancellationToken cancellationToken)
    {
        if (_remoteMode || _dialect is MxicDialect.Protocol40 or MxicDialect.ValkyrieT3 or MxicDialect.Joliet)
        {
            return;
        }

        var request = RazerMxicHeadsetProtocol.CreateRemoteMode(true, ReportId);
        if (_dialect != MxicDialect.Legacy)
        {
            await _transport.SendAsync(request, cancellationToken).ConfigureAwait(false);
            _remoteMode = true;
            return;
        }

        // AudioMxicDevice sends the remote-mode command up to three times and only
        // continues when the input report's first payload byte is STATUS_OK (0x4F).
        byte? lastStatus = null;
        for (var attempt = 0; attempt < 3; attempt++)
        {
            lastStatus = await _transport
                .SendAndReadStatusAsync(request, cancellationToken)
                .ConfigureAwait(false);
            if (lastStatus == RazerMxicHeadsetProtocol.StatusOk)
            {
                _remoteMode = true;
                return;
            }

            await Task.Delay(10 * (attempt + 1), cancellationToken).ConfigureAwait(false);
        }

        throw new InvalidOperationException(lastStatus is { } status
            ? $"MXIC remote mode was not acknowledged after 3 attempts (status 0x{status:X2})."
            : "MXIC remote mode was not acknowledged after 3 attempts.");
    }

    private static bool IsExpected(Exception exception) =>
        exception is IOException or InvalidDataException or InvalidOperationException or
            NotSupportedException or Win32Exception or UnauthorizedAccessException;

    public async ValueTask DisposeAsync()
    {
        await _transport.DisposeAsync().ConfigureAwait(false);
    }
}
