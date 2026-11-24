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
                    async () => await RunOnContextAsync(context, () => action(cancellationToken))
                        .ConfigureAwait(false),
                    cancellationToken,
                    bypassSession: true)
                    .ConfigureAwait(false);
            }
            else
            {
                // 02CF cannot report its mode, so an interrupted save can leave it in
                // driver mode and every OBM read then fails until the app restarts.
                // Put it back blind before a read unless the private-key runtime owns it.
                if (depth == 1 && normalModeRead &&
                    !RazerDeviceModeOwnership.IsDriverModeHeld(connection.Definition.ProductId))
                {
                    try
                    {
                        await SetMappingDeviceModeAsync(
                            connection, RazerHidStandardProtocol.NormalDeviceMode,
                            cancellationToken, verify: false)
                            .ConfigureAwait(false);
                    }
                    catch (Exception exception) when (
                        exception is InvalidDataException or NotSupportedException or
                            RazerCommandRejectedException or RazerDeviceTimeoutException)
                    {
                        WriteDiagnostic(connection.Definition.ProductId,
                            $"mapping normal mode restore before read failed: {exception.Message}");
                    }
                }

                await RunOnContextAsync(context, () => action(cancellationToken))
                    .ConfigureAwait(false);
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
    /// is unset, so read the active keymap, or fall back to 1, and select it.
    /// </summary>
    private async Task PrepareObmKeymapAsync(
        RazerHidDeviceConnection connection,
        CancellationToken cancellationToken)
    {
        if (connection.Definition.ProductId != 0x0244)
        {
            return;
        }

        var keymapId = (byte)0;
        try
        {
            keymapId = RazerHidMappingProtocol.ParseActiveKeymap(
                await ExecuteAsync(connection,
                    RazerHidMappingProtocol.GetActiveKeymap(RazerHidMappingProtocol.ProfileId),
                    cancellationToken).ConfigureAwait(false));
        }
        catch (Exception exception) when (
            exception is InvalidDataException or NotSupportedException or
                RazerCommandRejectedException or RazerDeviceTimeoutException)
        {
            WriteDiagnostic(connection.Definition.ProductId,
                $"mapping active keymap read failed: {exception.Message}");
            try
            {
                keymapId = RazerHidMappingProtocol.ParseObmKeymap(
                    await ExecuteAsync(connection, RazerHidMappingProtocol.GetObmKeymap(),
                        cancellationToken).ConfigureAwait(false));
            }
            catch (Exception targetException) when (
                targetException is InvalidDataException or NotSupportedException or
                    RazerCommandRejectedException or RazerDeviceTimeoutException)
            {
                WriteDiagnostic(connection.Definition.ProductId,
                    $"mapping keymap read failed: {targetException.Message}");
            }
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

    private static Task RunOnContextAsync(
        SynchronizationContext? context,
        Func<Task> action) =>
        RunOnContextAsync(context, async () =>
        {
            await action().ConfigureAwait(true);
            return true;
        });

    private static async Task<T> RunOnContextAsync<T>(
        SynchronizationContext? context,
        Func<Task<T>> action)
    {
        if (context is null || SynchronizationContext.Current == context)
        {
            return await action().ConfigureAwait(true);
        }

        var completion = new TaskCompletionSource<T>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        // Some WinUI continuations drop AsyncLocal; keep the held transaction state.
        var executionContext = ExecutionContext.Capture();
        context.Post(async _ =>
        {
            try
            {
                var work = executionContext is null
                    ? action()
                    : RunInExecutionContext(executionContext, action);
                completion.TrySetResult(await work.ConfigureAwait(false));
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
        return await completion.Task.ConfigureAwait(false);
    }

    private static Task<T> RunInExecutionContext<T>(
        ExecutionContext executionContext,
        Func<Task<T>> action)
    {
        Task<T>? task = null;
        ExecutionContext.Run(executionContext, _ => task = action(), null);
        return task!;
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
                // (0244) takes the dedicated path above.
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
    /// The Tartarus Pro (0244) answers 0x05 to every class-0x02 OBM command outside
    /// driver mode. Synapse uses 00:04 = 3/0 and never sends 02:84 on this keypad;
    /// keep the first mode where a real assignment read answers, then restore normal
    /// mode so the keypad keeps sending its normal HID reports.
    /// </summary>
    private async Task<T> WithTartarusProMappingModeAsync<T>(
        RazerHidDeviceConnection connection,
        Func<Task<T>> action,
        CancellationToken cancellationToken)
    {
        if (_tartarusMappingModes.TryGetValue(connection.InstanceId, out var known))
        {
            try
            {
                await SetMappingDeviceModeAsync(
                    connection, known.Mode, cancellationToken, verify: false, known.Parameter)
                    .ConfigureAwait(false);
                await PrepareObmKeymapAsync(connection, cancellationToken).ConfigureAwait(false);
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
                    await PrepareObmKeymapAsync(connection, cancellationToken).ConfigureAwait(false);
                    await ProbeKeypadTableAsync(connection, cancellationToken).ConfigureAwait(false);
                    _tartarusMappingModes[connection.InstanceId] = candidate;
                    WriteDiagnostic(connection.Definition.ProductId,
                        $"mapping OBM read unlocked in mode 0x{candidate.Mode:X2}/0x{candidate.Parameter:X2}");
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
                "mapping OBM rejected in driver, driver+store and normal modes; " +
                "the firmware does not expose the onboard table");
            await RestoreTartarusProModeAsync(connection).ConfigureAwait(false);
            return await action().ConfigureAwait(false);
        }
        finally
        {
            await RestoreTartarusProModeAsync(connection).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// 0244 has no 02:84 key table; asking for it only produces a false negative.
    /// Probe the two tables the keypad engine actually reads.
    /// </summary>
    private async Task ProbeKeypadTableAsync(
        RazerHidDeviceConnection connection,
        CancellationToken cancellationToken)
    {
        Exception? lastError = null;
        foreach (var table in new[] { AssignmentTable.Analog, AssignmentTable.Key })
        {
            try
            {
                var request = table == AssignmentTable.Analog
                    ? RazerHidMappingProtocol.GetAnalogKeyAssignment(
                        RazerHidMappingProtocol.ProfileId, 0x40, RazerHidMappingProtocol.NormalMode,
                        (byte)RazerHidMappingProtocol.ReportDataSize)
                    : RazerHidMappingProtocol.GetKeyAssignment(
                        RazerHidMappingProtocol.ProfileId, 0x40, RazerHidMappingProtocol.NormalMode,
                        (byte)RazerHidMappingProtocol.ReportDataSize);
                var response = await ExecuteAsync(connection, request, cancellationToken)
                    .ConfigureAwait(false);
                if (table == AssignmentTable.Analog)
                {
                    _ = RazerHidMappingProtocol.ParseAnalogAssignment(response);
                }
                else
                {
                    _ = RazerHidMappingProtocol.ParseAssignment(response);
                }

                return;
            }
            catch (Exception exception) when (
                exception is InvalidDataException or NotSupportedException or
                    RazerCommandRejectedException or RazerDeviceTimeoutException)
            {
                lastError = exception;
            }
        }

        throw new InvalidDataException(
            $"The keypad assignment tables did not answer: {lastError?.Message}");
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
