using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.Windows.Devices;

/// <summary>
/// Mako X lighting over the rzDeviceJolie class 0x0F commands on the same 65-byte
/// feature report as its audio page. Ten LEDs are exposed as one region.
/// </summary>
internal sealed class RazerMakoXChromaService : IRazerHeadsetLightingService
{
    private readonly string _devicePath;
    private readonly Action<string>? _diagnostic;

    internal RazerMakoXChromaService(string devicePath, Action<string>? diagnostic = null)
    {
        _devicePath = devicePath;
        _diagnostic = diagnostic;
    }

    public async Task<RazerChromaStandardState> ReadStateAsync(CancellationToken cancellationToken = default)
    {
        var value = await QueryAsync(
            Create(0x84, [0x00, 0x00, 0x00]), [0x00, 0x00], "read brightness", cancellationToken).ConfigureAwait(false);
        var brightness = (byte)Math.Ceiling(value[0] / 255d * 100);
        return new RazerChromaStandardState(
            brightness, null, [], null, null, null, null, []);
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
        if (colors.Count % 3 != 0) throw new ArgumentException("Lighting colors must be RGB triples.", nameof(colors));

        var payload = new byte[6 + colors.Count];
        payload[2] = (byte)effect;
        payload[5] = (byte)(colors.Count / 3);
        for (var index = 0; index < colors.Count; index++) payload[6 + index] = colors[index];
        return SendAsync(Create(0x02, payload), "set effect", cancellationToken);
    }

    private static byte[] Create(byte command, ReadOnlySpan<byte> payload) =>
        RazerValkyrieT3Protocol.CreateMakoX(0x0F, command, payload);

    private async Task SendAsync(byte[] report, string label, CancellationToken cancellationToken)
    {
        await using var transport = new RazerValkyrieT3Transport(_devicePath, makoX: true);
        _diagnostic?.Invoke($"mako-x lighting {label}: class=0x{report[7]:X2} id=0x{report[8]:X2} size=0x{report[6]:X2}");
        await transport.SendAsync(report, cancellationToken).ConfigureAwait(false);
    }

    private async Task<byte[]> QueryAsync(
        byte[] report,
        byte[] header,
        string label,
        CancellationToken cancellationToken)
    {
        await using var transport = new RazerValkyrieT3Transport(_devicePath, makoX: true);
        _diagnostic?.Invoke($"mako-x lighting {label}: class=0x{report[7]:X2} id=0x{report[8]:X2}");
        return await transport.QueryAsync(report, header, cancellationToken).ConfigureAwait(false) ??
            throw new InvalidOperationException($"Mako X {label} returned no matching response.");
    }
}
