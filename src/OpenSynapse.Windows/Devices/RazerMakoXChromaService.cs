using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.Windows.Devices;

/// <summary>
/// Mako X lighting over the rzDeviceJolie class 0x0F commands on the same 64-byte
/// feature report as its audio page. Ten LEDs are exposed as one region. The
/// firmware only implements Off, Static, Breathing, Spectrum, Wave and the
/// per-LED CustomFrame mode; probes of 0x00-0x7F answer status 0x05 (and blank
/// the strip) for every other id, so Starlight/Fire/Audio Meter are streamed as
/// frames by the app instead of being sent as firmware effects.
/// </summary>
internal sealed class RazerMakoXChromaService : IRazerHeadsetLightingService, IAsyncDisposable
{
    internal const int LedCount = 10;
    private readonly string _devicePath;
    private readonly Action<string>? _diagnostic;
    private readonly bool _keepOpen;
    private RazerValkyrieT3Transport? _transport;

    internal RazerMakoXChromaService(
        string devicePath,
        Action<string>? diagnostic = null,
        bool keepOpen = false)
    {
        _devicePath = devicePath;
        _diagnostic = diagnostic;
        _keepOpen = keepOpen;
    }

    public async Task<RazerChromaStandardState> ReadStateAsync(
        CancellationToken cancellationToken = default)
    {
        byte? brightness = null;
        RazerChromaEffect? effect = null;
        var colors = new List<byte>();
        var errors = new List<string>();

        try
        {
            var value = await QueryAsync(
                Create(0x84, [0x00, 0x00, 0x00]), [0x00, 0x00], "read brightness", cancellationToken)
                .ConfigureAwait(false);
            brightness = (byte)Math.Ceiling(value[0] / 255d * 100);
        }
        catch (Exception exception) when (IsExpected(exception))
        {
            errors.Add(exception.Message);
        }

        try
        {
            var value = await QueryAsync(
                Create(0x82, [0x00, 0x00]), [0x00, 0x00], "read effect", cancellationToken)
                .ConfigureAwait(false);
            _diagnostic?.Invoke(
                $"mako-x lighting effect readback: {Convert.ToHexString(value, 0, Math.Min(value.Length, 12))}");
            if (value.Length >= 4 && FromFirmwareEffectId(value[0]) is { } parsed)
            {
                effect = parsed;
                var count = Math.Min(value[3], (value.Length - 4) / 3);
                for (var index = 0; index < count * 3; index++) colors.Add(value[4 + index]);
            }
        }
        catch (Exception exception) when (IsExpected(exception))
        {
            errors.Add(exception.Message);
        }

        return new RazerChromaStandardState(
            brightness, effect, colors, null, null, null, null, errors);
    }

    public Task SetBrightnessAsync(byte percent, CancellationToken cancellationToken = default)
    {
        var scaled = (byte)(Math.Min(percent, (byte)100) * 255 / 100);
        return SendAsync(Create(0x04, [0x00, 0x00, scaled]), "set brightness", cancellationToken);
    }

    public Task SetEffectAsync(
        RazerChromaEffect effect,
        IReadOnlyList<byte> colors,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(colors);
        if (!Enum.IsDefined(effect)) throw new ArgumentOutOfRangeException(nameof(effect));
        if (!IsFirmwareEffect(effect))
        {
            // 1532:05AD answers status 0x05 and blanks the strip for every id
            // outside its six firmware effects, so an unsupported id must never
            // reach the wire. Starlight/Fire/Audio Meter are rendered as frames.
            throw new ArgumentOutOfRangeException(
                nameof(effect),
                effect,
                "The Mako X firmware only has Off, Static, Breathing, Spectrum, Wave and CustomFrame.");
        }

        if (colors.Count % 3 != 0) throw new ArgumentException("Lighting colors must be RGB triples.", nameof(colors));

        var colorCount = colors.Count / 3;
        var flags = effect switch
        {
            RazerChromaEffect.Breathing => (byte)(colorCount > 1 ? 2 : 1),
            RazerChromaEffect.Wave => (byte)1,
            _ => (byte)0,
        };
        var rate = effect == RazerChromaEffect.Wave ? (byte)40 : (byte)0;
        return SetEffectAsync(effect, colors, flags, rate, cancellationToken);
    }

    internal Task SetEffectAsync(
        RazerChromaEffect effect,
        IReadOnlyList<byte> colors,
        byte flags,
        byte rate,
        CancellationToken cancellationToken = default)
    {
        var payload = CreateEffectPayload(effect, colors, flags, rate);
        return SendAsync(Create(0x02, payload), "set effect", cancellationToken);
    }

    internal Task SetCustomFrameEffectAsync(CancellationToken cancellationToken = default) =>
        SetEffectAsync(RazerChromaEffect.CustomFrame, [], cancellationToken);

    internal Task SetFrameAsync(IReadOnlyList<byte> colors, CancellationToken cancellationToken = default)
    {
        var payload = CreateFramePayload(colors);
        // A 25 fps effect pump would otherwise write one log line per frame, and
        // a missed ACK must not stop the pump, so frames are fire-and-forget.
        return SendAsync(
            Create(0x03, payload), "set frame", cancellationToken, log: false, waitForAck: false);
    }

    internal static byte[] CreateEffectPayload(
        RazerChromaEffect effect,
        IReadOnlyList<byte> colors,
        byte flags,
        byte rate)
    {
        ArgumentNullException.ThrowIfNull(colors);
        if (!Enum.IsDefined(effect)) throw new ArgumentOutOfRangeException(nameof(effect));
        if (colors.Count % 3 != 0) throw new ArgumentException("Lighting colors must be RGB triples.", nameof(colors));
        if (colors.Count / 3 > byte.MaxValue)
            throw new ArgumentException("A Chroma effect carries at most 255 colors.", nameof(colors));

        var payload = new byte[6 + colors.Count];
        payload[2] = ToFirmwareEffectId(effect);
        payload[3] = flags;
        payload[4] = rate;
        payload[5] = (byte)(colors.Count / 3);
        for (var index = 0; index < colors.Count; index++) payload[6 + index] = colors[index];
        return payload;
    }

    internal static bool IsFirmwareEffect(RazerChromaEffect effect) => effect is
        RazerChromaEffect.Off or RazerChromaEffect.Static or RazerChromaEffect.Breathing or
        RazerChromaEffect.Spectrum or RazerChromaEffect.Wave or RazerChromaEffect.CustomFrame;

    internal static byte ToFirmwareEffectId(RazerChromaEffect effect) => effect switch
    {
        // 1532:05AD accepts 0/1/2/3/4/8 on class 0x0F command 0x02 and answers
        // success for each. Off/Static/Breathing/Spectrum/Wave keep the shared
        // middleware numbering; 8 selects the per-LED frame mode.
        RazerChromaEffect.Off or RazerChromaEffect.Static or RazerChromaEffect.Breathing or
            RazerChromaEffect.Spectrum or RazerChromaEffect.Wave or RazerChromaEffect.CustomFrame =>
            (byte)effect,
        _ => throw new ArgumentOutOfRangeException(
            nameof(effect),
            effect,
            "The Mako X firmware has no id for this effect."),
    };

    internal static RazerChromaEffect? FromFirmwareEffectId(byte effect) => effect switch
    {
        0x00 => RazerChromaEffect.Off,
        0x01 => RazerChromaEffect.Static,
        0x02 => RazerChromaEffect.Breathing,
        0x03 => RazerChromaEffect.Spectrum,
        0x04 => RazerChromaEffect.Wave,
        0x08 => RazerChromaEffect.CustomFrame,
        _ => null,
    };

    internal static byte[] CreateFramePayload(IReadOnlyList<byte> colors)
    {
        ArgumentNullException.ThrowIfNull(colors);
        if (colors.Count != LedCount * 3)
            throw new ArgumentException($"A Mako X frame carries {LedCount} RGB triples.", nameof(colors));

        var payload = new byte[5 + colors.Count];
        payload[4] = LedCount - 1;
        for (var index = 0; index < colors.Count; index++) payload[5 + index] = colors[index];
        return payload;
    }

    private static byte[] Create(byte command, ReadOnlySpan<byte> payload) =>
        RazerValkyrieT3Protocol.Create(0x0F, command, payload);

    private RazerValkyrieT3Transport GetTransport() =>
        _keepOpen
            ? _transport ??= new RazerValkyrieT3Transport(_devicePath, 0x60)
            : new RazerValkyrieT3Transport(_devicePath, 0x60);

    private async Task SendAsync(
        byte[] report,
        string label,
        CancellationToken cancellationToken,
        bool log = true,
        bool waitForAck = true)
    {
        var transport = GetTransport();
        try
        {
            if (log)
            {
                var payloadLength = Math.Min(report[6], (byte)12);
                var payload = payloadLength > 0
                    ? $" payload={Convert.ToHexString(report, 9, payloadLength)}"
                    : string.Empty;
                _diagnostic?.Invoke(
                    $"mako-x lighting {label}: class=0x{report[7]:X2} id=0x{report[8]:X2} size=0x{report[6]:X2}{payload}");
            }

            if (waitForAck)
            {
                await transport.SendAsync(report, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                await transport.SendOnlyAsync(report, cancellationToken).ConfigureAwait(false);
            }
        }
        catch
        {
            if (_keepOpen)
            {
                _transport = null;
                await transport.DisposeAsync().ConfigureAwait(false);
            }
            throw;
        }
        finally
        {
            if (!_keepOpen) await transport.DisposeAsync().ConfigureAwait(false);
        }
    }

    private async Task<byte[]> QueryAsync(
        byte[] report,
        byte[] header,
        string label,
        CancellationToken cancellationToken)
    {
        var transport = GetTransport();
        try
        {
            _diagnostic?.Invoke($"mako-x lighting {label}: class=0x{report[7]:X2} id=0x{report[8]:X2}");
            return await transport.QueryAsync(report, header, cancellationToken).ConfigureAwait(false) ??
                throw new InvalidOperationException($"Mako X {label} returned no matching response.");
        }
        catch
        {
            if (_keepOpen)
            {
                _transport = null;
                await transport.DisposeAsync().ConfigureAwait(false);
            }
            throw;
        }
        finally
        {
            if (!_keepOpen) await transport.DisposeAsync().ConfigureAwait(false);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_transport is { } transport)
        {
            _transport = null;
            await transport.DisposeAsync().ConfigureAwait(false);
        }
    }

    private static bool IsExpected(Exception exception) =>
        exception is IOException or InvalidOperationException or TimeoutException or
            UnauthorizedAccessException or System.ComponentModel.Win32Exception;
}
