using System.ComponentModel;
using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.Windows.Devices;

/// <summary>
/// Audio controls for the 64-byte MXIC / class-0x18 channels and the protocol40
/// headset channel (1532:0593). These models use report id 2 instead of the
/// 91-byte protocol 25 feature reports; protocol40 only exposes battery and
/// charging in its selected device class.
/// </summary>
public sealed class RazerMxicHeadsetService : IAsyncDisposable
{
    private enum MxicDialect
    {
        Legacy,
        Class18,
        ValkyrieT3,
        Protocol40,
    }

    private readonly IRazerMxicHeadsetTransport _transport;
    private readonly Action<string>? _diagnostic;
    private readonly MxicDialect _dialect;
    private bool _remoteMode;

    public RazerMxicHeadsetService(ushort productId, string devicePath, Action<string>? diagnostic = null)
        : this(
            new RazerMxicHeadsetTransport(devicePath, RazerMxicHeadsetProtocol.ReportLength),
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
            : RazerMxicHeadsetProtocol.UsesValkyrieT3Commands(productId) ? MxicDialect.ValkyrieT3
            : RazerMxicHeadsetProtocol.UsesClass18Commands(productId) ? MxicDialect.Class18
            : MxicDialect.Legacy;
        _diagnostic = diagnostic;
    }

    public static bool IsSupported(ushort productId) =>
        RazerMxicHeadsetProtocol.IsSupported(productId) ||
        RazerProtocol40HeadsetProtocol.IsSupported(productId);

    private byte ReportId => _dialect == MxicDialect.ValkyrieT3
        ? RazerMxicHeadsetProtocol.ValkyrieReportId
        : RazerMxicHeadsetProtocol.ReportId;

    private byte[] CreateEqPresetRead() => _dialect switch
    {
        MxicDialect.ValkyrieT3 => RazerMxicHeadsetProtocol.CreateGetEqPresetValkyrie(),
        MxicDialect.Class18 => RazerMxicHeadsetProtocol.CreateGetEqPresetClass18(),
        _ => RazerMxicHeadsetProtocol.CreateRequest(RazerMxicHeadsetProtocol.GetEqPreset, reportId: ReportId),
    };

    private byte[] EqPresetHeader => _dialect switch
    {
        MxicDialect.ValkyrieT3 => RazerMxicHeadsetProtocol.EqHeaderValkyrie,
        MxicDialect.Class18 => RazerMxicHeadsetProtocol.EqPresetHeaderClass18,
        _ => RazerMxicHeadsetProtocol.EqPresetHeader,
    };

    private byte ReadEqPreset(ReadOnlySpan<byte> value) => _dialect == MxicDialect.Legacy
        ? RazerMxicHeadsetProtocol.ReadEqPreset(value)
        : RazerMxicHeadsetProtocol.ReadEqPresetClass18(value);

    private byte[] CreateEqBandsRead() => _dialect switch
    {
        MxicDialect.ValkyrieT3 => RazerMxicHeadsetProtocol.CreateGetEqBandsValkyrie(),
        MxicDialect.Class18 => RazerMxicHeadsetProtocol.CreateGetEqBandsClass18(),
        _ => RazerMxicHeadsetProtocol.CreateRequest(RazerMxicHeadsetProtocol.GetEqBands, reportId: ReportId),
    };

    private byte[] EqBandsHeader => _dialect switch
    {
        MxicDialect.ValkyrieT3 => RazerMxicHeadsetProtocol.EqHeaderValkyrie,
        MxicDialect.Class18 => RazerMxicHeadsetProtocol.EqBandsHeaderClass18,
        _ => RazerMxicHeadsetProtocol.EqBandsHeader,
    };

    private byte[] CreateSetEqBands(byte[] bands) => _dialect switch
    {
        MxicDialect.ValkyrieT3 => RazerMxicHeadsetProtocol.CreateSetEqBandsValkyrie(bands),
        MxicDialect.Class18 => RazerMxicHeadsetProtocol.CreateSetEqBandsClass18(bands),
        _ => RazerMxicHeadsetProtocol.CreateSetEqBands(bands),
    };

    // Valkyrie T3 writes are fire-and-forget and verified by the readback below; these
    // headers remain only for the class-0x18 and legacy paths.
    private byte[] SetEqBandsHeader => _dialect switch
    {
        MxicDialect.Class18 => RazerMxicHeadsetProtocol.EqBandsHeaderClass18,
        _ => RazerMxicHeadsetProtocol.SetEqBandsHeader,
    };

    private byte[] CreateMicMuteRead() => _dialect != MxicDialect.Legacy
        ? RazerMxicHeadsetProtocol.CreateGetMicMuteClass18(ReportId)
        : RazerMxicHeadsetProtocol.CreateRequest(RazerMxicHeadsetProtocol.GetMicMute, reportId: ReportId);

    private byte[] MicMuteHeader => _dialect != MxicDialect.Legacy
        ? RazerMxicHeadsetProtocol.MicMuteHeaderClass18
        : RazerMxicHeadsetProtocol.MicMuteHeader;

    private byte[] CreateSetMicMute(bool muted) => _dialect != MxicDialect.Legacy
        ? RazerMxicHeadsetProtocol.CreateSetMicMuteClass18(muted, ReportId)
        : RazerMxicHeadsetProtocol.CreateSetMicMute(muted);

    private byte[] SetMicMuteHeader => _dialect != MxicDialect.Legacy
        ? RazerMxicHeadsetProtocol.MicMuteHeaderClass18
        : RazerMxicHeadsetProtocol.SetMicMuteHeader;

    private byte[] CreateSetEqPreset(byte preset) => _dialect switch
    {
        MxicDialect.ValkyrieT3 => RazerMxicHeadsetProtocol.CreateSetEqPresetValkyrie(preset),
        MxicDialect.Class18 => RazerMxicHeadsetProtocol.CreateSetEqPresetClass18(preset),
        _ => RazerMxicHeadsetProtocol.CreateSetEqPreset(preset),
    };

    private byte[] SetEqPresetHeader => _dialect switch
    {
        MxicDialect.Class18 => RazerMxicHeadsetProtocol.EqPresetHeaderClass18,
        _ => RazerMxicHeadsetProtocol.SetEqPresetHeader,
    };

    public async Task<RazerHeadsetAudioState> ReadStateAsync(
        CancellationToken cancellationToken = default)
    {
        if (_dialect == MxicDialect.Protocol40)
        {
            return await ReadProtocol40StateAsync(cancellationToken).ConfigureAwait(false);
        }

        var errors = new List<string>();
        var firmware = await ProbeAsync(
            "firmware",
            RazerMxicHeadsetProtocol.CreateRequest(RazerMxicHeadsetProtocol.GetFirmware, reportId: ReportId),
            RazerMxicHeadsetProtocol.FirmwareHeader,
            RazerMxicHeadsetProtocol.ReadFirmware,
            errors,
            cancellationToken).ConfigureAwait(false);
        var battery = await ProbeAsync(
            "battery",
            RazerMxicHeadsetProtocol.CreateRequest(RazerMxicHeadsetProtocol.GetBattery, reportId: ReportId),
            RazerMxicHeadsetProtocol.BatteryHeader,
            RazerMxicHeadsetProtocol.ReadBatteryPercent,
            errors,
            cancellationToken).ConfigureAwait(false);
        var charging = await ProbeAsync(
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
        if (_dialect != MxicDialect.ValkyrieT3)
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

    public Task SetEqPresetAsync(byte preset, CancellationToken cancellationToken = default) =>
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

    public async Task SetEqBandsAsync(
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

        var requested = bands.ToArray();
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
        if (_dialect == MxicDialect.ValkyrieT3)
        {
            await EnsureRemoteModeAsync(cancellationToken).ConfigureAwait(false);
            _diagnostic?.Invoke(
                $"mxic {label}: report id {report[0]} size=0x{report[9]:X2} sent without an ack");
            await _transport.SendAsync(report, cancellationToken).ConfigureAwait(false);
            return;
        }

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
        if (_remoteMode || _dialect == MxicDialect.Protocol40)
        {
            return;
        }

        await _transport.SendAsync(
            RazerMxicHeadsetProtocol.CreateRemoteMode(true, ReportId), cancellationToken).ConfigureAwait(false);
        _remoteMode = true;
    }

    private static bool IsExpected(Exception exception) =>
        exception is IOException or InvalidDataException or InvalidOperationException or
            NotSupportedException or Win32Exception or UnauthorizedAccessException;

    public async ValueTask DisposeAsync()
    {
        await _transport.DisposeAsync().ConfigureAwait(false);
    }
}
