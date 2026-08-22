using System.Text.Json.Nodes;
using System.Threading.Channels;
using OpenSynapse.Windows.Devices;
using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.App.Runtime;

internal sealed class BladeFnRuntime : IAsyncDisposable
{
    private static readonly TimeSpan RecoveryReadyTimeout = TimeSpan.FromSeconds(5);
    private static readonly string MappingPath = Path.Combine(
        AppContext.BaseDirectory,
        "Native",
        "Razer",
        "Product710Mapping.json");
    private static readonly string RecoveryHostPath = Path.Combine(
        AppContext.BaseDirectory,
        "OpenSynapse.RecoveryHost.exe");
    private static readonly string RecoveryMarkerPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "OpenSynapse",
        "blade-mapping-session.json");

    private readonly IRazerFeatureTransport _transport;
    private readonly string _featureDevicePath;
    private readonly BladeSoftwareModeCoordinator _modeCoordinator;
    private readonly Func<BladeMappingAction, CancellationToken, ValueTask> _leafExecutor;
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
        BladeSoftwareModeCoordinator modeCoordinator,
        Func<BladeMappingAction, CancellationToken, ValueTask> leafExecutor,
        string mappingPreset,
        bool initialSnapTapEnabled,
        Action<bool> snapTapChanged)
    {
        _transport = transport ?? throw new ArgumentNullException(nameof(transport));
        ArgumentException.ThrowIfNullOrWhiteSpace(featureDevicePath);
        _featureDevicePath = featureDevicePath;
        _modeCoordinator = modeCoordinator ?? throw new ArgumentNullException(nameof(modeCoordinator));
        _leafExecutor = leafExecutor ?? throw new ArgumentNullException(nameof(leafExecutor));
        ArgumentException.ThrowIfNullOrWhiteSpace(mappingPreset);
        _mappingPreset = mappingPreset;
        _initialSnapTapEnabled = initialSnapTapEnabled;
        _snapTapChanged = snapTapChanged ?? throw new ArgumentNullException(nameof(snapTapChanged));
    }

    internal Task Completion => _consumer ?? Task.CompletedTask;

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
                    cancellationToken)
                .ConfigureAwait(false)
                ?? throw new InvalidOperationException(
                    "Could not uniquely identify the Product 710 RZCONTROL endpoint.");

            _mapping = await LoadMappingAsync(_mappingPreset, cancellationToken).ConfigureAwait(false);
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
        if (mapping is not null && executor is not null)
        {
            Try(() => executor.SendRuntimeOutputs(mapping.Stop()), errors);
        }
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
                var snapTapBefore = mapping.SnapTapEnabled;
                var outputs = mapping.Process(input, out var action);
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
                        filter.SendConsumerUsage(display.Command switch
                        {
                            BladeMappingCommand.DriverBrightnessDown => 0x70,
                            BladeMappingCommand.DriverBrightnessUp => 0x6F,
                            BladeMappingCommand.DriverBrightnessStop => 0,
                            _ => throw new InvalidOperationException(
                                $"Unsupported display command: {display.Command}."),
                        });
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
            await completed.ConfigureAwait(false);
            if (!cancellationToken.IsCancellationRequested)
            {
                throw new InvalidOperationException("Blade Fn input pipeline stopped unexpectedly.");
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
        string mappingPreset,
        CancellationToken cancellationToken)
    {
        if (!StringComparer.Ordinal.Equals(
                mappingPreset,
                OpenSynapse.Core.Profiles.BladeProfileSettings.Product710DefaultMappingPreset))
        {
            throw new InvalidDataException($"Unsupported Blade mapping preset: {mappingPreset}.");
        }
        var json = await File.ReadAllTextAsync(MappingPath, cancellationToken)
            .ConfigureAwait(false);
        var root = JsonNode.Parse(json) as JsonObject
            ?? throw new InvalidDataException("Product 710 mapping root is invalid.");
        var graph = root["defaultMappings"]?["appEngine"] as JsonObject
            ?? throw new InvalidDataException("Product 710 default mapping graph is missing.");
        return BladeMappingInputRuntime.FromProduct710Graph(graph);
    }

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
