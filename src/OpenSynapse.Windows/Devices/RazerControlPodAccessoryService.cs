using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.Windows.Devices;

internal sealed class RazerControlPodAccessoryService : IAsyncDisposable
{
    private readonly RazerAccessoryCommandClient _client;

    internal RazerControlPodAccessoryService(RazerAccessoryCommandClient client) => _client = client;

    internal ushort ProductId => _client.ProductId;

    internal static async Task<RazerControlPodAccessoryService?> ConnectAsync(
        ushort productId,
        Action<string>? diagnostic = null,
        CancellationToken cancellationToken = default)
    {
        var connection = await RazerAccessoryDiscovery.FindAsync(
            productId,
            91,
            diagnostic,
            cancellationToken).ConfigureAwait(false);
        return connection is null
            ? null
            : new RazerControlPodAccessoryService(
                new RazerAccessoryCommandClient(connection, diagnostic));
    }

    internal async Task<RazerControlPodState> ReadStateAsync(
        CancellationToken cancellationToken = default)
    {
        var errors = new List<string>();
        var brightness = await TryReadAsync(
            "brightness",
            RazerControlPodAccessoryProtocol.GetBrightness,
            errors,
            cancellationToken).ConfigureAwait(false);
        var direction = await TryReadAsync(
            "roller direction",
            RazerControlPodAccessoryProtocol.GetRollerDirection,
            errors,
            cancellationToken).ConfigureAwait(false);
        var lowBattery = await TryReadAsync(
            "low battery warning",
            RazerControlPodAccessoryProtocol.GetLowBattery,
            errors,
            cancellationToken).ConfigureAwait(false);

        return new RazerControlPodState(
            brightness,
            direction,
            lowBattery,
            errors.Count == 0 ? null : string.Join("; ", errors));
    }

    internal Task SetBrightnessAsync(int value, CancellationToken cancellationToken = default) =>
        WriteAsync(
            RazerControlPodAccessoryProtocol.SetBrightness,
            RazerControlPodAccessoryProtocol.GetBrightness,
            value,
            cancellationToken);

    internal Task SetRollerDirectionAsync(
        int value,
        CancellationToken cancellationToken = default) =>
        WriteAsync(
            RazerControlPodAccessoryProtocol.SetRollerDirection,
            RazerControlPodAccessoryProtocol.GetRollerDirection,
            value,
            cancellationToken);

    internal Task SetLowBatteryPercentAsync(
        int value,
        CancellationToken cancellationToken = default) =>
        WriteAsync(
            RazerControlPodAccessoryProtocol.SetLowBattery,
            RazerControlPodAccessoryProtocol.GetLowBattery,
            value,
            cancellationToken);

    public ValueTask DisposeAsync() => _client.DisposeAsync();

    private async Task WriteAsync(
        byte setCommand,
        byte getCommand,
        int value,
        CancellationToken cancellationToken)
    {
        await QueryAsync(
            setCommand,
            1,
            new byte[] { checked((byte)value) },
            cancellationToken).ConfigureAwait(false);
        var readback = await QueryAsync(
            getCommand,
            0,
            ReadOnlyMemory<byte>.Empty,
            cancellationToken).ConfigureAwait(false);
        var actual = RazerControlPodAccessoryProtocol.ReadByte(readback);
        if (Math.Abs(actual - value) > 1)
        {
            throw new InvalidOperationException(
                $"Control Pod readback was {actual}, expected {value}.");
        }
    }

    private async Task<int?> TryReadAsync(
        string label,
        byte commandId,
        List<string> errors,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await QueryAsync(
                commandId, 0, ReadOnlyMemory<byte>.Empty, cancellationToken)
                .ConfigureAwait(false);
            return RazerControlPodAccessoryProtocol.ReadByte(response);
        }
        catch (Exception exception) when (IsExpected(exception))
        {
            errors.Add($"{label}: {exception.Message}");
            return null;
        }
    }

    private Task<byte[]> QueryAsync(
        byte commandId,
        byte dataSize,
        ReadOnlyMemory<byte> arguments,
        CancellationToken cancellationToken) =>
        _client.QueryAsync(
            dataSize,
            RazerControlPodAccessoryProtocol.CommandClass,
            commandId,
            arguments,
            response => RazerAccessoryProtocol.MatchesCommand(
                response,
                RazerControlPodAccessoryProtocol.CommandClass,
                commandId),
            cancellationToken);

    private static bool IsExpected(Exception exception) =>
        exception is IOException or InvalidDataException or InvalidOperationException or
            NotSupportedException or TimeoutException or UnauthorizedAccessException or
            System.ComponentModel.Win32Exception or ArgumentException;
}
