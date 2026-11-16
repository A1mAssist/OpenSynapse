using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.Windows.Devices;

internal sealed class RazerDockAccessoryService : IAsyncDisposable
{
    private readonly RazerAccessoryCommandClient _client;
    private const byte ProfileId = 0;
    private const byte LedId = 0;
    private int _transactionId = 0x1F;

    internal RazerDockAccessoryService(RazerAccessoryCommandClient client) => _client = client;

    internal ushort ProductId => _client.ProductId;

    internal static async Task<RazerDockAccessoryService?> ConnectAsync(
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
            : new RazerDockAccessoryService(
                new RazerAccessoryCommandClient(connection, diagnostic));
    }

    internal async Task<RazerDockState> ReadStateAsync(CancellationToken cancellationToken = default)
    {
        var errors = new List<string>();
        var hasDockSync = false;
        var hasChargingEffect = false;
        var dockSync = false;
        var chargingEffect = false;
        try
        {
            var response = await QueryAsync(
                RazerDockAccessoryProtocol.CreateGetDockSync(
                    ProfileId, LedId, NextTransaction()),
                RazerDockAccessoryProtocol.GetDockSync,
                ProfileId,
                LedId,
                cancellationToken).ConfigureAwait(false);
            dockSync = RazerDockAccessoryProtocol.ReadEnabled(response);
            hasDockSync = true;
        }
        catch (Exception exception) when (IsExpected(exception))
        {
            errors.Add($"dock sync: {exception.Message}");
        }

        try
        {
            var response = await QueryAsync(
                RazerDockAccessoryProtocol.CreateGetChargingEffect(
                    ProfileId, NextTransaction()),
                RazerDockAccessoryProtocol.GetChargingEffect,
                ProfileId,
                0,
                cancellationToken).ConfigureAwait(false);
            chargingEffect = RazerDockAccessoryProtocol.ReadEnabled(response);
            hasChargingEffect = true;
        }
        catch (Exception exception) when (IsExpected(exception))
        {
            errors.Add($"charging effect: {exception.Message}");
        }

        return new RazerDockState(
            dockSync,
            chargingEffect,
            hasDockSync,
            hasChargingEffect,
            errors.Count == 0 ? null : string.Join("; ", errors));
    }

    internal async Task SetDockSyncAsync(bool enabled, CancellationToken cancellationToken = default)
    {
        await QueryAsync(
            RazerDockAccessoryProtocol.CreateSetDockSync(
                ProfileId, LedId, enabled, NextTransaction()),
            RazerDockAccessoryProtocol.SetDockSync,
            ProfileId,
            LedId,
            cancellationToken).ConfigureAwait(false);
        var response = await QueryAsync(
            RazerDockAccessoryProtocol.CreateGetDockSync(
                ProfileId, LedId, NextTransaction()),
            RazerDockAccessoryProtocol.GetDockSync,
            ProfileId,
            LedId,
            cancellationToken).ConfigureAwait(false);
        if (RazerDockAccessoryProtocol.ReadEnabled(response) != enabled)
        {
            throw new InvalidOperationException("Dock sync readback did not match.");
        }
    }

    internal async Task SetChargingEffectAsync(
        bool enabled,
        CancellationToken cancellationToken = default)
    {
        await QueryAsync(
            RazerDockAccessoryProtocol.CreateSetChargingEffect(
                ProfileId, enabled, NextTransaction()),
            RazerDockAccessoryProtocol.SetChargingEffect,
            ProfileId,
            0,
            cancellationToken).ConfigureAwait(false);
        var response = await QueryAsync(
            RazerDockAccessoryProtocol.CreateGetChargingEffect(
                ProfileId, NextTransaction()),
            RazerDockAccessoryProtocol.GetChargingEffect,
            ProfileId,
            0,
            cancellationToken).ConfigureAwait(false);
        if (RazerDockAccessoryProtocol.ReadEnabled(response) != enabled)
        {
            throw new InvalidOperationException("Dock charging-effect readback did not match.");
        }
    }

    public ValueTask DisposeAsync() => _client.DisposeAsync();

    private byte NextTransaction() =>
        unchecked((byte)(Interlocked.Increment(ref _transactionId) & 0x1F));

    private Task<byte[]> QueryAsync(
        byte[] request,
        byte commandId,
        byte profileId,
        byte ledId,
        CancellationToken cancellationToken) =>
        _client.QueryRawAsync(
            request,
            candidate => RazerAccessoryProtocol.MatchesCommand(
                candidate,
                RazerDockAccessoryProtocol.CommandClass,
                commandId),
            cancellationToken);

    private static bool IsExpected(Exception exception) =>
        exception is IOException or InvalidDataException or InvalidOperationException or
            NotSupportedException or TimeoutException or UnauthorizedAccessException or
            System.ComponentModel.Win32Exception or ArgumentException;
}
