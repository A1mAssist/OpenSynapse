using System.Collections.Concurrent;
using OpenSynapse.Core.Devices;
using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.Windows.Devices;

public sealed partial class RazerHidDeviceService
{
    // A page read/apply enters and exits on the UI thread; WinUI does not always
    // flow AsyncLocal through its continuations, so the session has to live on
    // the service keyed by device instead of on the execution context.
    private readonly ConcurrentDictionary<string, int> _mappingModeSessions =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Switches to driver mode once for a whole mapping page read or write. Without this
    /// scope every row toggles the firmware mode twice, which makes the wheel click and
    /// turns a 13-row refresh into dozens of mode changes.
    /// </summary>
    public async Task RunMappingModeSessionAsync(
        RazerHidDeviceConnection connection,
        Func<CancellationToken, Task> action,
        CancellationToken cancellationToken = default,
        bool forWrite = false)
    {
        ArgumentNullException.ThrowIfNull(action);
        var context = SynchronizationContext.Current;
        var depth = _mappingModeSessions.AddOrUpdate(
            connection.InstanceId, 1, static (_, value) => value + 1);
        // 02CF rejects the 00:84 mode read but still answers its OBM tables in
        // normal mode. Switching it to driver mode makes the page read fail;
        // writes keep the blind switch below.
        var normalModeRead = !forWrite && connection.Definition.ProductId == 0x02CF;
        try
        {
            if (depth == 1 && !normalModeRead)
            {
                await WithMappingModeAsync(
                    connection,
                    async () =>
                    {
                        await PrepareObmKeymapAsync(connection, cancellationToken)
                            .ConfigureAwait(false);
                        await RunOnContextAsync(context, () => action(cancellationToken))
                            .ConfigureAwait(false);
                    },
                    cancellationToken,
                    bypassSession: true)
                    .ConfigureAwait(false);
            }
            else
            {
                await action(cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            if (_mappingModeSessions.AddOrUpdate(
                    connection.InstanceId, 0, static (_, value) => value - 1) <= 1)
            {
                _mappingModeSessions.TryRemove(connection.InstanceId, out _);
            }
        }
    }

    /// <summary>
    /// Synapse selects the OBM keymap before every assignment-table read. The
    /// Tartarus Pro rejects 0x92/0x8D with status 0x05 while the target keymap
    /// is unset, so read the current keymap, or fall back to 1, and select it.
    /// </summary>
    private async Task PrepareObmKeymapAsync(
        RazerHidDeviceConnection connection,
        CancellationToken cancellationToken)
    {
        if (connection.Definition.ProductId != 0x0244)
        {
            return;
        }

        var keymapId = RazerHidMappingProtocol.ProfileId;
        try
        {
            keymapId = RazerHidMappingProtocol.ParseObmKeymap(
                await ExecuteAsync(connection, RazerHidMappingProtocol.GetObmKeymap(), cancellationToken)
                    .ConfigureAwait(false));
        }
        catch (Exception exception) when (
            exception is InvalidDataException or NotSupportedException or
                RazerCommandRejectedException or RazerDeviceTimeoutException)
        {
            WriteDiagnostic(connection.Definition.ProductId,
                $"mapping keymap read failed: {exception.Message}");
        }

        if (keymapId == 0)
        {
            keymapId = RazerHidMappingProtocol.ProfileId;
        }

        try
        {
            await ExecuteWithoutResultAsync(connection,
                RazerHidMappingProtocol.SetObmKeymap(keymapId), cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception exception) when (
            exception is NotSupportedException or RazerCommandRejectedException or
                RazerDeviceTimeoutException)
        {
            WriteDiagnostic(connection.Definition.ProductId,
                $"mapping keymap set failed: {exception.Message}");
        }
    }

    private static async Task RunOnContextAsync(
        SynchronizationContext? context,
        Func<Task> action)
    {
        if (context is null || SynchronizationContext.Current == context)
        {
            await action().ConfigureAwait(true);
            return;
        }

        var completion = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        context.Post(async _ =>
        {
            try
            {
                await action().ConfigureAwait(true);
                completion.TrySetResult();
            }
            catch (OperationCanceledException exception)
            {
                completion.TrySetCanceled(exception.CancellationToken);
            }
            catch (Exception exception)
            {
                completion.TrySetException(exception);
            }
        }, null);
        await completion.Task.ConfigureAwait(false);
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
        CancellationToken cancellationToken,
        bool bypassSession = false) =>
        await WithMappingModeAsync(connection, async () =>
        {
            await action().ConfigureAwait(false);
            return true;
        }, cancellationToken, bypassSession).ConfigureAwait(false);

    private async Task<T> WithMappingModeAsync<T>(
        RazerHidDeviceConnection connection,
        Func<Task<T>> action,
        CancellationToken cancellationToken,
        bool bypassSession = false)
    {
        if (connection.Definition.ProductId == 0x0244)
        {
            if (!bypassSession && _mappingModeSessions.ContainsKey(connection.InstanceId))
            {
                return await action().ConfigureAwait(false);
            }

            return await WithTartarusProMappingModeAsync(
                connection, action, cancellationToken).ConfigureAwait(false);
        }

        if (!bypassSession && _mappingModeSessions.ContainsKey(connection.InstanceId))
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

                // Tartarus V2 and the Huntsman V3 Pro family need driver mode even when
                // their firmware does not implement the 00:84 read. The Tartarus Pro
                // (0244) is different: Synapse leaves it in normal mode when 00:84 fails.
                blind = true;
                _mappingModeBlind.TryAdd(connection.InstanceId, true);
                WriteDiagnostic(connection.Definition.ProductId,
                    $"mapping device mode read unavailable; switching without read-back: {exception.Message}");
            }
        }

        var switched = blind || original == RazerHidStandardProtocol.NormalDeviceMode;
        // A device left in driver mode by an earlier crash must be restored, but a
        // mode the private-key runtime is holding must stay in driver mode.
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
            if (restore &&
                !RazerDeviceModeOwnership.IsDriverModeHeld(connection.Definition.ProductId))
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
        bool verify = true,
        byte parameter = RazerHidStandardProtocol.NoStore)
    {
        var request = RazerHidStandardProtocol.SetDeviceMode(connection.Definition, mode, parameter);
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

    /// <summary>
    /// The Tartarus Pro (0244) answers 0x05 to every class-0x02 OBM command on the
    /// firmware seen in the field. Try the mode/param pairs used by Synapse and
    /// OpenRazer and keep the first one that makes 02:84 answer; restore normal
    /// mode afterwards so the keypad keeps sending its normal HID reports.
    /// </summary>
    private async Task<T> WithTartarusProMappingModeAsync<T>(
        RazerHidDeviceConnection connection,
        Func<Task<T>> action,
        CancellationToken cancellationToken)
    {
        if (_tartarusMappingUnavailable.ContainsKey(connection.InstanceId))
        {
            return await action().ConfigureAwait(false);
        }

        if (_tartarusMappingModes.TryGetValue(connection.InstanceId, out var known))
        {
            try
            {
                await SetMappingDeviceModeAsync(
                    connection, known.Mode, cancellationToken, verify: false, known.Parameter)
                    .ConfigureAwait(false);
                return await action().ConfigureAwait(false);
            }
            finally
            {
                await RestoreTartarusProModeAsync(connection).ConfigureAwait(false);
            }
        }

        try
        {
            foreach (var candidate in TartarusProMappingModes)
            {
                try
                {
                    await SetMappingDeviceModeAsync(
                        connection, candidate.Mode, cancellationToken, verify: false, candidate.Parameter)
                        .ConfigureAwait(false);
                    var buttons = RazerHidMappingProtocol.ParseButtonList(
                        await ExecuteAsync(connection, RazerHidMappingProtocol.GetButtonList(), cancellationToken)
                            .ConfigureAwait(false));
                    if (buttons.Count == 0)
                    {
                        throw new InvalidDataException("02:84 returned an empty button list.");
                    }
                    _tartarusMappingModes[connection.InstanceId] = candidate;
                    WriteDiagnostic(connection.Definition.ProductId,
                        $"mapping OBM unlocked in mode 0x{candidate.Mode:X2}/0x{candidate.Parameter:X2}");
                    return await action().ConfigureAwait(false);
                }
                catch (Exception exception) when (
                    exception is InvalidDataException or NotSupportedException or
                        RazerCommandRejectedException or RazerDeviceTimeoutException)
                {
                    WriteDiagnostic(connection.Definition.ProductId,
                        $"mapping OBM rejected in mode 0x{candidate.Mode:X2}/0x{candidate.Parameter:X2}: " +
                        exception.Message);
                }
            }

            WriteDiagnostic(connection.Definition.ProductId,
                "mapping OBM rejected 02:84 in normal, driver and driver+store modes; " +
                "the firmware does not expose the onboard table");
            _tartarusMappingUnavailable.TryAdd(connection.InstanceId, true);
            await RestoreTartarusProModeAsync(connection).ConfigureAwait(false);
            return await action().ConfigureAwait(false);
        }
        finally
        {
            await RestoreTartarusProModeAsync(connection).ConfigureAwait(false);
        }
    }

    private async Task RestoreTartarusProModeAsync(RazerHidDeviceConnection connection)
    {
        try
        {
            await SetMappingDeviceModeAsync(
                connection, RazerHidStandardProtocol.NormalDeviceMode,
                CancellationToken.None, verify: false).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            WriteDiagnostic(connection.Definition.ProductId,
                $"mapping device mode restore failed: {exception.Message}");
        }
    }

    private async Task<byte> ReadMappingDeviceModeAsync(
        RazerHidDeviceConnection connection,
        CancellationToken cancellationToken) =>
        RazerHidStandardProtocol.ParseDeviceMode(await ExecuteAsync(connection,
            RazerHidStandardProtocol.GetDeviceMode(connection.Definition), cancellationToken)
            .ConfigureAwait(false));
}
