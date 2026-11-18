using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.Windows.Devices;

public sealed partial class RazerHidDeviceService
{
    public async Task<int> GetPollingRateAsync(RazerHidDeviceConnection connection, CancellationToken cancellationToken = default)
    {
        var highRate = connection.Definition.Transactions.ContainsKey("razer_chroma_misc_get_polling_rate2");
        var profileId = await ReadActiveProfileIdAsync(connection, cancellationToken).ConfigureAwait(false) ?? 0x00;
        return RazerHidMouseProtocol.ParsePollingRate(await ExecuteAsync(connection,
            RazerHidMouseProtocol.GetPollingRate(connection.Definition, profileId), cancellationToken).ConfigureAwait(false), highRate);
    }

    /// <summary>
    /// Writes the polling rate and reports the rate the firmware holds afterwards. The
    /// device answers a successful write without echoing the payload, so the value read
    /// back is the only evidence that the setting landed.
    /// </summary>
    /// <returns>The reported rate, or null when the device cannot be read back.</returns>
    public async Task<int?> SetPollingRateAsync(RazerHidDeviceConnection connection, int hertz, CancellationToken cancellationToken = default)
    {
        var profileId = await ReadActiveProfileIdAsync(connection, cancellationToken).ConfigureAwait(false) ?? 0x00;
        foreach (var request in RazerHidMouseProtocol.SetPollingRate(connection.Definition, hertz, profileId))
        {
            await ExecuteWithoutResultAsync(connection, request, cancellationToken).ConfigureAwait(false);
        }

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
