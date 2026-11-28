using OpenSynapse.Core.Macros;
using OpenSynapse.Core.Storage;
using OpenSynapse.Windows.Input;
using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.Windows.Devices;

internal sealed class RazerKeypadMappingSession : IAsyncDisposable
{
    private static readonly uint[] MouseHooks = [0x00000030, 0x00780400, 0xFF880400];
    private readonly RazerFilterInputHost? _filter;
    private readonly RazerLegacyKeypadInputHost? _legacy;
    private readonly BladeRecoveryClient? _recovery;
    private readonly RazerFilterMacroInputSink? _sink;
    private readonly Action<string>? _log;
    private readonly object _disposeGate = new();
    private Task? _disposeTask;
    private volatile Exception? _failure;

    internal RazerKeypadMappingSession(string devicePath, RazerKeypadMappingRuntime runtime)
    {
        DevicePath = devicePath;
        Runtime = runtime;
    }

    private RazerKeypadMappingSession(
        string devicePath,
        RazerKeypadMappingRuntime runtime,
        RazerFilterInputHost? filter,
        BladeRecoveryClient recovery,
        RazerFilterMacroInputSink? sink,
        Action<string>? log,
        RazerLegacyKeypadInputHost? legacy = null) : this(devicePath, runtime)
    {
        _filter = filter;
        _legacy = legacy;
        _recovery = recovery;
        _sink = sink;
        _log = log;
    }

    internal string DevicePath { get; }
    internal RazerKeypadMappingRuntime Runtime { get; }

    internal void RequireRunning()
    {
        if (_failure is { } failure)
            throw new RazerKeypadMappingUnavailableException(RazerKeypadMappingUnavailableReason.Stopped,
                "Keypad input mapping stopped. Rescan the device to reconnect.", failure);
        if (_disposeTask is not null)
            throw new RazerKeypadMappingUnavailableException(RazerKeypadMappingUnavailableReason.Stopped,
                "Keypad input mapping is stopping.");
    }

    internal static async Task<RazerKeypadMappingSession> StartAsync(
        RazerHidDeviceConnection connection,
        IReadOnlyList<RazerHidButtonAssignment> assignments,
        Action<MacroInput, bool, bool> onInput,
        Action<string>? log,
        CancellationToken cancellationToken)
    {
        var featurePath = connection.DevicePath!;
        var keyboardHooks = RazerKeypadInputCatalog.GetKeyboardHooks(connection.Definition.ProductId);
        var mouseHooks = RazerKeypadInputCatalog.HasWheel(connection.Definition.ProductId) ? MouseHooks : [];
        var legacyControl = RazerKeypadInputCatalog.IsLegacy(connection.Definition.ProductId)
            ? await Task.Run(() => RazerLegacyKeypadControl.Discover(featurePath,
                connection.Definition.ProductId, cancellationToken), cancellationToken).ConfigureAwait(false)
            : null;
        var filterPath = legacyControl?.Identity.Path ??
            await RazerFilterEndpointDiscovery.DiscoverProduct710ForFeatureAsync(
                featurePath, connection.Definition.ProductId, cancellationToken).ConfigureAwait(false)
            ?? throw new RazerKeypadMappingUnavailableException(RazerKeypadMappingUnavailableReason.DriverMissing,
                "Keypad mapping requires the Razer device filter driver. No RZCONTROL endpoint matched this device instance.");
        var recoveryPath = Path.Combine(AppContext.BaseDirectory, "OpenSynapse.RecoveryHost.exe");
        if (!File.Exists(recoveryPath))
            throw new RazerKeypadMappingUnavailableException(RazerKeypadMappingUnavailableReason.RecoveryMissing,
                "OpenSynapse.RecoveryHost.exe is missing. Repair the OpenSynapse installation before enabling keypad mapping.");

        // A readable instance-specific file also lets the next launch recover a stale session.
        var markerName = "keypad-" + string.Concat(connection.InstanceId.Select(
            ch => char.IsAsciiLetterOrDigit(ch) ? ch : '_')) + ".json";
        var recovery = await BladeRecoveryClient.StartAsync(
            recoveryPath, AppDataPaths.Combine(markerName), featurePath, filterPath,
            keyboardHooks, TimeSpan.FromSeconds(5), cancellationToken,
            mouseHooks: mouseHooks, restoreDeviceMode: false,
            legacyKeypadIdentity: legacyControl?.Identity.Value ?? 0).ConfigureAwait(false);
        RazerKeypadMappingRuntime? runtime = null;
        RazerFilterMacroInputSink? sink = null;
        RazerKeypadMappingSession? session = null;
        try
        {
            if (legacyControl is null) sink = RazerFilterMacroInputSink.CreateForKeypad(filterPath, log);
            IMacroInputSink output = sink is not null ? sink : new WindowsMacroInputSink();
            runtime = new RazerKeypadMappingRuntime(
                connection.InstanceId, output, onInput,
                inputs => recovery.UpdateSyntheticKeys(inputs.Select(ToRecoveryKey).ToArray()),
                connection.Definition.ProductId);
            var filter = legacyControl is null ? new RazerFilterInputHost(filterPath, keyboardHooks,
                input => runtime.ProcessInputAsync(input).GetAwaiter().GetResult(), mouseHooks: mouseHooks) : null;
            var legacy = legacyControl is null ? null : new RazerLegacyKeypadInputHost(
                new WindowsRazerFilterDriverChannel(filterPath, legacyKeypad: true),
                legacyControl.Enable, legacyControl.Restore, async input =>
                {
                    if (!await runtime.ProcessInputAsync(input).ConfigureAwait(false))
                        throw new InvalidDataException("Unexpected legacy keypad input. Mapping stopped to restore normal input.");
                });
            session = new RazerKeypadMappingSession(featurePath, runtime, filter, recovery, sink, log, legacy);
            await runtime.ReplaceAssignmentsAsync(assignments, cancellationToken).ConfigureAwait(false);
            if (recovery.Completion.IsCompleted)
                throw new InvalidOperationException("Keypad recovery host stopped before input hooks were enabled.");
            if (legacy is not null) await legacy.StartAsync(cancellationToken).ConfigureAwait(false);
            else await filter!.StartAsync(cancellationToken).ConfigureAwait(false);
            _ = session.SuperviseAsync();
            session.RequireRunning();
            return session;
        }
        catch (Exception startupError)
        {
            try
            {
                if (session is not null)
                    await session.DisposeAsync().ConfigureAwait(false);
                else
                {
                    var cleanupErrors = new List<Exception>();
                    try { if (runtime is not null) await runtime.DisposeAsync().ConfigureAwait(false); }
                    catch (Exception exception) { cleanupErrors.Add(exception); }
                    try { if (sink is not null) await sink.DisposeAsync().ConfigureAwait(false); }
                    catch (Exception exception) { cleanupErrors.Add(exception); }
                    try { await recovery.RecoverAndShutdownAsync().ConfigureAwait(false); }
                    catch (Exception exception) { cleanupErrors.Add(exception); }
                    finally { await recovery.DisposeAsync().ConfigureAwait(false); }
                    if (cleanupErrors.Count > 0) throw new AggregateException(cleanupErrors);
                }
            }
            catch (Exception cleanupError) { throw new AggregateException(startupError, cleanupError); }
            throw;
        }
    }

    internal static BladeRecoverySyntheticKey ToRecoveryKey(MacroInput input) =>
        input.Kind == MacroInputKind.MouseButton
            ? new(checked((ushort)input.MouseButton), false, IsMouseButton: true)
            : input.ScanCode != 0
                ? new(checked((ushort)input.ScanCode), input.Extended)
                : new(0, input.Extended, VirtualKey: checked((ushort)input.VirtualKey));

    private async Task SuperviseAsync()
    {
        await Task.WhenAny(_legacy?.Completion ?? _filter!.Completion, _recovery!.Completion).ConfigureAwait(false);
        lock (_disposeGate)
        {
            if (_disposeTask is not null) return;
            _failure = new IOException("The keypad input or recovery channel closed unexpectedly.");
        }
        _log?.Invoke(_failure.Message);
        try { await DisposeAsync().ConfigureAwait(false); }
        catch (Exception exception) { _log?.Invoke($"keypad cleanup failed: {exception.Message}"); }
    }

    public ValueTask DisposeAsync()
    {
        lock (_disposeGate)
            return new ValueTask(_disposeTask ??= DisposeCoreAsync());
    }

    private async Task DisposeCoreAsync()
    {
        var errors = new List<Exception>();
        if (_legacy is not null)
        {
            try { await _legacy.DisposeAsync().ConfigureAwait(false); }
            catch (Exception exception) { errors.Add(exception); }
        }
        if (_filter is not null)
        {
            try { await _filter.DisposeAsync().ConfigureAwait(false); }
            catch (Exception exception) { errors.Add(exception); }
        }
        try { await Runtime.DisposeAsync().ConfigureAwait(false); }
        catch (Exception exception) { errors.Add(exception); }
        if (_sink is not null)
        {
            try { await _sink.DisposeAsync().ConfigureAwait(false); }
            catch (Exception exception) { errors.Add(exception); }
        }
        if (_recovery is not null)
        {
            try
            {
                if (errors.Count == 0)
                    await _recovery.CompleteNormalShutdownAsync().ConfigureAwait(false);
                else
                    await _recovery.RecoverAndShutdownAsync().ConfigureAwait(false);
            }
            catch (Exception exception) { errors.Add(exception); }
            finally { await _recovery.DisposeAsync().ConfigureAwait(false); }
        }
        if (errors.Count > 0) throw new AggregateException("Keypad mapping cleanup failed.", errors);
    }
}
