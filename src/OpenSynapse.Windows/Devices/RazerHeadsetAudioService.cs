using System.ComponentModel;
using OpenSynapse.Core.Devices;
using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.Windows.Devices;

public sealed record RazerHeadsetAudioConnection(
    string DevicePath,
    ushort ProductId,
    string Name,
    ushort FeatureReportByteLength);

public sealed record RazerHeadsetAudioState(
    Version? Firmware,
    byte? BatteryPercent,
    bool? Charging,
    byte? EqPreset,
    IReadOnlyList<byte>? EqBands,
    byte? Bass,
    bool? MicMuted,
    byte? MicVolume,
    RazerAudioSource? AudioSource,
    bool? NoiseSuppression,
    RazerAncState? AncState,
    byte? AncLevel,
    IReadOnlyList<string> Errors);

/// <summary>
/// Audio controls for the 18-device protocol 25 headset family (Nari, Kraken, Barracuda,
/// BlackShark V2 Pro, USB audio controllers). Identity and lighting stay on the device row;
/// this service owns the EQ, bass, microphone, audio-source, ANC and noise-suppression
/// commands that OpenRazer does not model.
/// </summary>
public sealed class RazerHeadsetAudioService
{
    public static readonly IReadOnlyDictionary<ushort, string> Products =
        new Dictionary<ushort, string>
        {
            [0x051A] = "Razer Nari Ultimate",
            [0x051C] = "Razer Nari",
            [0x051E] = "Razer Nari",
            [0x0520] = "Razer USB Audio Controller",
            [0x0521] = "Razer Kraken Kitty Edition",
            [0x0527] = "Razer Kraken Ultimate",
            [0x0528] = "Razer BlackShark V2 Pro",
            [0x0529] = "Razer USB Sound Card",
            [0x052D] = "Razer Kraken V3 Pro",
            [0x0533] = "Razer Kraken V3 HyperSense",
            [0x0539] = "Razer Barracuda Pro",
            [0x053B] = "Razer Barracuda 2.4",
            [0x053E] = "Razer Audio Mixer",
            [0x0549] = "Razer Kraken V3",
            [0x0554] = "Razer Kraken Kitty V2 Pro",
            [0x0555] = "Razer BlackShark V2 Pro",
            [0x0567] = "Razer Kraken V4 Pro",
            [0x056B] = "Razer Kraken V4",
            [0x0309] = "Razer Kraken BT Sanrio Limited Edition",
            [0x0526] = "Razer Kraken X USB",
            [0x0530] = "Razer Kraken BT Kitty Edition",
            [0x0537] = "Razer Kraken V3 X",
            [0x0534] = "Razer Kraken BT Sanrio Limited Edition",
            [0x0560] = "Razer Kraken Kitty V2",
            [0x0562] = "Razer Kraken Kitty V2 BT Quartz Edition",
            [0x0532] = "Razer Leviathan V2",
            [0x055A] = "Razer Nommo V2 Pro",
            [0x055C] = "Razer Nommo V2",
            [0x055E] = "Razer Nommo V2 X",
            [0x0542] = "Razer Seiren V2 Pro",
            [0x0543] = "Razer Seiren V2 X",
            [0x056A] = "Razer Seiren V3 Mini",
            [0x0548] = "Razer Leviathan V2 Pro",
            [0x056D] = "Razer Kraken V4 X",
            [0x0F66] = "Razer Marci",
            // The Seiren Emote shares the Camyt3 middleware bundle; its LED matrix uses the
            // class 0x10 Chroma commands, while EQ and mic volume match this family.
            [0x0F1B] = "Razer Seiren Emote",
        };

    private static readonly IReadOnlySet<ushort> SpeakerProductIds =
        new HashSet<ushort> { 0x0532, 0x055A, 0x055C, 0x055E, 0x0548 };

    private static readonly IReadOnlySet<ushort> MicrophoneProductIds =
        new HashSet<ushort> { 0x0542, 0x0543, 0x056A, 0x0F1B };

    private readonly IRazerFeatureTransport _transport;
    private readonly string _devicePath;
    private readonly TimeSpan _deviceWait;
    private readonly Action<string>? _diagnostic;
    private byte _transactionId = 0x1F;

    internal RazerHeadsetAudioService(
        IRazerFeatureTransport transport,
        string devicePath,
        TimeSpan deviceWait,
        Action<string>? diagnostic = null)
    {
        _transport = transport ?? throw new ArgumentNullException(nameof(transport));
        _devicePath = string.IsNullOrWhiteSpace(devicePath)
            ? throw new ArgumentException("A HID device path is required.", nameof(devicePath))
            : devicePath;
        _deviceWait = deviceWait;
        _diagnostic = diagnostic;
    }

    public static bool IsSupported(ushort productId) => Products.ContainsKey(productId);

    public static DeviceCategory GetCategory(ushort productId) =>
        MicrophoneProductIds.Contains(productId) ? DeviceCategory.Microphone
        : SpeakerProductIds.Contains(productId) ? DeviceCategory.Speaker
        : DeviceCategory.Headset;

    public static async Task<RazerHeadsetAudioConnection?> FindAsync(
        Action<string>? diagnostic = null,
        CancellationToken cancellationToken = default)
    {
        var interfaces = await WindowsHidDiscovery.FindVendorFeatureInterfacesAsync(
            0x1532,
            RazerFeatureReport.Length,
            cancellationToken,
            item => diagnostic?.Invoke(
                $"headset-audio interface pid={item.ProductId:X4} product='{item.ProductName}' " +
                $"usage=0x{item.UsagePage:X4}:0x{item.Usage:X4} feature={item.FeatureReportByteLength} " +
                $"access={item.Access}"))
            .ConfigureAwait(false);

        var candidates = interfaces
            .Where(item => Products.ContainsKey(item.ProductId))
            .OrderByDescending(item => item.Access == DeviceAccessState.Available)
            .ThenByDescending(item => item.UsagePage == 0xFF14)
            .ThenBy(item => item.ProductId)
            .ToArray();
        if (candidates.Length == 0)
        {
            diagnostic?.Invoke(
                "headset-audio found no admitted 91-byte interface; " +
                $"enumerated={string.Join(",", interfaces.Select(item => $"{item.ProductId:X4}:0x{item.UsagePage:X4}"))}");
            return null;
        }

        var chosen = candidates[0];
        diagnostic?.Invoke(
            $"headset-audio selected pid={chosen.ProductId:X4} product='{chosen.ProductName}' " +
            $"path={RazerFeatureTransport.DescribeDevicePath(chosen.DevicePath)} " +
            $"usage=0x{chosen.UsagePage:X4}:0x{chosen.Usage:X4} access={chosen.Access}");
        return new RazerHeadsetAudioConnection(
            chosen.DevicePath,
            chosen.ProductId,
            Products[chosen.ProductId],
            chosen.FeatureReportByteLength);
    }

    public async Task<RazerHeadsetAudioState> ReadStateAsync(CancellationToken cancellationToken = default)
    {
        var errors = new List<string>();
        var firmware = await ProbeAsync(
            "firmware", RazerHeadsetAudioProtocol.GetFirmware, RazerHeadsetAudioProtocol.ReadFirmware,
            errors, cancellationToken).ConfigureAwait(false);
        var battery = await ProbeAsync(
            "battery", RazerHeadsetAudioProtocol.GetBatteryLevel, RazerHeadsetAudioProtocol.ReadBatteryPercent,
            errors, cancellationToken).ConfigureAwait(false);
        var charging = await ProbeAsync(
            "charging", RazerHeadsetAudioProtocol.GetChargingStatus, RazerHeadsetAudioProtocol.ReadChargingStatus,
            errors, cancellationToken).ConfigureAwait(false);
        var preset = await ProbeAsync(
            "EQ preset", txn => RazerHeadsetAudioProtocol.GetEqPreset(txn), RazerHeadsetAudioProtocol.ReadEqPreset,
            errors, cancellationToken).ConfigureAwait(false);
        var bass = await ProbeAsync(
            "bass", txn => RazerHeadsetAudioProtocol.GetBass(txn), RazerHeadsetAudioProtocol.ReadBass,
            errors, cancellationToken).ConfigureAwait(false);
        var micMute = await ProbeAsync(
            "microphone mute", txn => RazerHeadsetAudioProtocol.GetMicMute(txn), RazerHeadsetAudioProtocol.ReadMicMute,
            errors, cancellationToken).ConfigureAwait(false);
        var micVolume = await ProbeAsync(
            "microphone volume", txn => RazerHeadsetAudioProtocol.GetMicVolume(txn), RazerHeadsetAudioProtocol.ReadMicVolume,
            errors, cancellationToken).ConfigureAwait(false);
        var source = await ProbeAsync(
            "audio source", txn => RazerHeadsetAudioProtocol.GetAudioSource(txn), RazerHeadsetAudioProtocol.ReadAudioSource,
            errors, cancellationToken).ConfigureAwait(false);
        var noise = await ProbeAsync(
            "noise suppression", txn => RazerHeadsetAudioProtocol.GetNoiseSuppression(txn),
            RazerHeadsetAudioProtocol.ReadNoiseSuppression, errors, cancellationToken).ConfigureAwait(false);

        IReadOnlyList<byte>? bands = null;
        try
        {
            bands = await ReadEqBandsRequiredAsync(true, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (IsExpected(exception))
        {
            try
            {
                bands = await ReadEqBandsRequiredAsync(false, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception fallback) when (IsExpected(fallback))
            {
                errors.Add($"EQ bands: {exception.Message}; 8-band fallback: {fallback.Message}");
            }
        }

        RazerAncState? ancState = null;
        byte? ancLevel = null;
        try
        {
            (ancState, ancLevel) = RazerHeadsetAudioProtocol.ReadAnc(
                await QueryAsync(txn => RazerHeadsetAudioProtocol.GetAnc(txn), "read ANC", cancellationToken)
                    .ConfigureAwait(false));
        }
        catch (Exception exception) when (IsExpected(exception))
        {
            errors.Add($"ANC: {exception.Message}");
        }

        return new RazerHeadsetAudioState(
            firmware.Ok ? firmware.Value : null,
            battery.Ok ? battery.Value : null,
            charging.Ok ? charging.Value : null,
            preset.Ok ? preset.Value : null,
            bands,
            bass.Ok ? bass.Value : null,
            micMute.Ok ? micMute.Value : null,
            micVolume.Ok ? micVolume.Value : null,
            source.Ok ? source.Value : null,
            noise.Ok ? noise.Value : null,
            ancState,
            ancLevel,
            errors);
    }

    public Task SetEqPresetAsync(byte preset, CancellationToken cancellationToken = default)
    {
        if (preset == 0xFF)
        {
            throw new ArgumentOutOfRangeException(nameof(preset));
        }

        return WriteAsync(
            txn => RazerHeadsetAudioProtocol.GetEqPreset(txn),
            RazerHeadsetAudioProtocol.ReadEqPreset,
            preset,
            (value, txn) => RazerHeadsetAudioProtocol.SetEqPreset(txn, value),
            value => value == preset,
            "EQ preset",
            cancellationToken);
    }

    public async Task SetEqBandsAsync(
        IReadOnlyList<byte> bands,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(bands);
        var tenBand = bands.Count == RazerHeadsetAudioProtocol.TenBandCount;
        if (!tenBand && bands.Count != RazerHeadsetAudioProtocol.EightBandCount)
        {
            throw new ArgumentException("Equalizer data must contain 10 or 8 bands.", nameof(bands));
        }

        var previous = await ReadEqBandsRequiredAsync(tenBand, cancellationToken).ConfigureAwait(false);
        if (previous.SequenceEqual(bands))
        {
            return;
        }

        try
        {
            await QueryAsync(
                txn => RazerHeadsetAudioProtocol.SetEqBands(txn, bands.ToArray()),
                "set EQ bands",
                cancellationToken).ConfigureAwait(false);
            var actual = await ReadEqBandsRequiredAsync(tenBand, cancellationToken).ConfigureAwait(false);
            if (!actual.SequenceEqual(bands))
            {
                throw new InvalidOperationException("EQ band readback did not match the requested values.");
            }
        }
        catch
        {
            await TryRestoreAsync(
                txn => RazerHeadsetAudioProtocol.SetEqBands(txn, previous.ToArray()),
                "restore EQ bands").ConfigureAwait(false);
            throw;
        }
    }

    public Task SetBassAsync(byte level, CancellationToken cancellationToken = default)
    {
        if (level > 100)
        {
            throw new ArgumentOutOfRangeException(nameof(level), "Bass level must be between 0 and 100.");
        }

        return WriteAsync(
            txn => RazerHeadsetAudioProtocol.GetBass(txn),
            RazerHeadsetAudioProtocol.ReadBass,
            level,
            (value, txn) => RazerHeadsetAudioProtocol.SetBass(txn, value),
            value => value == level,
            "bass",
            cancellationToken);
    }

    public Task SetMicMuteAsync(bool muted, CancellationToken cancellationToken = default) =>
        WriteAsync(
            txn => RazerHeadsetAudioProtocol.GetMicMute(txn),
            RazerHeadsetAudioProtocol.ReadMicMute,
            muted,
            (value, txn) => RazerHeadsetAudioProtocol.SetMicMute(txn, value),
            value => value == muted,
            "microphone mute",
            cancellationToken);

    public Task SetMicVolumeAsync(byte volume, CancellationToken cancellationToken = default)
    {
        if (volume > 100)
        {
            throw new ArgumentOutOfRangeException(nameof(volume), "Microphone volume must be between 0 and 100.");
        }

        return WriteAsync(
            txn => RazerHeadsetAudioProtocol.GetMicVolume(txn),
            RazerHeadsetAudioProtocol.ReadMicVolume,
            volume,
            (value, txn) => RazerHeadsetAudioProtocol.SetMicVolume(txn, value),
            value => value == volume,
            "microphone volume",
            cancellationToken);
    }

    public Task SetAudioSourceAsync(RazerAudioSource source, CancellationToken cancellationToken = default)
    {
        if (!Enum.IsDefined(source))
        {
            throw new ArgumentOutOfRangeException(nameof(source));
        }

        return WriteAsync(
            txn => RazerHeadsetAudioProtocol.GetAudioSource(txn),
            RazerHeadsetAudioProtocol.ReadAudioSource,
            source,
            (value, txn) => RazerHeadsetAudioProtocol.SetAudioSource(txn, value),
            value => value == source,
            "audio source",
            cancellationToken);
    }

    public Task SetNoiseSuppressionAsync(bool enabled, CancellationToken cancellationToken = default) =>
        WriteAsync(
            txn => RazerHeadsetAudioProtocol.GetNoiseSuppression(txn),
            RazerHeadsetAudioProtocol.ReadNoiseSuppression,
            enabled,
            (value, txn) => RazerHeadsetAudioProtocol.SetNoiseSuppression(txn, value),
            value => value == enabled,
            "noise suppression",
            cancellationToken);

    public async Task SetAncAsync(
        RazerAncState state,
        byte level,
        CancellationToken cancellationToken = default)
    {
        if (!Enum.IsDefined(state))
        {
            throw new ArgumentOutOfRangeException(nameof(state));
        }
        if (level > RazerHeadsetAudioProtocol.TenBandCount)
        {
            throw new ArgumentOutOfRangeException(nameof(level), "ANC level must be between 0 and 10.");
        }

        var previous = RazerHeadsetAudioProtocol.ReadAnc(
            await QueryAsync(txn => RazerHeadsetAudioProtocol.GetAnc(txn), "read ANC", cancellationToken)
                .ConfigureAwait(false));
        if (previous.State == state && previous.Level == level)
        {
            return;
        }

        try
        {
            await QueryAsync(
                txn => RazerHeadsetAudioProtocol.SetAnc(txn, state, level),
                "set ANC",
                cancellationToken).ConfigureAwait(false);
            var actual = RazerHeadsetAudioProtocol.ReadAnc(
                await QueryAsync(txn => RazerHeadsetAudioProtocol.GetAnc(txn), "read back ANC", cancellationToken)
                    .ConfigureAwait(false));
            if (actual.State != state || actual.Level != level)
            {
                throw new InvalidOperationException(
                    $"ANC readback was {actual.State}/{actual.Level}, expected {state}/{level}.");
            }
        }
        catch
        {
            await TryRestoreAsync(
                txn => RazerHeadsetAudioProtocol.SetAnc(txn, previous.State, previous.Level),
                "restore ANC").ConfigureAwait(false);
            throw;
        }
    }

    private async Task WriteAsync<T>(
        Func<byte, byte[]> readRequest,
        Func<ReadOnlySpan<byte>, T> parse,
        T value,
        Func<T, byte, byte[]> writeRequest,
        Func<T, bool> matches,
        string label,
        CancellationToken cancellationToken)
    {
        var previous = parse(await QueryAsync(readRequest, $"read {label}", cancellationToken).ConfigureAwait(false));
        if (matches(previous))
        {
            return;
        }

        try
        {
            await QueryAsync(txn => writeRequest(value, txn), $"set {label}", cancellationToken)
                .ConfigureAwait(false);
            var actual = parse(
                await QueryAsync(readRequest, $"read back {label}", cancellationToken).ConfigureAwait(false));
            if (!matches(actual))
            {
                throw new InvalidOperationException($"{label} readback did not match the requested value.");
            }
        }
        catch
        {
            await TryRestoreAsync(txn => writeRequest(previous, txn), $"restore {label}").ConfigureAwait(false);
            throw;
        }
    }

    private async Task<byte[]> ReadEqBandsRequiredAsync(bool tenBand, CancellationToken cancellationToken) =>
        RazerHeadsetAudioProtocol.ReadEqBands(
            await QueryAsync(
                txn => RazerHeadsetAudioProtocol.GetEqBands(txn, tenBand),
                tenBand ? "read 10-band EQ" : "read 8-band EQ",
                cancellationToken).ConfigureAwait(false),
            tenBand);

    private async Task<(bool Ok, T Value)> ProbeAsync<T>(
        string label,
        Func<byte, byte[]> request,
        Func<ReadOnlySpan<byte>, T> parse,
        List<string> errors,
        CancellationToken cancellationToken)
    {
        try
        {
            return (true, parse(await QueryAsync(request, $"read {label}", cancellationToken).ConfigureAwait(false)));
        }
        catch (Exception exception) when (IsExpected(exception))
        {
            errors.Add($"{label}: {exception.Message}");
            return (false, default!);
        }
    }

    private async Task TryRestoreAsync(Func<byte, byte[]> request, string label)
    {
        try
        {
            await QueryAsync(request, label, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception exception) when (IsExpected(exception))
        {
            _diagnostic?.Invoke($"headset {label} failed: {exception.Message}");
        }
    }

    private async Task<byte[]> QueryAsync(
        Func<byte, byte[]> request,
        string label,
        CancellationToken cancellationToken)
    {
        var report = request(NextTransactionId());
        _diagnostic?.Invoke(
            $"headset {label}: size={report[6]}, class=0x{report[7]:X2}, command=0x{report[8]:X2}");
        return await _transport.QueryPreparedAsync(
            _devicePath, report, _deviceWait, cancellationToken).ConfigureAwait(false);
    }

    private byte NextTransactionId()
    {
        if (_transactionId >= 0x1F)
        {
            _transactionId = 0;
        }
        else
        {
            _transactionId++;
        }

        return _transactionId;
    }

    private static bool IsExpected(Exception exception) =>
        exception is IOException or InvalidDataException or InvalidOperationException or
            NotSupportedException or Win32Exception or UnauthorizedAccessException;
}
