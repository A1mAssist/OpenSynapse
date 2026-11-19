using OpenSynapse.Core.Devices;
using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.Windows.Devices;

public sealed partial class RazerHidDeviceService
{
    private static readonly AsyncLocal<HashSet<string>?> MappingModeSessions = new();

    /// <summary>
    /// Switches to driver mode once for a whole mapping page read or write. Without this
    /// scope every row toggles the firmware mode twice, which makes the wheel click and
    /// turns a 13-row refresh into dozens of mode changes.
    /// </summary>
    public async Task RunMappingModeSessionAsync(
        RazerHidDeviceConnection connection,
        Func<CancellationToken, Task> action,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(action);
        if (MappingModeSessions.Value?.Contains(connection.InstanceId) == true)
        {
            await action(cancellationToken).ConfigureAwait(false);
            return;
        }

        await WithMappingModeAsync(connection, async () =>
        {
            var previous = MappingModeSessions.Value;
            var next = previous is null
                ? new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                : new HashSet<string>(previous, StringComparer.OrdinalIgnoreCase);
            next.Add(connection.InstanceId);
            MappingModeSessions.Value = next;
            try
            {
                await action(cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                MappingModeSessions.Value = previous;
            }
        }, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// The Tartarus Pro/02EB answer status 0x05 ("command not supported in this device mode")
    /// to every OBM read until the firmware is switched to driver mode. Synapse switches
    /// mode before its mapping page and restores normal mode on unload; each operation here
    /// does the same so the keypad is never left in driver mode.
    /// </summary>
    private async Task WithMappingModeAsync(
        RazerHidDeviceConnection connection,
        Func<Task> action,
        CancellationToken cancellationToken) =>
        await WithMappingModeAsync(connection, async () =>
        {
            await action().ConfigureAwait(false);
            return true;
        }, cancellationToken).ConfigureAwait(false);

    private async Task<T> WithMappingModeAsync<T>(
        RazerHidDeviceConnection connection,
        Func<Task<T>> action,
        CancellationToken cancellationToken)
    {
        if (MappingModeSessions.Value?.Contains(connection.InstanceId) == true)
        {
            return await action().ConfigureAwait(false);
        }

        if (!RequiresMappingDriverMode(connection))
        {
            return await action().ConfigureAwait(false);
        }

        if (_mappingModeUnavailable.ContainsKey(connection.InstanceId))
        {
            return await action().ConfigureAwait(false);
        }

        var blind = _mappingModeBlind.ContainsKey(connection.InstanceId);
        var original = RazerHidStandardProtocol.NormalDeviceMode;
        if (!blind)
        {
            try
            {
                original = await ReadMappingDeviceModeAsync(connection, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (Exception exception) when (
                exception is InvalidDataException or NotSupportedException or
                    RazerCommandRejectedException or RazerDeviceTimeoutException)
            {
                if (!UsesBlindMappingDriverMode(connection))
                {
                    // A firmware that rejects 00:84 may still answer its OBM tables in
                    // normal mode, so a mode read it does not implement must not take the
                    // whole mapping page down.
                    _mappingModeUnavailable.TryAdd(connection.InstanceId, true);
                    WriteDiagnostic(connection.Definition.ProductId,
                        $"mapping device mode unavailable: {exception.Message}");
                    return await action().ConfigureAwait(false);
                }

                // Tartarus V2/Pro and the Huntsman V3 Pro family need driver mode even when
                // their firmware does not implement the 00:84 read.
                blind = true;
                _mappingModeBlind.TryAdd(connection.InstanceId, true);
                WriteDiagnostic(connection.Definition.ProductId,
                    $"mapping device mode read unavailable; switching without read-back: {exception.Message}");
            }
        }

        var switched = blind || original == RazerHidStandardProtocol.NormalDeviceMode;
        // A device left in driver mode by an earlier crash must be restored too.
        var restore = switched || original == RazerHidStandardProtocol.DriverDeviceMode;
        try
        {
            if (switched)
            {
                if (!await SetMappingDeviceModeAsync(
                        connection, RazerHidStandardProtocol.DriverDeviceMode, cancellationToken,
                        verify: !blind)
                        .ConfigureAwait(false))
                {
                    WriteDiagnostic(connection.Definition.ProductId,
                        "mapping driver mode did not stick; the OBM tables will likely reject with 0x05");
                }
            }

            return await action().ConfigureAwait(false);
        }
        finally
        {
            // Restore even if the write or its read-back fails: the firmware may
            // already be in driver mode and must not be left there.
            if (restore)
            {
                try
                {
                    await SetMappingDeviceModeAsync(
                        connection, RazerHidStandardProtocol.NormalDeviceMode, CancellationToken.None,
                        verify: !blind)
                        .ConfigureAwait(false);
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    WriteDiagnostic(connection.Definition.ProductId,
                        $"mapping device mode restore failed: {exception.Message}");
                }
            }
        }
    }

    private async Task<bool> SetMappingDeviceModeAsync(
        RazerHidDeviceConnection connection,
        byte mode,
        CancellationToken cancellationToken,
        bool verify = true)
    {
        var request = RazerHidStandardProtocol.SetDeviceMode(connection.Definition, mode);
        try
        {
            await ExecuteWithoutResultAsync(connection, request, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is RazerDeviceTimeoutException or NotSupportedException)
        {
            // Some firmware applies driver mode without acknowledging 00:04; Synapse
            // ignores the same timeout. The read-back decides whether it took. A firmware
            // that rejects 00:04 still gets the mapping attempt in its current mode.
        }

        if (!verify)
        {
            WriteDiagnostic(connection.Definition.ProductId,
                $"mapping device mode 0x{mode:X2} requested without read-back");
            return true;
        }

        var after = await ReadMappingDeviceModeAsync(connection, cancellationToken).ConfigureAwait(false);
        WriteDiagnostic(connection.Definition.ProductId,
            $"mapping device mode 0x{mode:X2} requested, now 0x{after:X2}");
        return after == mode;
    }

    private async Task<byte> ReadMappingDeviceModeAsync(
        RazerHidDeviceConnection connection,
        CancellationToken cancellationToken) =>
        RazerHidStandardProtocol.ParseDeviceMode(await ExecuteAsync(connection,
            RazerHidStandardProtocol.GetDeviceMode(connection.Definition), cancellationToken)
            .ConfigureAwait(false));
}
