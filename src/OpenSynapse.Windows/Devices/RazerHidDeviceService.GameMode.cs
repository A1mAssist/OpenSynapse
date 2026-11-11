using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.Windows.Devices;

/// <summary>00:88 reply: mode plus the optional key-cover and lifted flags.</summary>
public sealed record RazerKeyboardGameModeState(bool Enabled, byte KeyCover, byte Lifted);

public sealed partial class RazerHidDeviceService
{
    // The JS keyboard deviceUI reads game mode with 00:88 and writes it with
    // 00:08, both four-byte payloads; 0203/0216/0235 declare the same pair.
    public async Task<RazerKeyboardGameModeState> GetGameModeAsync(
        RazerHidDeviceConnection connection,
        CancellationToken cancellationToken = default)
    {
        RequireReady(connection);
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
        return ExecuteWithoutResultAsync(connection,
            new RazerHidRequest("razer_chroma_standard_set_game_mode",
                RazerKeyboardProtocol.SetGameModeState(
                    MiddlewareTransactionId, enabled ? (byte)1 : (byte)0)),
            cancellationToken);
    }
}
