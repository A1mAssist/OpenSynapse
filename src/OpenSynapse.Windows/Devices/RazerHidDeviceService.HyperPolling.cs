using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.Windows.Devices;

public sealed partial class RazerHidDeviceService
{
    public async Task<byte> GetHyperPollingIndicatorAsync(
        RazerHidDeviceConnection connection,
        CancellationToken cancellationToken = default)
    {
        // The get/set pair shares the receiver transaction declared by the catalog.
        var response = await ExecuteAsync(connection, RazerHidStandardProtocol.Create(connection.Definition,
            "razer_chroma_misc_set_hyperpolling_wireless_dongle_indicator_led_mode",
            0x01, 0x07, 0x90, []), cancellationToken).ConfigureAwait(false);
        var mode = RazerHidStandardProtocol.Arguments(response, 1)[0];
        return mode is >= 1 and <= 3 ? mode
            : throw new RazerReportMismatchException($"Invalid HyperPolling indicator mode: {mode}.");
    }

    public Task SetHyperPollingIndicatorAsync(
        RazerHidDeviceConnection connection,
        byte mode,
        CancellationToken cancellationToken = default)
    {
        if (mode is < 1 or > 3)
        {
            throw new ArgumentOutOfRangeException(nameof(mode));
        }
        return RunExclusiveAsync(connection, async token =>
        {
            await ExecuteWithoutResultAsync(connection, RazerHidStandardProtocol.Create(connection.Definition,
                "razer_chroma_misc_set_hyperpolling_wireless_dongle_indicator_led_mode",
                0x01, 0x07, 0x10, [mode]), token).ConfigureAwait(false);
            var actual = await GetHyperPollingIndicatorAsync(connection, token).ConfigureAwait(false);
            WriteDiagnostic(connection.Definition.ProductId,
                $"HyperPolling indicator: requested={mode}, readback={actual}.");
            if (actual != mode)
                throw new RazerReportMismatchException($"HyperPolling indicator readback mismatch: requested={mode}, actual={actual}.");
        }, cancellationToken);
    }

    public async Task PairHyperPollingAsync(
        RazerHidDeviceConnection connection,
        ushort mouseProductId,
        CancellationToken cancellationToken = default)
    {
        var step1 = RazerHidStandardProtocol.Create(connection.Definition,
            "razer_chroma_misc_set_hyperpolling_wireless_dongle_pair_step1",
            0x01, 0x00, 0x46, [0x01]);
        var step2 = RazerHidStandardProtocol.Create(connection.Definition,
            "razer_chroma_misc_set_hyperpolling_wireless_dongle_pair_step2",
            0x03, 0x00, 0x41, [0x01, checked((byte)(mouseProductId >> 8)), checked((byte)mouseProductId)]);
        await ExecuteWithoutResultAsync(connection, step1, cancellationToken).ConfigureAwait(false);
        await ExecuteWithoutResultAsync(connection, step2, cancellationToken).ConfigureAwait(false);
    }

    public Task UnpairHyperPollingAsync(
        RazerHidDeviceConnection connection,
        ushort mouseProductId,
        CancellationToken cancellationToken = default) =>
        ExecuteWithoutResultAsync(connection, RazerHidStandardProtocol.Create(connection.Definition,
            "razer_chroma_misc_set_hyperpolling_wireless_dongle_unpair",
            0x02, 0x00, 0x42,
            [checked((byte)(mouseProductId >> 8)), checked((byte)mouseProductId)]), cancellationToken);
}
