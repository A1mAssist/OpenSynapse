using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.Windows.Devices;

public sealed partial class RazerHidDeviceService
{
    public async Task<int> GetPollingRateAsync(RazerHidDeviceConnection connection, CancellationToken cancellationToken = default)
    {
        var definition = connection.Definition;
        var highRate = connection.PollingRateUsesHighRate ??
            RazerHidMouseProtocol.PrefersHighRate(definition);
        var profileId = await ReadActiveProfileIdAsync(connection, cancellationToken).ConfigureAwait(false) ?? 0x00;
        try
        {
            var response = await ExecuteAsync(connection,
                RazerHidMouseProtocol.GetPollingRate(definition, profileId, highRate), cancellationToken).ConfigureAwait(false);
            connection.PollingRateUsesHighRate = highRate;
            return RazerHidMouseProtocol.ParsePollingRate(response, highRate);
        }
        // RazerHidRequestExecutor maps a device rejection (0x05) to NotSupportedException
        // and leaves a silent, non-busy timeout as RazerDeviceTimeoutException. Both mean
        // "this family is not implemented here", so both must fall back.
        catch (Exception exception) when (
            (exception is NotSupportedException or RazerDeviceTimeoutException { IsRetryableBusy: false }) &&
            definition.Transactions.ContainsKey(
            highRate ? "razer_chroma_misc_get_polling_rate" : "razer_chroma_misc_get_polling_rate2"))
        {
            // A dual-link dongle answers the high-speed pair only while docked; the same
            // device on its plain receiver answers the legacy pair.
            highRate = !highRate;
            var response = await ExecuteAsync(connection,
                RazerHidMouseProtocol.GetPollingRate(definition, profileId, highRate), cancellationToken).ConfigureAwait(false);
            connection.PollingRateUsesHighRate = highRate;
            return RazerHidMouseProtocol.ParsePollingRate(response, highRate);
        }
    }

    /// <summary>
    /// Writes the polling rate and reports the rate the firmware holds afterwards. The
    /// device answers a successful write without echoing the payload, so the value read
    /// back is the only evidence that the setting landed.
    /// </summary>
    /// <returns>The reported rate, or null when the device cannot be read back.</returns>
    public async Task<int?> SetPollingRateAsync(RazerHidDeviceConnection connection, int hertz, CancellationToken cancellationToken = default)
    {
        var definition = connection.Definition;
        // Rates above 1000 Hz only exist in the high-speed ladder. A legacy fallback that
        // happened during a read must not make this write throw on EncodePollingRate.
        var highRate = hertz > 1000 ||
            (connection.PollingRateUsesHighRate ?? RazerHidMouseProtocol.PrefersHighRate(definition));
        var profileId = await ReadActiveProfileIdAsync(connection, cancellationToken).ConfigureAwait(false) ?? 0x00;
        try
        {
            await WritePollingRateAsync(connection, hertz, profileId, highRate, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (
            (exception is NotSupportedException or RazerDeviceTimeoutException { IsRetryableBusy: false }) &&
            CanFallBack(definition, hertz, highRate))
        {
            highRate = !highRate;
            await WritePollingRateAsync(connection, hertz, profileId, highRate, cancellationToken).ConfigureAwait(false);
        }
        connection.PollingRateUsesHighRate = highRate;

        if (!connection.Capabilities.Contains(RazerHidBackendCapability.PollingRateRead))
        {
            return null;
        }

        try
        {
            return await GetPollingRateAsync(connection, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is RazerDeviceTimeoutException or InvalidDataException)
        {
            // The write already landed. A device that cannot answer the read-back must not
            // turn a completed write into a failure.
            return null;
        }
    }

    private async Task WritePollingRateAsync(
        RazerHidDeviceConnection connection,
        int hertz,
        byte profileId,
        bool highRate,
        CancellationToken cancellationToken)
    {
        foreach (var request in RazerHidMouseProtocol.SetPollingRate(
                     connection.Definition, hertz, profileId, highRate))
        {
            await ExecuteWithoutResultAsync(connection, request, cancellationToken).ConfigureAwait(false);
        }
    }

    private static bool CanFallBack(RazerHidDeviceDefinition device, int hertz, bool highRate) =>
        highRate
            // The legacy table has no code for the high-speed-only ladders.
            ? hertz <= 1000 && device.Transactions.ContainsKey("razer_chroma_misc_set_polling_rate")
            : device.Transactions.ContainsKey("razer_chroma_misc_set_polling_rate2") ||
              device.CommandSequences.ContainsKey("razer_chroma_misc_set_polling_rate2");

    private async Task<byte?> ReadActiveProfileIdAsync(
        RazerHidDeviceConnection connection,
        CancellationToken cancellationToken)
    {
        var request = RazerHidMouseProtocol.GetActiveProfile(connection.Definition);
        if (request is null)
        {
            return null;
        }

        try
        {
            var response = await ExecuteAsync(connection, request, cancellationToken).ConfigureAwait(false);
            return response[RazerFeatureReport.ArgumentsOffset];
        }
        catch (Exception exception) when (exception is RazerDeviceTimeoutException or RazerCommandRejectedException or NotSupportedException or InvalidDataException)
        {
            // The write can still be attempted with the default profile; a read failure
            // must not turn a working polling-rate write into a no-op.
            return null;
        }
    }

    public async Task<(int X, int Y)> GetDpiAsync(RazerHidDeviceConnection connection, CancellationToken cancellationToken = default)
    {
        var definition = connection.Definition;
        var response = await ExecuteAsync(connection,
            RazerHidMouseProtocol.GetDpi(definition), cancellationToken).ConfigureAwait(false);
        var dpi = definition.UsesP1DpiScale
            ? RazerHidStandardProtocol.ParseP1Dpi(response)
            : RazerHidMouseProtocol.ParseDpi(response,
                definition.Transactions.ContainsKey("razer_chroma_misc_get_dpi_xy_byte"));
        return dpi.X > 0 && dpi.Y > 0 ? dpi : throw new InvalidDataException("The DPI response contained no values.");
    }

    /// <summary>
    /// Writes the DPI and reports the values the firmware holds afterwards, so a caller
    /// can tell a stored setting from a write the device ignored.
    /// </summary>
    /// <returns>The reported DPI, or null when the device cannot be read back.</returns>
    public async Task<(int X, int Y)?> SetDpiAsync(RazerHidDeviceConnection connection, int x, int y, CancellationToken cancellationToken = default)
    {
        if (!connection.Definition.SupportsIndependentDpi)
        {
            y = x;
        }
        await ExecuteWithoutResultAsync(connection,
            RazerHidMouseProtocol.SetDpi(connection.Definition, x, y), cancellationToken).ConfigureAwait(false);

        if (!connection.Capabilities.Contains(RazerHidBackendCapability.DpiRead))
        {
            return null;
        }

        try
        {
            return await GetDpiAsync(connection, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is RazerDeviceTimeoutException or InvalidDataException)
        {
            // The write already landed. A device that cannot answer the read-back must not
            // turn a completed write into a failure.
            return null;
        }
    }

    public Task SetDpiStagesAsync(RazerHidDeviceConnection connection, RazerHidDpiStages state, CancellationToken cancellationToken = default) =>
        ExecuteWithoutResultAsync(connection,
            RazerHidMouseProtocol.SetDpiStages(connection.Definition, state), cancellationToken);

    public async Task<RazerHidDpiStages> GetDpiStagesAsync(
        RazerHidDeviceConnection connection,
        CancellationToken cancellationToken = default)
    {
        var request = RazerHidMouseProtocol.GetDpiStages(connection.Definition);
        var response = await ExecuteAsync(connection, request, cancellationToken).ConfigureAwait(false);
        try
        {
            return RazerHidMouseProtocol.ParseDpiStages(connection.Definition, response);
        }
        catch (InvalidDataException exception)
        {
            WriteDiagnostic(connection.Definition.ProductId,
                $"DPI stages response rejected: {exception.Message}; report={Convert.ToHexString(response)}");
            throw;
        }
    }
}
