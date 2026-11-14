using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.Windows.Devices;

public sealed partial class RazerKeyboardService
{
    /// <summary>
    /// Protocol-25 keyboards whose bundle declares gameMode.hasGameModeLED. Their
    /// game mode is the standard Game LED zone; 0249/0277/02C4/02EB have no gameMode block.
    /// </summary>
    internal static readonly IReadOnlySet<ushort> GameModeLedProducts = new HashSet<ushort>
    {
        0x023E, 0x024F, 0x0250, 0x0251, 0x028E, 0x02B1, 0x02B3, 0x02CC,
        0x02D0, 0x02D1, 0x02D4, 0x02D8, 0x02E3, 0x02E4, 0x02E5, 0x02E6,
        0x02EA, 0x02F0, 0x02F4,
    };

    public async Task SetGameModeAsync(bool enabled, CancellationToken cancellationToken = default)
    {
        var previous = ReadGameMode(
            await QueryAsync(GameModeReadRequest, "read game mode", cancellationToken)
                .ConfigureAwait(false));
        if (previous == enabled)
        {
            return;
        }

        try
        {
            await QueryAsync(
                transaction => GameModeWriteRequest(transaction, enabled),
                "set game mode",
                cancellationToken).ConfigureAwait(false);
            var actual = ReadGameMode(
                await QueryAsync(GameModeReadRequest, "read back game mode", cancellationToken)
                    .ConfigureAwait(false));
            if (actual != enabled)
            {
                throw new InvalidOperationException(
                    $"Game mode readback was {actual}, expected {enabled}.");
            }
        }
        catch
        {
            await TryRestoreAsync(
                transaction => GameModeWriteRequest(transaction, previous),
                "restore game mode").ConfigureAwait(false);
            throw;
        }
    }

    private Func<byte, byte[]> GameModeReadRequest => GameModeLedProducts.Contains(_productId)
        ? RazerKeyboardProtocol.GetGameLedState
        : RazerKeyboardProtocol.GetGameModeState;

    private byte[] GameModeWriteRequest(byte transaction, bool enabled) =>
        GameModeLedProducts.Contains(_productId)
            ? RazerKeyboardProtocol.SetGameLedState(transaction, enabled)
            : RazerKeyboardProtocol.SetGameModeState(transaction, enabled ? (byte)1 : (byte)0);

    private bool ReadGameMode(ReadOnlySpan<byte> response) =>
        GameModeLedProducts.Contains(_productId)
            ? RazerKeyboardProtocol.ReadGameLedState(response)
            : RazerKeyboardProtocol.ReadGameModeState(response).GameMode != 0;
}
