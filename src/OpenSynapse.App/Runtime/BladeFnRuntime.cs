using System.Text.Json.Nodes;
using System.Threading.Channels;
using OpenSynapse.Core.Storage;
using OpenSynapse.Windows.Devices;
using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.App.Runtime;

/// <summary>
/// The Razer filter driver for this Blade model is absent or not bound to this device, so Fn
/// remapping cannot work here at all. Retrying cannot change that, so callers must treat
/// this as unsupported rather than as a transient failure.
/// </summary>
internal sealed class BladeFilterEndpointMissingException(string message) : InvalidOperationException(message);

internal sealed class BladeFnRuntime : IAsyncDisposable
{
    private static readonly TimeSpan RecoveryReadyTimeout = TimeSpan.FromSeconds(5);
    private static readonly string RecoveryHostPath = Path.Combine(
        AppContext.BaseDirectory,
        "OpenSynapse.RecoveryHost.exe");
    private static readonly string RecoveryMarkerPath =
        AppDataPaths.Combine("blade-mapping-session.json");

    private readonly IRazerFeatureTransport _transport;
    private readonly string _featureDevicePath;
    private readonly ushort _productId;
    private readonly BladeSoftwareModeCoordinator _modeCoordinator;
    private readonly Func<BladeMappingAction, CancellationToken, ValueTask> _leafExecutor;
    private readonly Action<BladeMappingInputEvent> _inputObserver;
    private readonly Action<string>? _diagnostic;
    private readonly HashSet<(int Code, bool IsDown)> _diagnosticRazerKeys = [];
    private readonly string _mappingPreset;
    private readonly bool _initialSnapTapEnabled;
    private readonly Action<bool> _snapTapChanged;
    private readonly Channel<BladeMappingInputEvent> _queue =
        Channel.CreateUnbounded<BladeMappingInputEvent>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false,
        });
    private readonly CancellationTokenSource _stop = new();
    private IRazerFeatureSession? _session;
    private BladeSoftwareModeCoordinator.BladeSoftwareModeLease? _lease;
    private BladeRecoveryClient? _recovery;
    private BladeMappingInputRuntime? _mapping;
    private BladeMappingActionExecutor? _executor;
    private RazerFilterInputHost? _filter;
    private Task? _inputConsumer;
    private Task? _consumer;
    private int _started;
    private int _disposed;

    internal BladeFnRuntime(
        IRazerFeatureTransport transport,
        string featureDevicePath,
        ushort productId,
        BladeSoftwareModeCoordinator modeCoordinator,
        Func<BladeMappingAction, CancellationToken, ValueTask> leafExecutor,
        Action<BladeMappingInputEvent> inputObserver,
        string mappingPreset,
        bool initialSnapTapEnabled,
        Action<bool> snapTapChanged,
        Action<string>? diagnostic = null)
    {
        _transport = transport ?? throw new ArgumentNullException(nameof(transport));
        ArgumentException.ThrowIfNullOrWhiteSpace(featureDevicePath);
        _featureDevicePath = featureDevicePath;
        _productId = productId;
        _modeCoordinator = modeCoordinator ?? throw new ArgumentNullException(nameof(modeCoordinator));
        _leafExecutor = leafExecutor ?? throw new ArgumentNullException(nameof(leafExecutor));
        _inputObserver = inputObserver ?? throw new ArgumentNullException(nameof(inputObserver));
        _diagnostic = diagnostic;
        ArgumentException.ThrowIfNullOrWhiteSpace(mappingPreset);
        _mappingPreset = mappingPreset;
        _initialSnapTapEnabled = initialSnapTapEnabled;
        _snapTapChanged = snapTapChanged ?? throw new ArgumentNullException(nameof(snapTapChanged));
    }

    internal Task Completion => _consumer ?? Task.CompletedTask;

    /// <summary>The filter runtime only runs for a model whose key graph we ship.</summary>
    internal static bool HasMapping(ushort productId) => File.Exists(MappingPathFor(productId));

    private void WriteDiagnostic(string message)
    {
        try
        {
            _diagnostic?.Invoke(message);
        }
        catch
        {
            // Diagnostics must never interfere with input routing.
        }
    }

    private void LogDkmTable(string filterDevicePath)
    {
        if (_diagnostic is null)
        {
            return;
        }

        if (!RazerDkmTableReader.TryRead(filterDevicePath, out var table, out var error))
        {
            WriteDiagnostic($"dkm table unavailable: {error}");
            return;
        }

        var nonZero = 0;
        foreach (var value in table)
        {
            if (value != 0)
            {
                nonZero++;
            }
        }

        WriteDiagnostic(
            $"dkm table readable; nonZero={nonZero}; head={Convert.ToHexString(table.AsSpan(0, 16))}");
    }

    internal async Task StartAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        if (Interlocked.Exchange(ref _started, 1) != 0)
        {
            throw new InvalidOperationException("Blade Fn runtime is already started.");
        }

        try
        {
            var filterDevicePath = await RazerFilterEndpointDiscovery
                .DiscoverProduct710ForFeatureAsync(
                    _featureDevicePath,
                    _productId,
                    cancellationToken)
                .ConfigureAwait(false)
                ?? throw new BladeFilterEndpointMissingException(
                    $"Could not uniquely identify the RZCONTROL endpoint for PID 0x{_productId:X4}.");

            LogDkmTable(filterDevicePath);

            _mapping = await LoadMappingAsync(_productId, _mappingPreset, cancellationToken)
                .ConfigureAwait(false);
            var hooks = _mapping.Rules
                .Where(static rule => rule.InputKind == BladeMappingInputKind.Keyboard)
                .Select(static rule => (
                    ScanCode: checked((ushort)rule.InputCode),
                    Flag: (ushort)(rule.InputExtended ? 2 : 0)))
                .Distinct()
                .OrderBy(static hook => hook.ScanCode)
                .ToArray();
            _session = await _transport
                .OpenSessionAsync(_featureDevicePath, cancellationToken)
                .ConfigureAwait(false);
            await BladeAudioMuteRuntime
                .InitializeSessionAsync(_session, cancellationToken)
                .ConfigureAwait(false);

            _recovery = await BladeRecoveryClient.StartAsync(
                    RecoveryHostPath,
                    RecoveryMarkerPath,
                    _featureDevicePath,
                    filterDevicePath,
                    hooks,
                    RecoveryReadyTimeout,
                    cancellationToken)
                .ConfigureAwait(false);
            _executor = new BladeMappingActionExecutor(
                new WindowsKeyboardInputSink(),
                _leafExecutor,
                UpdateRecoveryKeys);
            _executor.SendRuntimeOutputs(_mapping.SetSnapTapEnabled(_initialSnapTapEnabled));
            _filter = new RazerFilterInputHost(
                filterDevicePath,
                hooks,
                input =>
                {
                    if (!_queue.Writer.TryWrite(input))
                    {
                        throw new InvalidOperationException("Blade Fn input queue is closed.");
                    }
                });

            var inputConsumer = ConsumeAsync(_mapping, _executor, _filter, _stop.Token);
            _inputConsumer = inputConsumer;
            if (_recovery.Completion.IsCompleted)
            {
                throw new InvalidOperationException("RecoveryHost stopped before input redirect was enabled.");
            }
            await _filter.StartAsync(cancellationToken).ConfigureAwait(false);
            _consumer = SuperviseAsync(
                inputConsumer,
                _filter,
                _mapping,
                _executor,
                _recovery,
                _stop.Token);
            _lease = await _modeCoordinator.AcquireAsync(
                    _featureDevicePath,
                    token => BladeAudioMuteRuntime.SetDeviceModeAsync(
                        _session,
                        softwareMode: true,
                        token),
                    () => BladeAudioMuteRuntime.SetDeviceModeAsync(
                        _session,
                        softwareMode: false,
                        CancellationToken.None),
                    cancellationToken)
                .ConfigureAwait(false);

            if (_consumer.IsCompleted)
            {
                await _consumer.ConfigureAwait(false);
                throw new InvalidOperationException("Blade Fn input consumer stopped during startup.");
            }
        }
        catch (Exception startupError)
        {
            try
            {
                await DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception cleanupError)
            {
                throw new AggregateException(startupError, cleanupError);
            }

            throw;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        var errors = new List<Exception>();
        _stop.Cancel();
        var filter = Interlocked.Exchange(ref _filter, null);
        var filterStop = filter?.DisposeAsync().AsTask();
        if (filterStop is not null)
        {
            await TryAsync(() => new ValueTask(filterStop), errors).ConfigureAwait(false);
        }
        _queue.Writer.TryComplete();
        var consumer = Interlocked.Exchange(ref _consumer, null);
        if (consumer is not null)
        {
            await TryAsync(() => new ValueTask(consumer), errors).ConfigureAwait(false);
        }
        var inputConsumer = Interlocked.Exchange(ref _inputConsumer, null);
        if (inputConsumer is not null && !ReferenceEquals(inputConsumer, consumer))
        {
            await TryAsync(() => new ValueTask(inputConsumer), errors).ConfigureAwait(false);
        }

        var mapping = Interlocked.Exchange(ref _mapping, null);
        var executor = Interlocked.Exchange(ref _executor, null);
        if (executor is not null)
        {
            await TryAsync(executor.DisposeAsync, errors).ConfigureAwait(false);
        }
        mapping?.Dispose();

        var session = Interlocked.Exchange(ref _session, null);
        var lease = Interlocked.Exchange(ref _lease, null);
        if (session is not null && lease is not null)
        {
            await TryAsync(
                () => lease.ReleaseAsync(() => BladeAudioMuteRuntime.SetDeviceModeAsync(
                    session,
                    softwareMode: false,
                    CancellationToken.None)),
                errors).ConfigureAwait(false);
        }
        if (session is not null)
        {
            await TryAsync(session.DisposeAsync, errors).ConfigureAwait(false);
        }

        var recovery = Interlocked.Exchange(ref _recovery, null);
        if (recovery is not null)
        {
            if (errors.Count == 0)
            {
                await TryAsync(
                    () => new ValueTask(recovery.CompleteNormalShutdownAsync()),
                    errors).ConfigureAwait(false);
            }
            else
            {
                await TryAsync(
                    () => new ValueTask(recovery.RecoverAndShutdownAsync()),
                    errors).ConfigureAwait(false);
            }
            await TryAsync(recovery.DisposeAsync, errors).ConfigureAwait(false);
        }

        _stop.Dispose();
        if (errors.Count != 0)
        {
            throw new AggregateException("Blade Fn runtime cleanup was incomplete.", errors);
        }
    }

    private async Task ConsumeAsync(
        BladeMappingInputRuntime mapping,
        BladeMappingActionExecutor executor,
        RazerFilterInputHost filter,
        CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var input in _queue.Reader
                               .ReadAllAsync(cancellationToken)
                               .ConfigureAwait(false))
            {
                _inputObserver(input);
                var snapTapBefore = mapping.SnapTapEnabled;
                var outputs = mapping.Process(input, out var action, out var passThrough);
                if (input.Kind == BladeMappingInputKind.RazerKey &&
                    _diagnostic is not null &&
                    _diagnosticRazerKeys.Add((input.Code, input.IsDown)))
                {
                    var detail = action switch
                    {
                        BladeCommandMappingAction command =>
                            $" -> {command.CommandKind}:{command.Command}",
                        BladeAudioMappingAction audio =>
                            $" -> audio:{audio.Command}:{audio.Mute}",
                        BladeBacklightMappingAction backlight =>
                            $" -> backlight:{backlight.Command}",
                        _ when outputs.Count > 0 => $" -> keys:{outputs.Count}",
                        _ => string.Empty,
                    };
                    WriteDiagnostic(
                        $"razerKey {input.Code} (0x{input.Code:X2}) " +
                        $"{(input.IsDown ? "down" : "up")}{detail}");
                }
                if (passThrough)
                {
                    filter.SendKeyboardInput(input);
                    continue;
                }
                executor.SendRuntimeOutputs(outputs);
                if (snapTapBefore != mapping.SnapTapEnabled)
                {
                    _snapTapChanged(mapping.SnapTapEnabled);
                }
                if (action is not null)
                {
                    if (action is BladeCommandMappingAction
                        {
                            Kind: BladeMappingOutputKind.Display,
                        } display)
                    {
                        try
                        {
                            filter.SendConsumerUsage(display.Command switch
                            {
                                BladeMappingCommand.DriverBrightnessDown => 0x70,
                                BladeMappingCommand.DriverBrightnessUp => 0x6F,
                                BladeMappingCommand.DriverBrightnessStop => 0,
                                _ => throw new InvalidOperationException(
                                    $"Unsupported display command: {display.Command}."),
                            });
                        }
                        catch (System.ComponentModel.Win32Exception)
                        {
                        }
                        catch (IOException)
                        {
                        }
                    }
                    else if (action is BladeCommandMappingAction or
                        BladeBacklightMappingAction or
                        BladeAudioMappingAction)
                    {
                        executor.QueueLeafAction(input, action);
                    }
                    else
                    {
                        await executor.ExecuteAsync(input, action, cancellationToken)
                            .ConfigureAwait(false);
                    }
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private async Task SuperviseAsync(
        Task inputConsumer,
        RazerFilterInputHost filter,
        BladeMappingInputRuntime mapping,
        BladeMappingActionExecutor executor,
        BladeRecoveryClient recovery,
        CancellationToken cancellationToken)
    {
        Exception? failure = null;
        try
        {
            var completed = await Task.WhenAny(
                    inputConsumer,
                    filter.Completion,
                    executor.Completion,
                    recovery.Completion)
                .ConfigureAwait(false);
            var component = ReferenceEquals(completed, inputConsumer)
                ? "input consumer"
                : ReferenceEquals(completed, filter.Completion)
                    ? "filter input"
                    : ReferenceEquals(completed, executor.Completion)
                        ? "mapping executor"
                        : "RecoveryHost";
            try
            {
                await completed.ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                throw new InvalidOperationException($"Blade Fn {component} failed.", exception);
            }
            if (!cancellationToken.IsCancellationRequested)
            {
                throw new InvalidOperationException($"Blade Fn {component} stopped unexpectedly.");
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            failure = exception;
        }

        if (failure is null)
        {
            try
            {
                await Task.WhenAll(inputConsumer, filter.Completion).ConfigureAwait(false);
                return;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        }

        var errors = new List<Exception> { failure };
        _stop.Cancel();
        _queue.Writer.TryComplete(failure);
        await TryAsync(filter.DisposeAsync, errors).ConfigureAwait(false);
        await TryAsync(() => new ValueTask(inputConsumer), errors).ConfigureAwait(false);
        Try(() => executor.SendRuntimeOutputs(mapping.Stop()), errors);
        await TryAsync(
            () => new ValueTask(executor.StopAsync()),
            errors).ConfigureAwait(false);
        throw new AggregateException("Blade Fn input failed closed.", errors);
    }

    private void UpdateRecoveryKeys(IReadOnlyList<BladeMappingOutputEvent> outputs)
    {
        var keys = outputs
            .Where(static output => output.IsDown)
            .Select(static output => new BladeRecoverySyntheticKey(
                checked((ushort)output.ScanCode),
                output.Extended))
            .ToArray();
        (_recovery ?? throw new InvalidOperationException("RecoveryHost is not ready."))
            .UpdateSyntheticKeys(keys);
    }

    private static async Task<BladeMappingInputRuntime> LoadMappingAsync(
        ushort productId,
        string mappingPreset,
        CancellationToken cancellationToken)
    {
        if (!StringComparer.Ordinal.Equals(
                mappingPreset,
                OpenSynapse.Core.Profiles.BladeProfileSettings.Product710DefaultMappingPreset))
        {
            throw new InvalidDataException($"Unsupported Blade mapping preset: {mappingPreset}.");
        }
        var json = await File.ReadAllTextAsync(MappingPathFor(productId), cancellationToken)
            .ConfigureAwait(false);
        var root = JsonNode.Parse(json) as JsonObject
            ?? throw new InvalidDataException($"Product {productId} mapping root is invalid.");
        var graph = root["defaultMappings"]?["appEngine"] as JsonObject
            ?? throw new InvalidDataException($"Product {productId} default mapping graph is missing.");
        return BladeMappingInputRuntime.FromGraph(graph);
    }

    private static string MappingPathFor(ushort productId) => Path.Combine(
        AppContext.BaseDirectory,
        "Native",
        "Razer",
        "Mappings",
        $"Product{productId}Mapping.json");

    private static void Try(Action action, ICollection<Exception> errors)
    {
        try
        {
            action();
        }
        catch (Exception exception)
        {
            errors.Add(exception);
        }
    }

    private static async ValueTask TryAsync(
        Func<ValueTask> action,
        ICollection<Exception> errors)
    {
        try
        {
            await action().ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            errors.Add(exception);
        }
    }
}
