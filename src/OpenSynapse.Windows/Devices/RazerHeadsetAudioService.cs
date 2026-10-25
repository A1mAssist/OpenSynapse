using System.ComponentModel;
using OpenSynapse.Core.Devices;
using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.Windows.Devices;

public enum RazerAudioSource : byte
{
    None = 0,
    Spdif = 1,
    Aux = 2,
    Bluetooth = 3,
    Usb = 4,
}

public sealed record RazerHeadsetAudioConnection(
    string DevicePath,
    ushort ProductId,
    string Name,
    byte ReportId,
    int InputReportLength);

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
    byte? AncState,
    byte? AncLevel,
    IReadOnlyList<string> Errors);

/// <summary>
/// Protocol 25 audio controls for the 18-device P1 profile (Nari, Kraken, Barracuda,
/// BlackShark V2 Pro, USB Audio Controller). Identity stays in the device row; this service
/// owns EQ, bass, microphone, audio source and noise-suppression commands.
/// </summary>
public sealed class RazerHeadsetAudioService : IAsyncDisposable
{
    private static readonly IReadOnlyDictionary<ushort, string> Products =
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
        };

    private readonly IRazerAudio25Transport _transport;
    private readonly Action<string>? _diagnostic;

    private RazerHeadsetAudioService(
        RazerHeadsetAudioConnection connection,
        IRazerAudio25Transport transport,
        Action<string>? diagnostic)
    {
        Connection = connection;
        _transport = transport;
        _diagnostic = diagnostic;
    }

    public RazerHeadsetAudioConnection Connection { get; }

    public static bool IsSupported(ushort productId) => Products.ContainsKey(productId);

    public static async Task<RazerHeadsetAudioConnection?> FindAsync(
        Action<string>? diagnostic = null,
        CancellationToken cancellationToken = default)
    {
        var interfaces = await WindowsHidDiscovery.FindVendorFeatureInterfacesAsync(
            0x1532,
            RazerAudio25Protocol.ReportLength,
            cancellationToken,
            item => diagnostic?.Invoke(
                $"audio25 interface pid={item.ProductId:X4} product='{item.ProductName}' " +
                $"usage=0x{item.UsagePage:X4}:0x{item.Usage:X4} feature={item.FeatureReportByteLength} " +
                $"out={item.OutputReportByteLength} access={item.Access}"))
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
                "audio25 found no admitted 65-byte headset interface; " +
                $"enumerated={string.Join(",", interfaces.Select(item => $"{item.ProductId:X4}:0x{item.UsagePage:X4}:0x{item.Usage:X4}"))}");
            return null;
        }

        var chosen = candidates[0];
        diagnostic?.Invoke(
            $"audio25 selected pid={chosen.ProductId:X4} product='{chosen.ProductName}' " +
            $"path={RazerFeatureTransport.DescribeDevicePath(chosen.DevicePath)} " +
            $"usage=0x{chosen.UsagePage:X4}:0x{chosen.Usage:X4} access={chosen.Access}");
        return new RazerHeadsetAudioConnection(
            chosen.DevicePath,
            chosen.ProductId,
            Products[chosen.ProductId],
            RazerAudio25Protocol.UsbReportId,
            chosen.InputReportByteLength);
    }

    public static async Task<RazerHeadsetAudioService> OpenAsync(
        RazerHeadsetAudioConnection connection,
        Action<string>? diagnostic = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection);
        var reportIds = connection.ReportId == RazerAudio25Protocol.UsbReportId
            ? new[] { RazerAudio25Protocol.UsbReportId, RazerAudio25Protocol.BleReportId }
            : new[] { connection.ReportId, RazerAudio25Protocol.UsbReportId };
        Exception? lastException = null;
        foreach (var reportId in reportIds.Distinct())
        {
            RazerAudio25Transport? transport = null;
            try
            {
                transport = new RazerAudio25Transport(
                    connection.DevicePath,
                    reportId,
                    TimeSpan.FromMilliseconds(10),
                    diagnostic);
                _ = await transport.SendAsync(RazerAudio25Protocol.GetFirmware(), cancellationToken)
                    .ConfigureAwait(false);
                return new RazerHeadsetAudioService(
                    connection with { ReportId = reportId },
                    transport,
                    diagnostic);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                lastException = exception;
                diagnostic?.Invoke($"audio25 report id 0x{reportId:X2} probe failed: {exception.Message}");
                if (transport is not null)
                {
                    await transport.DisposeAsync().ConfigureAwait(false);
                }
            }
        }

        throw new InvalidOperationException("The protocol 25 audio device did not accept either report id.", lastException);
    }

    internal RazerHeadsetAudioService(
        RazerHeadsetAudioConnection connection,
        IRazerAudio25Transport transport,
        Action<string>? diagnostic,
        bool _)
    {
        Connection = connection;
        _transport = transport ?? throw new ArgumentNullException(nameof(transport));
        _diagnostic = diagnostic;
    }

    public async Task<RazerHeadsetAudioState> ReadStateAsync(CancellationToken cancellationToken = default)
    {
        var errors = new List<string>();
        var firmware = await TryReadAsync(
            "firmware", RazerAudio25Protocol.GetFirmware(),
            data => RazerAudio25Protocol.ReadFirmware(data), errors, cancellationToken).ConfigureAwait(false);
        var battery = await TryReadAsync(
            "battery", RazerAudio25Protocol.GetBatteryLevel(),
            data => (byte?)RazerAudio25Protocol.ReadBatteryLevel(data), errors, cancellationToken).ConfigureAwait(false);
        var charging = await TryReadAsync(
            "charging", RazerAudio25Protocol.GetChargingStatus(),
            data => (bool?)RazerAudio25Protocol.ReadChargingStatus(data), errors, cancellationToken).ConfigureAwait(false);
        var preset = await TryReadAsync(
            "EQ preset", RazerAudio25Protocol.GetEqPreset(),
            data => (byte?)RazerAudio25Protocol.ReadEqPreset(data), errors, cancellationToken).ConfigureAwait(false);
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

        var bass = await TryReadAsync(
            "bass", RazerAudio25Protocol.GetBass(),
            data => (byte?)RazerAudio25Protocol.ReadBass(data), errors, cancellationToken).ConfigureAwait(false);
        var micMute = await TryReadAsync(
            "microphone mute", RazerAudio25Protocol.GetMicMute(),
            data => (bool?)RazerAudio25Protocol.ReadMicMute(data), errors, cancellationToken).ConfigureAwait(false);
        var micVolume = await TryReadAsync(
            "microphone volume", RazerAudio25Protocol.GetMicVolume(),
            data => (byte?)RazerAudio25Protocol.ReadMicVolume(data), errors, cancellationToken).ConfigureAwait(false);
        var source = await TryReadAsync(
            "audio source", RazerAudio25Protocol.GetAudioSource(),
            data => (RazerAudioSource?)RazerAudio25Protocol.ReadAudioSource(data), errors, cancellationToken).ConfigureAwait(false);
        var noise = await TryReadAsync(
            "noise suppression", RazerAudio25Protocol.GetNoiseSuppression(),
            data => (bool?)RazerAudio25Protocol.ReadNoiseSuppression(data), errors, cancellationToken).ConfigureAwait(false);
        var anc = await TryReadAsync(
            "ANC", RazerAudio25Protocol.GetAnc(),
            data => RazerAudio25Protocol.ReadAnc(data), errors, cancellationToken).ConfigureAwait(false);

        return new RazerHeadsetAudioState(
            firmware, battery, charging, preset, bands, bass, micMute, micVolume, source,
            noise, anc.State, anc.Level, errors);
    }

    public async Task SetEqPresetAsync(byte preset, CancellationToken cancellationToken = default)
    {
        var previous = RazerAudio25Protocol.ReadEqPreset(
            await QueryAsync(RazerAudio25Protocol.GetEqPreset(), "read EQ preset", cancellationToken).ConfigureAwait(false));
        if (previous == preset)
        {
            return;
        }

        try
        {
            await QueryAsync(RazerAudio25Protocol.SetEqPreset(preset), "set EQ preset", cancellationToken).ConfigureAwait(false);
            var actual = RazerAudio25Protocol.ReadEqPreset(
                await QueryAsync(RazerAudio25Protocol.GetEqPreset(), "read back EQ preset", cancellationToken).ConfigureAwait(false));
            if (actual != preset)
            {
                throw new InvalidOperationException($"EQ preset readback was 0x{actual:X2}, expected 0x{preset:X2}.");
            }
        }
        catch
        {
            await TryRestoreAsync(RazerAudio25Protocol.SetEqPreset(previous), "restore EQ preset").ConfigureAwait(false);
            throw;
        }
    }

    public async Task SetEqBandsAsync(
        IReadOnlyList<byte> bands,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(bands);
        var tenBand = bands.Count == 10;
        if (!tenBand && bands.Count != 8)
        {
            throw new ArgumentException("Equalizer data must contain either 10 or 8 bands.", nameof(bands));
        }

        var previous = await ReadEqBandsRequiredAsync(tenBand, cancellationToken).ConfigureAwait(false);
        if (previous.SequenceEqual(bands))
        {
            return;
        }

        try
        {
            await QueryAsync(
                RazerAudio25Protocol.SetEqBands(bands.ToArray(), tenBand: tenBand),
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
                RazerAudio25Protocol.SetEqBands(previous.ToArray(), tenBand: tenBand),
                "restore EQ bands").ConfigureAwait(false);
            throw;
        }
    }

    public async Task SetBassAsync(byte level, CancellationToken cancellationToken = default)
    {
        if (level > 100)
        {
            throw new ArgumentOutOfRangeException(nameof(level), "Bass level must be between 0 and 100.");
        }

        var previous = RazerAudio25Protocol.ReadBass(
            await QueryAsync(RazerAudio25Protocol.GetBass(), "read bass", cancellationToken).ConfigureAwait(false));
        if (previous == level)
        {
            return;
        }

        try
        {
            await QueryAsync(RazerAudio25Protocol.SetBass(level), "set bass", cancellationToken).ConfigureAwait(false);
            var actual = RazerAudio25Protocol.ReadBass(
                await QueryAsync(RazerAudio25Protocol.GetBass(), "read back bass", cancellationToken).ConfigureAwait(false));
            if (actual != level)
            {
                throw new InvalidOperationException($"Bass readback was {actual}, expected {level}.");
            }
        }
        catch
        {
            await TryRestoreAsync(RazerAudio25Protocol.SetBass(previous), "restore bass").ConfigureAwait(false);
            throw;
        }
    }

    public async Task SetMicMuteAsync(bool muted, CancellationToken cancellationToken = default)
    {
        var previous = RazerAudio25Protocol.ReadMicMute(
            await QueryAsync(RazerAudio25Protocol.GetMicMute(), "read microphone mute", cancellationToken).ConfigureAwait(false));
        if (previous == muted)
        {
            return;
        }

        try
        {
            await QueryAsync(RazerAudio25Protocol.SetMicMute(muted), "set microphone mute", cancellationToken).ConfigureAwait(false);
            var actual = RazerAudio25Protocol.ReadMicMute(
                await QueryAsync(RazerAudio25Protocol.GetMicMute(), "read back microphone mute", cancellationToken).ConfigureAwait(false));
            if (actual != muted)
            {
                throw new InvalidOperationException($"Microphone mute readback was {actual}, expected {muted}.");
            }
        }
        catch
        {
            await TryRestoreAsync(RazerAudio25Protocol.SetMicMute(previous), "restore microphone mute").ConfigureAwait(false);
            throw;
        }
    }

    public async Task SetMicVolumeAsync(byte volume, CancellationToken cancellationToken = default)
    {
        if (volume > 100)
        {
            throw new ArgumentOutOfRangeException(nameof(volume), "Microphone volume must be between 0 and 100.");
        }

        var previous = RazerAudio25Protocol.ReadMicVolume(
            await QueryAsync(RazerAudio25Protocol.GetMicVolume(), "read microphone volume", cancellationToken).ConfigureAwait(false));
        if (previous == volume)
        {
            return;
        }

        try
        {
            await QueryAsync(RazerAudio25Protocol.SetMicVolume(volume), "set microphone volume", cancellationToken).ConfigureAwait(false);
            var actual = RazerAudio25Protocol.ReadMicVolume(
                await QueryAsync(RazerAudio25Protocol.GetMicVolume(), "read back microphone volume", cancellationToken).ConfigureAwait(false));
            if (actual != volume)
            {
                throw new InvalidOperationException($"Microphone volume readback was {actual}, expected {volume}.");
            }
        }
        catch
        {
            await TryRestoreAsync(RazerAudio25Protocol.SetMicVolume(previous), "restore microphone volume").ConfigureAwait(false);
            throw;
        }
    }

    public async Task SetAudioSourceAsync(RazerAudioSource source, CancellationToken cancellationToken = default)
    {
        if (!Enum.IsDefined(source))
        {
            throw new ArgumentOutOfRangeException(nameof(source));
        }

        var previousValue = RazerAudio25Protocol.ReadAudioSource(
            await QueryAsync(RazerAudio25Protocol.GetAudioSource(), "read audio source", cancellationToken).ConfigureAwait(false));
        if (previousValue == (byte)source)
        {
            return;
        }

        try
        {
            await QueryAsync(RazerAudio25Protocol.SetAudioSource((byte)source), "set audio source", cancellationToken).ConfigureAwait(false);
            var actual = RazerAudio25Protocol.ReadAudioSource(
                await QueryAsync(RazerAudio25Protocol.GetAudioSource(), "read back audio source", cancellationToken).ConfigureAwait(false));
            if (actual != (byte)source)
            {
                throw new InvalidOperationException($"Audio source readback was {actual}, expected {(byte)source}.");
            }
        }
        catch
        {
            await TryRestoreAsync(RazerAudio25Protocol.SetAudioSource(previousValue), "restore audio source").ConfigureAwait(false);
            throw;
        }
    }

    public async Task SetNoiseSuppressionAsync(bool enabled, CancellationToken cancellationToken = default)
    {
        var previous = RazerAudio25Protocol.ReadNoiseSuppression(
            await QueryAsync(RazerAudio25Protocol.GetNoiseSuppression(), "read noise suppression", cancellationToken).ConfigureAwait(false));
        if (previous == enabled)
        {
            return;
        }

        try
        {
            await QueryAsync(RazerAudio25Protocol.SetNoiseSuppression(enabled), "set noise suppression", cancellationToken).ConfigureAwait(false);
            var actual = RazerAudio25Protocol.ReadNoiseSuppression(
                await QueryAsync(RazerAudio25Protocol.GetNoiseSuppression(), "read back noise suppression", cancellationToken).ConfigureAwait(false));
            if (actual != enabled)
            {
                throw new InvalidOperationException($"Noise suppression readback was {actual}, expected {enabled}.");
            }
        }
        catch
        {
            await TryRestoreAsync(RazerAudio25Protocol.SetNoiseSuppression(previous), "restore noise suppression").ConfigureAwait(false);
            throw;
        }
    }

    private async Task<IReadOnlyList<byte>> ReadEqBandsRequiredAsync(bool tenBand, CancellationToken cancellationToken)
    {
        var data = await QueryAsync(RazerAudio25Protocol.GetEqBands(tenBand: tenBand), "read EQ bands", cancellationToken).ConfigureAwait(false);
        return RazerAudio25Protocol.ReadEqBands(data, tenBand);
    }

    private async Task<byte[]> QueryAsync(RazerAudio25Command command, string description, CancellationToken cancellationToken)
    {
        var response = await _transport.SendAsync(command, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccess)
        {
            throw new InvalidOperationException($"{description} failed with status 0x{(byte)response.Status:X2}.");
        }
        return response.Data;
    }

    private async Task<T?> TryReadAsync<T>(
        string description,
        RazerAudio25Command command,
        Func<ReadOnlySpan<byte>, T> parser,
        List<string> errors,
        CancellationToken cancellationToken)
    {
        try
        {
            var data = await QueryAsync(command, description, cancellationToken).ConfigureAwait(false);
            return parser(data);
        }
        catch (Exception exception) when (IsExpected(exception))
        {
            errors.Add($"{description}: {exception.Message}");
            _diagnostic?.Invoke($"audio25 {description} failed: {exception.Message}");
            return default;
        }
    }

    private async Task TryRestoreAsync(RazerAudio25Command command, string description)
    {
        try
        {
            await QueryAsync(command, description, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception exception) when (IsExpected(exception))
        {
            _diagnostic?.Invoke($"audio25 {description} failed: {exception.Message}");
        }
    }

    private static bool IsExpected(Exception exception) =>
        exception is Win32Exception or IOException or InvalidDataException or InvalidOperationException or
            NotSupportedException or ArgumentException;

    public ValueTask DisposeAsync() => _transport.DisposeAsync();
}
