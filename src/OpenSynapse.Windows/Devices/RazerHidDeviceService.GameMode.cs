using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.Windows.Devices;

/// <summary>Game mode plus the legacy reply's optional key-cover and lifted flags.</summary>
public sealed record RazerKeyboardGameModeState(bool Enabled, byte KeyCover, byte Lifted);

public sealed partial class RazerHidDeviceService
{
    /// <summary>
    /// Resolves the storage byte from the exact manifest. Most keyboards expose the
    /// Game LED in the persistent store; 026B/026C use the temporary store.
    /// </summary>
    internal static bool TryGetGameLedStorage(
        RazerHidDeviceDefinition device,
        out RazerHidStorage storage)
    {
        foreach (var candidate in new[] { RazerHidStorage.Persistent, RazerHidStorage.Temporary })
        {
            if (SupportsStorageAndZone(device, "razer_chroma_standard_get_led_state", candidate, RazerHidLedZone.Game) &&
                SupportsStorageAndZone(device, "razer_chroma_standard_set_led_state", candidate, RazerHidLedZone.Game))
            {
                storage = candidate;
                return true;
            }
        }

        storage = default;
        return false;
    }

    public async Task<RazerKeyboardGameModeState> GetGameModeAsync(
        RazerHidDeviceConnection connection,
        CancellationToken cancellationToken = default)
    {
        RequireReady(connection);
        if (TryGetGameLedStorage(connection.Definition, out var storage))
        {
            var enabled = RazerHidStandardProtocol.ParseLedState(
                await ExecuteAsync(connection,
                    RazerHidStandardProtocol.GetLedState(
                        connection.Definition,
                        (byte)storage,
                        RazerHidStandardProtocol.GameLed),
                    cancellationToken).ConfigureAwait(false),
                (byte)storage,
                RazerHidStandardProtocol.GameLed);
            return new RazerKeyboardGameModeState(enabled, 0, 0);
        }

        // Older keyboards that do not declare the standard Game LED keep the
        // middleware's 00:88/00:08 pair.
        var request = RazerKeyboardProtocol.GetGameModeState(MiddlewareTransactionId);
        var response = await ExecuteAsync(connection,
            new RazerHidRequest("razer_chroma_standard_get_game_mode", request),
            cancellationToken).ConfigureAwait(false);
        var (mode, keyCover, lifted) = RazerKeyboardProtocol.ReadGameModeState(response);
        return new RazerKeyboardGameModeState(mode != 0, keyCover, lifted);
    }

    public Task SetGameModeAsync(
        RazerHidDeviceConnection connection,
        bool enabled,
        CancellationToken cancellationToken = default)
    {
        RequireReady(connection);
        if (TryGetGameLedStorage(connection.Definition, out var storage))
        {
            return ExecuteWithoutResultAsync(connection,
                RazerHidStandardProtocol.SetLedState(
                    connection.Definition,
                    (byte)storage,
                    RazerHidStandardProtocol.GameLed,
                    enabled),
                cancellationToken);
        }

        return ExecuteWithoutResultAsync(connection,
            new RazerHidRequest("razer_chroma_standard_set_game_mode",
                RazerKeyboardProtocol.SetGameModeState(
                    MiddlewareTransactionId, enabled ? (byte)1 : (byte)0)),
            cancellationToken);
    }
}
