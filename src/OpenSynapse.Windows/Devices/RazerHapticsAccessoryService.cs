using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.Windows.Devices;

internal sealed class RazerHapticsAccessoryService : IAsyncDisposable
{
    private readonly RazerAccessoryCommandClient _client;

    internal RazerHapticsAccessoryService(RazerAccessoryCommandClient client) => _client = client;

    internal ushort ProductId => _client.ProductId;

    internal static async Task<RazerHapticsAccessoryService?> ConnectAsync(
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
            : new RazerHapticsAccessoryService(
                new RazerAccessoryCommandClient(connection, diagnostic));
    }

    internal async Task<RazerHapticsState> ReadStateAsync(CancellationToken cancellationToken = default)
    {
        var errors = new List<string>();
        var mode = await TryReadAsync(
            "mode",
            RazerHapticsAccessoryProtocol.CreateGetMode(),
            RazerHapticsAccessoryProtocol.ReadMode,
            errors,
            cancellationToken).ConfigureAwait(false);
        var (enabled, intensity) = await TryReadAsync(
            "intensity",
            RazerHapticsAccessoryProtocol.CreateGetIntensity(),
            RazerHapticsAccessoryProtocol.ReadIntensity,
            errors,
            cancellationToken).ConfigureAwait(false);
        var gain = await TryReadAsync(
            "gain",
            RazerHapticsAccessoryProtocol.CreateGetScalar(RazerHapticsAccessoryProtocol.GetGain),
            response => RazerHapticsAccessoryProtocol.ReadBigEndianScalar(response),
            errors,
            cancellationToken).ConfigureAwait(false);
        var perception = await TryReadAsync(
            "perception",
            RazerHapticsAccessoryProtocol.CreateGetScalar(RazerHapticsAccessoryProtocol.GetPerception),
            response => RazerHapticsAccessoryProtocol.ReadBigEndianScalar(response),
            errors,
            cancellationToken).ConfigureAwait(false);
        var transient = await TryReadAsync(
            "transient",
            RazerHapticsAccessoryProtocol.CreateGetScalar(RazerHapticsAccessoryProtocol.GetTransient),
            response => RazerHapticsAccessoryProtocol.ReadBigEndianScalar(response),
            errors,
            cancellationToken).ConfigureAwait(false);
        var signal = await TryReadAsync(
            "signal",
            RazerHapticsAccessoryProtocol.CreateGetScalar(RazerHapticsAccessoryProtocol.GetSignalGain),
            response => RazerHapticsAccessoryProtocol.ReadBigEndianScalar(response),
            errors,
            cancellationToken).ConfigureAwait(false);
        return RazerHapticsAccessoryProtocol.ParseState(
            mode,
            enabled,
            intensity,
            gain,
            perception,
            transient,
            signal,
            errors.Count == 0 ? null : string.Join("; ", errors));
    }

    internal Task SetModeAsync(int mode, CancellationToken cancellationToken = default) =>
        SendAsync(
            RazerHapticsAccessoryProtocol.CreateSetMode(mode),
            cancellationToken);

    internal Task SetIntensityAsync(
        bool enabled,
        int intensity,
        CancellationToken cancellationToken = default) =>
        SendAsync(
            RazerHapticsAccessoryProtocol.CreateSetIntensity(enabled, intensity),
            cancellationToken);

    internal Task SetGainAsync(int value, CancellationToken cancellationToken = default) =>
        SetScalarAsync(
            RazerHapticsAccessoryProtocol.CreateSetScalar(RazerHapticsAccessoryProtocol.SetGain, value),
            cancellationToken);

    internal Task SetPerceptionAsync(int value, CancellationToken cancellationToken = default) =>
        SetScalarAsync(
            RazerHapticsAccessoryProtocol.CreateSetScalar(RazerHapticsAccessoryProtocol.SetPerception, value),
            cancellationToken);

    internal Task SetTransientAsync(int value, CancellationToken cancellationToken = default) =>
        SetScalarAsync(
            RazerHapticsAccessoryProtocol.CreateSetScalar(RazerHapticsAccessoryProtocol.SetTransient, value),
            cancellationToken);

    internal Task SetSignalGainAsync(int value, CancellationToken cancellationToken = default) =>
        SetScalarAsync(
            RazerHapticsAccessoryProtocol.CreateSetScalar(RazerHapticsAccessoryProtocol.SetSignalGain, value),
            cancellationToken);

    private async Task SetScalarAsync(byte[] request, CancellationToken cancellationToken)
    {
        await SendAsync(request, cancellationToken).ConfigureAwait(false);
        var response = await _client.QueryAsync(3, request[7], (byte)(request[8] | 0x80),
            request.AsMemory(RazerAccessoryProtocol.ArgumentsOffset, 3),
            candidate => RazerAccessoryProtocol.MatchesCommand(candidate, request[7], (byte)(request[8] | 0x80)),
            cancellationToken).ConfigureAwait(false);
        if (RazerHapticsAccessoryProtocol.ReadBigEndianScalar(response) !=
            RazerHapticsAccessoryProtocol.ReadBigEndianScalar(request))
            throw new InvalidOperationException("Sensa scalar readback did not match the requested value.");
    }

    private Task SendAsync(byte[] request, CancellationToken cancellationToken) =>
        _client.QueryAsync(
            request[6],
            request[7],
            request[8],
            request.AsMemory(RazerAccessoryProtocol.ArgumentsOffset, request[6]),
            response => RazerAccessoryProtocol.MatchesCommand(response, request[7], request[8]),
            cancellationToken);

    private async Task<T> TryReadAsync<T>(
        string label,
        byte[] request,
        Func<byte[], T> parse,
        List<string> errors,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await _client.QueryAsync(
                request[6],
                request[7],
                request[8],
                request.AsMemory(RazerAccessoryProtocol.ArgumentsOffset, request[6]),
                candidate => RazerAccessoryProtocol.MatchesCommand(candidate, request[7], request[8]),
                cancellationToken).ConfigureAwait(false);
            return parse(response);
        }
        catch (Exception exception) when (IsExpected(exception))
        {
            errors.Add($"{label}: {exception.Message}");
            return default!;
        }
    }

    public ValueTask DisposeAsync() => _client.DisposeAsync();

    private static bool IsExpected(Exception exception) =>
        exception is IOException or InvalidDataException or InvalidOperationException or
            NotSupportedException or TimeoutException or UnauthorizedAccessException or
            System.ComponentModel.Win32Exception or ArgumentException;
}
