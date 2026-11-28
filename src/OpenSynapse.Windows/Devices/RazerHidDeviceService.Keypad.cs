using OpenSynapse.Core.Macros;
using OpenSynapse.Core.Devices;
using OpenSynapse.Core.Profiles;
using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.Windows.Devices;

public sealed partial class RazerHidDeviceService
{
    private readonly SemaphoreSlim _keypadGate = new(1, 1);
    private readonly Dictionary<string, RazerKeypadMappingSession> _keypadSessions = new(StringComparer.OrdinalIgnoreCase);
    private bool _keypadStopped;

    public event Action<MacroInput, bool, bool>? KeypadInput;
    internal Func<RazerHidDeviceConnection, IReadOnlyList<ButtonMappingProfile>>? KeypadMappingProfileResolver { get; set; }
    internal Func<RazerHidDeviceConnection, IReadOnlyList<RazerHidButtonAssignment>, CancellationToken,
        Task<RazerKeypadMappingSession>>? KeypadSessionFactory { get; set; }

    internal static bool UsesHostKeypadMapping(RazerHidDeviceConnection connection) =>
        RazerKeypadInputCatalog.UsesHostMapping(connection.Definition.ProductId);

    private static void ValidateKeypadFirmwareInput(RazerHidDeviceConnection connection, byte buttonId, byte mode)
    {
        if (connection.Definition.ProductId != 0x02EB) return;
        if (!RazerKeypadKeyCatalog.TartarusV2ProMappingIds.Contains(buttonId))
            throw new NotSupportedException($"Keypad input 0x{buttonId:X2} has no editable firmware assignment.");
        if (mode > 1) throw new ArgumentOutOfRangeException(nameof(mode));
    }

    private Task<T> WithKeypadKeymapAsync<T>(
        RazerHidDeviceConnection connection, Func<Task<T>> action, CancellationToken cancellationToken)
    {
        if (connection.Definition.ProductId != 0x02EB) return action();
        return RunExclusiveAsync(connection, async token =>
        {
            var profile = await ResolveMappingProfileIdAsync(connection, token).ConfigureAwait(false);
            var activeReply = await ExecuteAsync(connection, RazerHidMappingProtocol.GetActiveKeymap(profile), token)
                .ConfigureAwait(false);
            var activeArgs = RazerHidStandardProtocol.Arguments(activeReply, 2);
            var active = activeArgs[1];
            if (activeArgs[0] != profile || active == 0)
                throw new InvalidDataException("The keypad returned an invalid active keymap.");
            var previous = RazerHidMappingProtocol.ParseObmKeymap(await ExecuteAsync(
                connection, RazerHidMappingProtocol.GetObmKeymap(), token).ConfigureAwait(false));
            try
            {
                await ExecuteWithoutResultAsync(connection, RazerHidMappingProtocol.SetObmKeymap(active), token)
                    .ConfigureAwait(false);
                return await action().ConfigureAwait(false);
            }
            finally
            {
                await ExecuteWithoutResultAsync(connection, RazerHidMappingProtocol.SetObmKeymap(previous),
                    CancellationToken.None).ConfigureAwait(false);
            }
        }, cancellationToken);
    }

    private async Task<RazerKeypadMappingRuntime> GetKeypadRuntimeAsync(
        RazerHidDeviceConnection connection, CancellationToken cancellationToken,
        IReadOnlyList<RazerHidButtonAssignment>? initialAssignments = null)
    {
        RequireReady(connection);
        await _keypadGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_keypadStopped) throw new RazerKeypadMappingUnavailableException(
                RazerKeypadMappingUnavailableReason.Stopped, "Keypad mapping is shutting down.");
            if (!_keypadSessions.TryGetValue(connection.InstanceId, out var session))
            {
                var assignments = initialAssignments ?? ToKeypadAssignments(
                    KeypadMappingProfileResolver?.Invoke(connection) ?? [], connection.Definition.ProductId);
                session = KeypadSessionFactory is { } factory
                    ? await factory(connection, assignments, cancellationToken).ConfigureAwait(false)
                    : await RazerKeypadMappingSession.StartAsync(connection, assignments,
                        (input, down, layer) => KeypadInput?.Invoke(input, down, layer),
                        message => WriteDiagnostic(connection.Definition.ProductId, message), cancellationToken).ConfigureAwait(false);
                _keypadSessions.Add(connection.InstanceId, session);
                WriteDiagnostic(connection.Definition.ProductId, "device-scoped host mapping ready; hardware OBM is not used");
            }
            session.RequireRunning();
            return session.Runtime;
        }
        finally { _keypadGate.Release(); }
    }

    internal async Task ReplaceKeypadMappingsAsync(
        RazerHidDeviceConnection connection, IReadOnlyCollection<ButtonMappingProfile> mappings,
        CancellationToken cancellationToken)
    {
        var assignments = ToKeypadAssignments(mappings, connection.Definition.ProductId);
        var runtime = await GetKeypadRuntimeAsync(connection, cancellationToken, assignments).ConfigureAwait(false);
        await runtime.ReplaceAssignmentsAsync(assignments, cancellationToken).ConfigureAwait(false);
    }

    private static IReadOnlyList<RazerHidButtonAssignment> ToKeypadAssignments(
        IEnumerable<ButtonMappingProfile> mappings, ushort productId)
    {
        var assignments = mappings.Where(mapping => mapping.FunctionId.HasValue)
            // Earlier versions invented five extra D-pad rows. Keep them on disk, but never execute them.
            .Where(mapping => !RazerKeypadInputCatalog.IsLegacy(productId) || mapping.ButtonId is not (>= 0xF4 and <= 0xF8))
            .Select(mapping => new RazerHidButtonAssignment(mapping.ButtonId, mapping.Mode ?? 0,
                mapping.FunctionId!.Value, mapping.FunctionData?.ToArray() ?? [])).ToArray();
        foreach (var assignment in assignments) RazerKeypadInputCatalog.ValidateAssignment(assignment, productId);
        if (assignments.Select(item => (item.ButtonId, item.Mode)).Distinct().Count() != assignments.Length)
            throw new ArgumentException("A keypad input has more than one assignment in the same layer.", nameof(mappings));
        return assignments;
    }

    internal async Task SyncKeypadMappingsAsync(
        IReadOnlyList<RazerHidDeviceConnection> connections, CancellationToken cancellationToken)
    {
        var active = connections.Where(connection => connection.IsReady && UsesHostKeypadMapping(connection))
            .ToDictionary(connection => connection.InstanceId, StringComparer.OrdinalIgnoreCase);
        await _keypadGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            foreach (var pair in _keypadSessions.ToArray())
            {
                var keep = active.TryGetValue(pair.Key, out var connection) &&
                    string.Equals(pair.Value.DevicePath, connection.DevicePath, StringComparison.OrdinalIgnoreCase);
                if (keep)
                {
                    try { pair.Value.RequireRunning(); }
                    catch (InvalidOperationException) { keep = false; }
                }
                if (keep) continue;
                _keypadSessions.Remove(pair.Key);
                try { await pair.Value.DisposeAsync().ConfigureAwait(false); }
                catch (Exception exception) { WriteDiagnostic(pair.Value.Runtime.ProductId, $"keypad disconnect cleanup: {exception.Message}"); }
            }
        }
        finally { _keypadGate.Release(); }

        foreach (var connection in active.Values)
        {
            if (KeypadMappingProfileResolver?.Invoke(connection).Count is not > 0) continue;
            try { await GetKeypadRuntimeAsync(connection, cancellationToken).ConfigureAwait(false); }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                WriteDiagnostic(connection.Definition.ProductId, $"keypad mapping unavailable: {exception.Message}");
            }
        }
    }

    internal async Task StopKeypadMappingsAsync()
    {
        await _keypadGate.WaitAsync().ConfigureAwait(false);
        try { _keypadStopped = true; }
        finally { _keypadGate.Release(); }
        await SyncKeypadMappingsAsync([], CancellationToken.None).ConfigureAwait(false);
    }
}
