using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.Windows.Devices;

internal readonly record struct RazerMakoXPowerState(bool Enabled, int Minutes);

/// <summary>
/// Mako X auto-sleep over rzDeviceJolie class 0x07. The bundle calls these
/// commands "Silence Detection Control"; its UI exposes a 15-60 minute window.
/// </summary>
internal sealed class RazerMakoXPowerService
{
    private readonly string _devicePath;
    private readonly Action<string>? _diagnostic;

    internal RazerMakoXPowerService(string devicePath, Action<string>? diagnostic = null)
    {
        _devicePath = devicePath;
        _diagnostic = diagnostic;
    }

    internal async Task<RazerMakoXPowerState> ReadAsync(CancellationToken cancellationToken = default)
    {
        var enabled = await QueryAsync(
            Create(0x88, [0x00]), [0x00], "read auto sleep", cancellationToken).ConfigureAwait(false);
        var duration = await QueryAsync(
            Create(0x83, [0x00, 0x00]), [], "read sleep time", cancellationToken).ConfigureAwait(false);

        return ParseState(enabled, duration);
    }

    internal async Task SetAsync(
        bool enabled,
        int minutes,
        CancellationToken cancellationToken = default)
    {
        await SendAsync(
            CreateSleepTime(minutes),
            "set sleep time",
            cancellationToken).ConfigureAwait(false);
        await SendAsync(
            CreateAutoSleep(enabled),
            "set auto sleep",
            cancellationToken).ConfigureAwait(false);
    }

    internal static byte[] CreateAutoSleep(bool enabled) =>
        Create(0x08, [0x00, (byte)(enabled ? 1 : 0)]);

    internal static byte[] CreateSleepTime(int minutes)
    {
        var seconds = (ushort)Math.Clamp(minutes * 60, 0, ushort.MaxValue);
        return Create(0x03, [(byte)(seconds >> 8), (byte)(seconds & 0xFF)]);
    }

    internal static RazerMakoXPowerState ParseState(
        ReadOnlySpan<byte> enabled,
        ReadOnlySpan<byte> duration)
    {
        if (enabled.Length < 1 || duration.Length < 2)
            throw new InvalidDataException("Mako X auto-sleep returned a short response.");

        var seconds = (duration[0] << 8) | duration[1];
        return new RazerMakoXPowerState(
            enabled[0] != 0,
            Math.Clamp((int)Math.Round(seconds / 60d), 0, 60));
    }

    private static byte[] Create(byte command, ReadOnlySpan<byte> payload) =>
        RazerValkyrieT3Protocol.Create(0x07, command, payload);

    private async Task SendAsync(byte[] report, string label, CancellationToken cancellationToken)
    {
        await using var transport = new RazerValkyrieT3Transport(_devicePath, 0x60);
        _diagnostic?.Invoke(
            $"mako-x power {label}: class=0x{report[7]:X2} id=0x{report[8]:X2}");
        await transport.SendAsync(report, cancellationToken).ConfigureAwait(false);
    }

    private async Task<byte[]> QueryAsync(
        byte[] report,
        byte[] header,
        string label,
        CancellationToken cancellationToken)
    {
        await using var transport = new RazerValkyrieT3Transport(_devicePath, 0x60);
        _diagnostic?.Invoke(
            $"mako-x power {label}: class=0x{report[7]:X2} id=0x{report[8]:X2}");
        return await transport.QueryAsync(report, header, cancellationToken).ConfigureAwait(false) ??
            throw new InvalidOperationException($"Mako X {label} returned no matching response.");
    }
}
