using System.ComponentModel;
using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.Windows.Devices;

/// <summary>
/// Audio controls for the 2023 BlackShark V2 Pro (1532:0555). This model uses the
/// 64-byte MXIC channel instead of the 91-byte protocol 25 feature reports.
/// </summary>
public sealed class RazerMxicHeadsetService : IAsyncDisposable
{
    private readonly IRazerMxicHeadsetTransport _transport;
    private readonly Action<string>? _diagnostic;
    private readonly bool _class18;
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
        _class18 = RazerMxicHeadsetProtocol.UsesClass18Commands(productId);
        _diagnostic = diagnostic;
    }

    public static bool IsSupported(ushort productId) => RazerMxicHeadsetProtocol.IsSupported(productId);

    private byte[] CreateEqPresetRead() => _class18
        ? RazerMxicHeadsetProtocol.CreateGetEqPresetClass18()
        : RazerMxicHeadsetProtocol.CreateRequest(RazerMxicHeadsetProtocol.GetEqPreset);

    private byte[] EqPresetHeader => _class18
        ? RazerMxicHeadsetProtocol.EqPresetHeaderClass18
        : RazerMxicHeadsetProtocol.EqPresetHeader;

    private byte ReadEqPreset(ReadOnlySpan<byte> value) => _class18
        ? RazerMxicHeadsetProtocol.ReadEqPresetClass18(value)
        : RazerMxicHeadsetProtocol.ReadEqPreset(value);

    private byte[] CreateEqBandsRead() => _class18
        ? RazerMxicHeadsetProtocol.CreateGetEqBandsClass18()
        : RazerMxicHeadsetProtocol.CreateRequest(RazerMxicHeadsetProtocol.GetEqBands);

    private byte[] EqBandsHeader => _class18
        ? RazerMxicHeadsetProtocol.EqBandsHeaderClass18
        : RazerMxicHeadsetProtocol.EqBandsHeader;

    private byte[] CreateSetEqBands(byte[] bands) => _class18
        ? RazerMxicHeadsetProtocol.CreateSetEqBandsClass18(bands)
        : RazerMxicHeadsetProtocol.CreateSetEqBands(bands);

    private byte[] SetEqBandsHeader => _class18
        ? RazerMxicHeadsetProtocol.EqBandsHeaderClass18
        : RazerMxicHeadsetProtocol.SetEqBandsHeader;

    private byte[] CreateMicMuteRead() => _class18
        ? RazerMxicHeadsetProtocol.CreateGetMicMuteClass18()
        : RazerMxicHeadsetProtocol.CreateRequest(RazerMxicHeadsetProtocol.GetMicMute);

    private byte[] MicMuteHeader => _class18
        ? RazerMxicHeadsetProtocol.MicMuteHeaderClass18
        : RazerMxicHeadsetProtocol.MicMuteHeader;

    private byte[] CreateSetMicMute(bool muted) => _class18
        ? RazerMxicHeadsetProtocol.CreateSetMicMuteClass18(muted)
        : RazerMxicHeadsetProtocol.CreateSetMicMute(muted);

    private byte[] SetMicMuteHeader => _class18
        ? RazerMxicHeadsetProtocol.MicMuteHeaderClass18
        : RazerMxicHeadsetProtocol.SetMicMuteHeader;

    private byte[] CreateSetEqPreset(byte preset) => _class18
        ? RazerMxicHeadsetProtocol.CreateSetEqPresetClass18(preset)
        : RazerMxicHeadsetProtocol.CreateSetEqPreset(preset);

    private byte[] SetEqPresetHeader => _class18
        ? RazerMxicHeadsetProtocol.EqPresetHeaderClass18
        : RazerMxicHeadsetProtocol.SetEqPresetHeader;

    public async Task<RazerHeadsetAudioState> ReadStateAsync(
        CancellationToken cancellationToken = default)
    {
        var errors = new List<string>();
        var firmware = await ProbeAsync(
            "firmware",
            RazerMxicHeadsetProtocol.CreateRequest(RazerMxicHeadsetProtocol.GetFirmware),
            RazerMxicHeadsetProtocol.FirmwareHeader,
            RazerMxicHeadsetProtocol.ReadFirmware,
            errors,
            cancellationToken).ConfigureAwait(false);
        var battery = await ProbeAsync(
            "battery",
            RazerMxicHeadsetProtocol.CreateRequest(RazerMxicHeadsetProtocol.GetBattery),
            RazerMxicHeadsetProtocol.BatteryHeader,
            RazerMxicHeadsetProtocol.ReadBatteryPercent,
            errors,
            cancellationToken).ConfigureAwait(false);
        var charging = await ProbeAsync(
            "charging",
            RazerMxicHeadsetProtocol.CreateRequest(RazerMxicHeadsetProtocol.GetCharging),
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
        var micMute = await ProbeAsync(
            "microphone mute",
            CreateMicMuteRead(),
            MicMuteHeader,
            RazerMxicHeadsetProtocol.ReadMicMute,
            errors,
            cancellationToken).ConfigureAwait(false);

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

    public Task SetEqPresetAsync(byte preset, CancellationToken cancellationToken = default) =>
        WriteAsync(
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
        WriteAsync(
            CreateMicMuteRead(),
            MicMuteHeader,
            RazerMxicHeadsetProtocol.ReadMicMute,
            muted,
            CreateSetMicMute,
            SetMicMuteHeader,
            value => value == muted,
            "microphone mute",
            cancellationToken);

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
            await QueryReportAsync(
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
            await QueryReportAsync(request, expectedHeader, label, CancellationToken.None)
                .ConfigureAwait(false);
        }
        catch (Exception exception) when (IsExpected(exception))
        {
            _diagnostic?.Invoke($"mxic {label} failed: {exception.Message}");
        }
    }

    private async Task<byte[]> QueryReportAsync(
        byte[] report,
        byte[] expectedHeader,
        string label,
        CancellationToken cancellationToken)
    {
        await EnsureRemoteModeAsync(cancellationToken).ConfigureAwait(false);
        _diagnostic?.Invoke(
            $"mxic {label}: command=0x{report[10]:X2} 0x{report[11]:X2} 0x{report[12]:X2}");
        var value = await _transport.QueryAsync(report, expectedHeader, cancellationToken)
            .ConfigureAwait(false);
        return value ?? throw new InvalidOperationException(
            $"MXIC {label} returned no matching response.");
    }

    private async Task EnsureRemoteModeAsync(CancellationToken cancellationToken)
    {
        if (_remoteMode)
        {
            return;
        }

        await _transport.SendAsync(
            RazerMxicHeadsetProtocol.CreateRemoteMode(true), cancellationToken).ConfigureAwait(false);
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
