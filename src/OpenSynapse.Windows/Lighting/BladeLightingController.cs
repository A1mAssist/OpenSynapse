using System.ComponentModel;
using OpenSynapse.Core.Devices;
using OpenSynapse.Windows.Devices;
using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.Windows.Lighting;

public enum BladeLightingMode
{
    Off,
    Static,
    Breathing,
    Spectrum,
    Wave,
    Fire,
    Reactive,
    Ripple,
    AudioMeter,
    Ambient,
    Wheel,
    Starlight,
    Tidal,
    ChromaFile,
}

public enum BladeStarlightColorMode
{
    Random,
    Single,
    Dual,
}

public sealed record BladeLightingEffect(
    BladeLightingMode Mode,
    RazerRgb Color = default,
    BladeWaveDirection Direction = BladeWaveDirection.Right,
    RazerRgb SecondColor = default,
    byte ReactiveSpeed = 2,
    byte StarlightSpeed = 2,
    BladeStarlightColorMode StarlightColorMode = BladeStarlightColorMode.Single,
    LightingPalette? Palette = null,
    byte AnimationSpeed = 2,
    ChromaFileAnimation? ChromaAnimation = null)
{
    public static BladeLightingEffect Off { get; } = new(BladeLightingMode.Off);
    public static BladeLightingEffect Spectrum { get; } = new(BladeLightingMode.Spectrum);
    public static BladeLightingEffect Fire { get; } = new(BladeLightingMode.Fire);
}

public interface IBladeLightingController : IAsyncDisposable
{
    Task RuntimeCompletion { get; }

    Task ApplyAsync(
        IReadOnlyList<DeviceDescriptor> devices,
        BladeLightingEffect effect,
        CancellationToken cancellationToken = default);

    Task StopAsync();

    Task SetDisplayAvailableAsync(bool available);

    Task ApplyExternalAsync(
        IReadOnlyList<DeviceDescriptor> devices,
        ISoftwareLightingFrameSource source,
        bool restorePersistentEffect = true,
        CancellationToken cancellationToken = default);
}

public sealed class BladeLightingController : IBladeLightingController
{
    // The matrix path is seven feature reports per frame. Keep only the latest
    // frame if the device cannot sustain this target; never build a stale queue.
    private static readonly TimeSpan FrameInterval = TimeSpan.FromMilliseconds(1000d / 60d);
    // Stay well below the observed sub-30-second firmware idle window without becoming a frame pump.
    private static readonly TimeSpan NativeEffectKeepAliveInterval = TimeSpan.FromSeconds(5);
    private static readonly RazerRgb DefaultRestoreColor = new(0x99, 0xDD, 0x72);
    private static readonly TimeSpan MatrixWait = TimeSpan.FromMilliseconds(1);

    private readonly IRazerFeatureTransport _transport;
    private readonly RazerDeviceRegistry _registry;
    private readonly RazerRgb[] _restoreFrame;
    private readonly BladeSoftwareModeCoordinator _modeCoordinator;
    private readonly TimeSpan _nativeEffectKeepAliveInterval;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private SoftwareLightingRuntime? _runtime;
    private ISoftwareLightingFrameSource? _externalSource;
    private WindowsKeyboardLightingAdapter? _keyboardInput;
    private BladeSoftwareModeCoordinator.BladeSoftwareModeLease? _modeLease;
    private Task _runtimeCompletion = Task.CompletedTask;
    private byte _transactionId;
    private bool _nativeEffectActive;
    private string? _nativeEffectDevicePath;
    private CancellationTokenSource? _nativeEffectKeepAliveCancellation;
    private Task _nativeEffectKeepAliveTask = Task.CompletedTask;
    private int _turnOffOnStop;
    private int _displayAvailable = 1;
    private BladeLightingDialect _lightingDialect;
    private int _matrixRows = BladeLightingProtocol.Rows;
    private int _matrixColumns = BladeLightingProtocol.Columns;
    private int _disposed;

    public BladeLightingController()
        : this(new RazerFeatureTransport())
    {
    }

    public BladeLightingController(
        IRazerFeatureTransport transport,
        RazerRgb? restoreColor = null)
        : this(transport, RazerDeviceRegistry.BuiltIn, restoreColor ?? DefaultRestoreColor)
    {
    }

    internal BladeLightingController(
        IRazerFeatureTransport transport,
        RazerDeviceRegistry registry)
        : this(transport, registry, DefaultRestoreColor)
    {
    }

    internal BladeLightingController(
        IRazerFeatureTransport transport,
        RazerDeviceRegistry registry,
        BladeSoftwareModeCoordinator modeCoordinator)
        : this(transport, registry, DefaultRestoreColor, modeCoordinator)
    {
    }

    internal BladeLightingController(
        IRazerFeatureTransport transport,
        RazerDeviceRegistry registry,
        BladeSoftwareModeCoordinator modeCoordinator,
        TimeSpan nativeEffectKeepAliveInterval)
        : this(transport, registry, DefaultRestoreColor, modeCoordinator, nativeEffectKeepAliveInterval)
    {
    }

    internal BladeLightingController(
        IRazerFeatureTransport transport,
        RazerDeviceRegistry registry,
        RazerRgb restoreColor,
        BladeSoftwareModeCoordinator? modeCoordinator = null,
        TimeSpan? nativeEffectKeepAliveInterval = null)
    {
        _transport = transport ?? throw new ArgumentNullException(nameof(transport));
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _restoreFrame = QuickLightingEngine.RenderSolid(restoreColor);
        _modeCoordinator = modeCoordinator ?? new BladeSoftwareModeCoordinator();
        _nativeEffectKeepAliveInterval = nativeEffectKeepAliveInterval ?? NativeEffectKeepAliveInterval;
    }

    public async Task ApplyAsync(
        IReadOnlyList<DeviceDescriptor> devices,
        BladeLightingEffect effect,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(devices);
        Validate(effect);
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
            if (Volatile.Read(ref _displayAvailable) == 0 && effect.Mode != BladeLightingMode.Off)
            {
                return;
            }
            var (device, manifest) = FindReadyBlade(devices);
            await ValidateCurrentPathAsync(device.Id, manifest, cancellationToken).ConfigureAwait(false);
            await StopCoreAsync().ConfigureAwait(false);
            _transactionId = 0;
            if (_lightingDialect != BladeLightingDialect.LedMatrixBlade)
            {
                _modeLease = await _modeCoordinator.AcquireAsync(
                    device.Id,
                    token => SendAsync(
                        device.Id,
                        BladeDeviceModeProtocol.CreateSetSoftwareRequest(NextTransactionId()),
                        token),
                    () => SendAsync(
                        device.Id,
                        BladeDeviceModeProtocol.CreateSetNormalRequest(NextTransactionId()),
                        CancellationToken.None),
                    cancellationToken).ConfigureAwait(false);
            }
            try
            {
                await SendLightingEngineGateAsync(device.Id, cancellationToken).ConfigureAwait(false);
                if (TryCreateNativeEffectRequest(effect, out var nativeRequest))
                {
                    try
                    {
                        await SendAsync(
                            device.Id,
                            nativeRequest,
                            cancellationToken).ConfigureAwait(false);
                    }
                    catch (Exception exception) when (
                        (_lightingDialect is BladeLightingDialect.LedMatrixBlade or BladeLightingDialect.FirmwareEffect) &&
                        BladeLightingRuntimeDialect.IsExplicitRejection(exception))
                    {
                        BladeLightingRuntimeDialect.Downgrade(device.ProductId, exception.Message);
                        throw;
                    }
                    if (effect.Mode == BladeLightingMode.Off)
                    {
                        await ReleaseModeLeaseAsync(CancellationToken.None).ConfigureAwait(false);
                        _runtimeCompletion = Task.CompletedTask;
                        return;
                    }
                    _nativeEffectActive = true;
                    _nativeEffectDevicePath = device.Id;
                    _nativeEffectKeepAliveCancellation = new CancellationTokenSource();
                    _nativeEffectKeepAliveTask = RunNativeEffectKeepAliveAsync(
                        device.Id,
                        manifest,
                        _nativeEffectKeepAliveCancellation.Token);
                    _runtimeCompletion = _nativeEffectKeepAliveTask;
                    return;
                }
                var pump = new BladeMatrixFramePump(
                    _transport,
                    device.Id,
                    token => RestoreAsync(device.Id, token),
                    _matrixRows,
                    _matrixColumns,
                    CreateFrameRequestBuilder());
                ILightingInputAdapter? inputAdapter = null;
                ISoftwareLightingFrameSource source;
                if (effect.Mode is BladeLightingMode.Reactive or BladeLightingMode.Ripple)
                {
                    var keyboardInput = new WindowsKeyboardLightingAdapter();
                    inputAdapter = keyboardInput;
                    source = new KeyboardEffectFrameSource(effect, keyboardInput);
                }
                else if (effect.Mode == BladeLightingMode.AudioMeter)
                {
                    var audioInput = new WasapiAudioMeterAdapter();
                    inputAdapter = audioInput;
                    source = new AudioMeterFrameSource(audioInput, effect.Palette);
                }
                else if (effect.Mode == BladeLightingMode.Ambient)
                {
                    var displayInput = new WindowsDisplayCaptureAdapter();
                    inputAdapter = displayInput;
                    source = new AmbientFrameSource(displayInput);
                }
                else if (effect.Mode == BladeLightingMode.ChromaFile)
                {
                    source = new ChromaFileFrameSource(
                        effect.ChromaAnimation ??
                        throw new InvalidOperationException("The Chroma animation is missing."));
                }
                else
                {
                    source = new EffectFrameSource(effect);
                }
                await StartRuntimeAsync(pump, source, inputAdapter, cancellationToken).ConfigureAwait(false);
                _keyboardInput = inputAdapter as WindowsKeyboardLightingAdapter;
            }
            catch (Exception exception)
            {
                if ((_lightingDialect is BladeLightingDialect.LedMatrixBlade or BladeLightingDialect.FirmwareEffect) &&
                    BladeLightingRuntimeDialect.IsExplicitRejection(exception))
                {
                    BladeLightingRuntimeDialect.Downgrade(device.ProductId, exception.Message);
                }
                try
                {
                    await ReleaseModeLeaseAsync(CancellationToken.None).ConfigureAwait(false);
                }
                catch
                {
                    // Preserve the takeover failure as the primary error.
                }
                throw;
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task ApplyExternalAsync(
        IReadOnlyList<DeviceDescriptor> devices,
        ISoftwareLightingFrameSource source,
        bool restorePersistentEffect = true,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(devices);
        ArgumentNullException.ThrowIfNull(source);
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (Volatile.Read(ref _displayAvailable) == 0)
            {
                return;
            }
            if (ReferenceEquals(_externalSource, source) &&
                _runtime is not null &&
                !_runtimeCompletion.IsCompleted)
            {
                return;
            }

            var (device, manifest) = FindReadyBlade(devices);
            await ValidateCurrentPathAsync(device.Id, manifest, cancellationToken).ConfigureAwait(false);
            await StopCoreAsync().ConfigureAwait(false);
            _transactionId = 0;
            if (_lightingDialect != BladeLightingDialect.LedMatrixBlade)
            {
                _modeLease = await _modeCoordinator.AcquireAsync(
                    device.Id,
                    token => SendAsync(device.Id,
                        BladeDeviceModeProtocol.CreateSetSoftwareRequest(NextTransactionId()), token),
                    () => SendAsync(device.Id,
                        BladeDeviceModeProtocol.CreateSetNormalRequest(NextTransactionId()),
                        CancellationToken.None),
                    cancellationToken).ConfigureAwait(false);
            }
            try
            {
                await SendLightingEngineGateAsync(device.Id, cancellationToken).ConfigureAwait(false);
                var pump = new BladeMatrixFramePump(
                    _transport,
                    device.Id,
                    restorePersistentEffect
                        ? token => RestoreAsync(device.Id, token)
                        : token => ReleaseModeLeaseAsync(CancellationToken.None),
                    _matrixRows,
                    _matrixColumns,
                    CreateFrameRequestBuilder());
                await StartRuntimeAsync(pump, source, null, cancellationToken).ConfigureAwait(false);
                _externalSource = source;
            }
            catch
            {
                try
                {
                    await ReleaseModeLeaseAsync(CancellationToken.None).ConfigureAwait(false);
                }
                catch
                {
                    // Preserve the takeover failure as the primary error.
                }
                throw;
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public Task RuntimeCompletion => _runtimeCompletion;

    public async Task SetDisplayAvailableAsync(bool available)
    {
        Volatile.Write(ref _displayAvailable, available ? 1 : 0);
        if (available)
        {
            return;
        }

        CancelNativeEffectKeepAlive();
        Interlocked.Exchange(ref _turnOffOnStop, 1);
        try
        {
            await _gate.WaitAsync().ConfigureAwait(false);
            try
            {
                var modeDevicePath = _modeLease?.DevicePath;
                var runtime = _runtime;
                _runtime = null;
                _externalSource = null;
                _keyboardInput = null;
                try
                {
                    if (runtime is not null)
                    {
                        try
                        {
                            await runtime.PrepareForSuspendAsync().ConfigureAwait(false);
                        }
                        finally
                        {
                            await runtime.DisposeAsync().ConfigureAwait(false);
                        }

                        if (modeDevicePath is not null)
                        {
                            await SendAsync(
                                modeDevicePath,
                                CreateOffRequest(),
                                CancellationToken.None).ConfigureAwait(false);
                        }
                    }
                    if (_nativeEffectActive && _nativeEffectDevicePath is not null)
                    {
                        CancelNativeEffectKeepAlive();
                        try
                        {
                            await SendAsync(
                                _nativeEffectDevicePath,
                                CreateOffRequest(),
                                CancellationToken.None).ConfigureAwait(false);
                        }
                        finally
                        {
                            _nativeEffectActive = false;
                            _nativeEffectDevicePath = null;
                        }
                    }
                }
                finally
                {
                    await ReleaseModeLeaseAsync(CancellationToken.None).ConfigureAwait(false);
                }
            }
            finally
            {
                _gate.Release();
            }
        }
        finally
        {
            Interlocked.Exchange(ref _turnOffOnStop, 0);
        }
    }

    public async Task StopAsync()
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            await StopCoreAsync().ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        try
        {
            await StopAsync().ConfigureAwait(false);
        }
        finally
        {
            _gate.Dispose();
        }
    }

    private async Task StopCoreAsync()
    {
        var runtime = _runtime;
        _runtime = null;
        _externalSource = null;
        _keyboardInput = null;
        try
        {
            if (runtime is not null)
            {
                await runtime.DisposeAsync().ConfigureAwait(false);
            }
        }
        catch (Exception runtimeFailure)
        {
            try
            {
                await ReleaseModeLeaseAsync(CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception modeFailure)
            {
                throw new AggregateException(runtimeFailure, modeFailure);
            }
            throw;
        }

        if (_nativeEffectActive)
        {
            CancelNativeEffectKeepAlive();
            try
            {
                await SendAsync(
                    _nativeEffectDevicePath ?? throw new InvalidOperationException("Native lighting device path is unavailable."),
                    CreateOffRequest(),
                    CancellationToken.None).ConfigureAwait(false);
            }
            finally
            {
                _nativeEffectActive = false;
                _nativeEffectDevicePath = null;
            }
        }

        await ReleaseModeLeaseAsync(CancellationToken.None).ConfigureAwait(false);
    }

    private void CancelNativeEffectKeepAlive()
    {
        var cancellation = Interlocked.Exchange(ref _nativeEffectKeepAliveCancellation, null);
        cancellation?.Cancel();
        cancellation?.Dispose();
    }

    private async Task RunNativeEffectKeepAliveAsync(
        string devicePath,
        RazerDeviceManifest manifest,
        CancellationToken cancellationToken)
    {
        try
        {
            while (true)
            {
                await Task.Delay(_nativeEffectKeepAliveInterval, cancellationToken).ConfigureAwait(false);
                var shouldKeepAlive = false;
                await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
                try
                {
                    shouldKeepAlive = _nativeEffectActive &&
                        Volatile.Read(ref _displayAvailable) != 0 &&
                        StringComparer.OrdinalIgnoreCase.Equals(_nativeEffectDevicePath, devicePath);
                }
                finally
                {
                    _gate.Release();
                }

                if (!shouldKeepAlive)
                {
                    return;
                }

                try
                {
                    await ValidateCurrentPathAsync(
                        devicePath,
                        manifest,
                        cancellationToken).ConfigureAwait(false);
                }
                catch (NotSupportedException) when (!cancellationToken.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception exception) when (
                    !cancellationToken.IsCancellationRequested &&
                    exception is Win32Exception or IOException or InvalidOperationException)
                {
                    // A transient HID failure must not permanently disable the keepalive.
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    internal void ObserveMappingInput(BladeMappingInputEvent input)
    {
        _keyboardInput?.ObserveMappingInput(input);
    }

    private async Task StartRuntimeAsync(
        BladeMatrixFramePump pump,
        ISoftwareLightingFrameSource source,
        ILightingInputAdapter? inputAdapter,
        CancellationToken cancellationToken)
    {
        var runtime = inputAdapter is null
            ? new SoftwareLightingRuntime(pump, source, FrameInterval)
            : new SoftwareLightingRuntime(pump, source, FrameInterval, inputAdapter);
        _runtime = runtime;
        _runtimeCompletion = runtime.Completion;
        _ = runtime.Completion.ContinueWith(
            completed =>
            {
                _ = completed.Exception;
                Interlocked.CompareExchange(ref _runtime, null, runtime);
            },
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
        try
        {
            await pump.FirstFrameApplied.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            _runtime = null;
            try
            {
                await runtime.DisposeAsync().ConfigureAwait(false);
            }
            catch
            {
                // The apply failure remains the useful error; runtime completion is still observable.
            }
            throw;
        }
    }

    private bool TryCreateNativeEffectRequest(
        BladeLightingEffect effect,
        out byte[] request)
    {
        if (_lightingDialect != BladeLightingDialect.LedMatrixBlade &&
            effect.Palette is not null &&
            effect.Mode is BladeLightingMode.Spectrum or BladeLightingMode.Wave)
        {
            request = Array.Empty<byte>();
            return false;
        }

        if (_lightingDialect == BladeLightingDialect.LedMatrixBlade)
        {
            request = effect.Mode switch
            {
                BladeLightingMode.Off => CreateOffRequest(),
                BladeLightingMode.Static =>
                    BladeLightingLedMatrixBladeProtocol.CreateStaticRequest(NextTransactionId(), effect.Color),
                BladeLightingMode.Breathing =>
                    BladeLightingLedMatrixBladeProtocol.CreateBreathingSingleRequest(NextTransactionId(), effect.Color),
                BladeLightingMode.Spectrum =>
                    BladeLightingLedMatrixBladeProtocol.CreateSpectrumRequest(NextTransactionId()),
                BladeLightingMode.Wave =>
                    BladeLightingLedMatrixBladeProtocol.CreateWaveRequest(NextTransactionId(), effect.Direction),
                BladeLightingMode.Reactive =>
                    BladeLightingLedMatrixBladeProtocol.CreateReactiveRequest(
                        NextTransactionId(), effect.ReactiveSpeed, effect.Color),
                _ => Array.Empty<byte>(),
            };
            return request.Length != 0;
        }

        request = effect.Mode switch
        {
            BladeLightingMode.Off => BladeLightingProtocol.CreateOffRequest(),
            BladeLightingMode.Static => BladeLightingProtocol.CreateStaticRequest(effect.Color),
            BladeLightingMode.Wave => BladeLightingProtocol.CreateWaveRequest(effect.Direction),
            BladeLightingMode.Spectrum => BladeLightingProtocol.CreateSpectrumRequest(),
            BladeLightingMode.Reactive =>
                BladeLightingProtocol.CreateReactiveRequest(effect.ReactiveSpeed, effect.Color),
            BladeLightingMode.Starlight =>
                effect.StarlightColorMode switch
                {
                    BladeStarlightColorMode.Random =>
                        BladeLightingProtocol.CreateStarlightRandomRequest(effect.StarlightSpeed),
                    BladeStarlightColorMode.Single =>
                        BladeLightingProtocol.CreateStarlightSingleRequest(effect.StarlightSpeed, effect.Color),
                    BladeStarlightColorMode.Dual =>
                        BladeLightingProtocol.CreateStarlightDualRequest(
                            effect.StarlightSpeed, effect.Color, effect.SecondColor),
                    _ => Array.Empty<byte>(),
                },
            BladeLightingMode.Breathing =>
                BladeLightingProtocol.CreateBreathingSingleRequest(effect.Color),
            _ => Array.Empty<byte>(),
        };

        return request.Length != 0;
    }

    private byte[] CreateOffRequest() =>
        _lightingDialect == BladeLightingDialect.LedMatrixBlade
            ? BladeLightingLedMatrixBladeProtocol.CreateOffRequest(NextTransactionId())
            : BladeLightingProtocol.CreateOffRequest();

    private Func<IReadOnlyList<RazerRgb>, int, int, byte[][]> CreateFrameRequestBuilder() =>
        _lightingDialect switch
        {
            BladeLightingDialect.LedMatrixBlade =>
                (frame, rows, columns) => BladeLightingLedMatrixBladeProtocol.CreateMatrixFrameRequests(
                    frame, rows, columns, NextTransactionId),
            _ => BladeLightingProtocol.CreateMatrixFrameRequests,
        };

    private (DeviceDescriptor Device, RazerDeviceManifest Manifest) FindReadyBlade(
        IReadOnlyList<DeviceDescriptor> devices)
    {
        foreach (var device in devices)
        {
            var manifest = _registry.Find(device.VendorId, device.ProductId);
            var dialect = manifest is null
                ? BladeLightingDialect.Unknown
                : BladeLightingRuntimeDialect.Get(device.ProductId, manifest.LightingDialect);
            if (manifest is not null &&
                IsWritableLightingDialect(dialect, manifest) &&
                device.Access == DeviceAccessState.Available &&
                device.Capability == DeviceCapabilityState.PendingValidation &&
                device.FeatureReportByteLength == manifest.Collection.FeatureReportLength &&
                device.UsagePage == manifest.Collection.UsagePage &&
                device.Usage == manifest.Collection.Usage)
            {
                _lightingDialect = dialect;
                _matrixRows = manifest.MatrixRows == 0
                    ? dialect == BladeLightingDialect.LedMatrixBlade
                        ? BladeLightingLedMatrixBladeProtocol.Rows
                        : BladeLightingProtocol.Rows
                    : manifest.MatrixRows;
                _matrixColumns = manifest.MatrixColumns == 0
                    ? dialect == BladeLightingDialect.LedMatrixBlade
                        ? BladeLightingLedMatrixBladeProtocol.Columns
                        : BladeLightingProtocol.Columns
                    : manifest.MatrixColumns;
                return (device, manifest);
            }
        }

        throw new InvalidOperationException("No writable Blade keyboard lighting feature collection was found.");
    }

    private static bool IsWritableLightingDialect(
        BladeLightingDialect dialect,
        RazerDeviceManifest manifest) =>
        dialect switch
        {
            BladeLightingDialect.LedMatrixBlade => true,
            BladeLightingDialect.LedMatrix710 => manifest.MatrixRows > 0 && manifest.MatrixColumns > 0,
            BladeLightingDialect.LedMatrixBlade2 or BladeLightingDialect.LedMatrix2026 =>
                manifest.MatrixRows > 0 && manifest.MatrixColumns > 0,
            _ => false,
        };

    private async Task ValidateCurrentPathAsync(
        string devicePath,
        RazerDeviceManifest manifest,
        CancellationToken cancellationToken)
    {
        var request = manifest.GetRequiredCapability(CapabilityIds.KeyboardBrightnessGet);
        var response = await _transport.QueryAsync(
            devicePath,
            request.TransactionId,
            request.MaximumDataSize,
            request.CommandClass,
            request.CommandId,
            request.Arguments,
            request.Wait,
            cancellationToken,
            request.AllowRemainingPacketsMismatch).ConfigureAwait(false);
        if (response[6] < 2)
        {
            throw new InvalidOperationException("Keyboard brightness readback is too short; matrix lighting will not start.");
        }
    }

    private async Task SendFrameAsync(
        string devicePath,
        IReadOnlyList<RazerRgb> frame,
        CancellationToken cancellationToken)
    {
        await _transport.SendBatchAsync(
            devicePath,
            CreateFrameRequestBuilder()(frame, _matrixRows, _matrixColumns),
            MatrixWait,
            cancellationToken).ConfigureAwait(false);
    }

    private async Task RestoreAsync(string devicePath, CancellationToken cancellationToken)
    {
        if (Volatile.Read(ref _turnOffOnStop) != 0)
        {
            return;
        }

        try
        {
            await SendFrameAsync(
                devicePath,
                BladeMatrixFramePump.ScaleFrame(_restoreFrame, _matrixRows, _matrixColumns),
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception frameFailure)
        {
            try
            {
                await ReleaseModeLeaseAsync(CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception modeFailure)
            {
                throw new AggregateException(frameFailure, modeFailure);
            }
            throw;
        }

        await ReleaseModeLeaseAsync(CancellationToken.None).ConfigureAwait(false);
    }

    private async Task ReleaseModeLeaseAsync(CancellationToken cancellationToken)
    {
        var lease = Interlocked.Exchange(ref _modeLease, null);
        if (lease is null)
        {
            return;
        }

        await lease.ReleaseAsync(() => SendAsync(
            lease.DevicePath,
            BladeDeviceModeProtocol.CreateSetNormalRequest(NextTransactionId()),
            cancellationToken)).ConfigureAwait(false);
    }

    private Task<byte[]> SendAsync(
        string devicePath,
        byte[] request,
        CancellationToken cancellationToken)
    {
        if (request[6] == 0x01 && request[7] == 0x03 && request[8] == 0x0A &&
            request[RazerFeatureReport.ArgumentsOffset] == 0x19)
        {
            return _transport.QueryPreparedAsync(
                devicePath,
                request,
                MatrixWait,
                cancellationToken);
        }

        return _transport.QueryAsync(
            devicePath,
            request[2],
            request[6],
            request[7],
            request[8],
            request.AsMemory(RazerFeatureReport.ArgumentsOffset, request[6]),
            MatrixWait,
            cancellationToken);
    }

    /// <summary>
    /// The 2026 matrix dialect has no 0F:02 lighting-engine gate. Sending the 2025
    /// gate makes the device reject the whole apply with 0x05 and leaves the old frame.
    /// </summary>
    private Task SendLightingEngineGateAsync(string devicePath, CancellationToken cancellationToken) =>
        _lightingDialect is BladeLightingDialect.LedMatrixBlade or
            BladeLightingDialect.LedMatrix2026 or BladeLightingDialect.LedMatrixBlade2
            ? Task.CompletedTask
            : SendAsync(
                devicePath,
                BladeLightingProtocol.CreateLightingEngineGateRequest(NextTransactionId()),
                cancellationToken);

    private byte NextTransactionId()
    {
        var current = _transactionId;
        _transactionId = current == 30 ? (byte)0 : (byte)(current + 1);
        return current;
    }

    private static void Validate(BladeLightingEffect effect)
    {
        ArgumentNullException.ThrowIfNull(effect);
        if (!Enum.IsDefined(effect.Mode))
        {
            throw new ArgumentOutOfRangeException(nameof(effect));
        }
        if (effect.Mode is BladeLightingMode.Wave or BladeLightingMode.Wheel &&
            !Enum.IsDefined(effect.Direction))
        {
            throw new ArgumentOutOfRangeException(nameof(effect));
        }
        if (effect.Mode == BladeLightingMode.Reactive && effect.ReactiveSpeed is < 1 or > 4)
        {
            throw new ArgumentOutOfRangeException(nameof(effect));
        }
        if (effect.Mode == BladeLightingMode.Starlight &&
            (effect.StarlightSpeed is < 1 or > 3 || !Enum.IsDefined(effect.StarlightColorMode)))
        {
            throw new ArgumentOutOfRangeException(nameof(effect));
        }
        if (effect.Mode is BladeLightingMode.Wheel or BladeLightingMode.Tidal &&
            effect.AnimationSpeed is < 1 or > 3)
        {
            throw new ArgumentOutOfRangeException(nameof(effect));
        }
        if (effect.Mode == BladeLightingMode.ChromaFile && effect.ChromaAnimation is null)
        {
            throw new ArgumentException("The Chroma animation is missing.", nameof(effect));
        }
    }

    private sealed class EffectFrameSource : ISoftwareLightingFrameSource
    {
        private readonly BladeLightingEffect _effect;
        private readonly StarlightLightingRenderer? _starlight;

        public EffectFrameSource(BladeLightingEffect effect)
        {
            _effect = effect;
            if (effect.Mode == BladeLightingMode.Starlight)
            {
                _starlight = new StarlightLightingRenderer(effect.Color, seed: 710);
            }
        }

        public ValueTask<IReadOnlyList<RazerRgb>> RenderAsync(
            TimeSpan elapsed,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            IReadOnlyList<RazerRgb> frame = _effect.Mode switch
            {
                BladeLightingMode.Off => QuickLightingEngine.RenderSolid(default),
                BladeLightingMode.Static => QuickLightingEngine.RenderSolid(_effect.Color),
                BladeLightingMode.Breathing => QuickLightingEngine.RenderBreathing(elapsed, _effect.Color),
                BladeLightingMode.Spectrum => QuickLightingEngine.RenderSpectrum(
                    elapsed, QuickLightingEngine.Rows, QuickLightingEngine.Columns, _effect.Palette),
                BladeLightingMode.Wave => QuickLightingEngine.RenderWave(
                    elapsed, _effect.Direction, QuickLightingEngine.Rows, QuickLightingEngine.Columns, _effect.Palette),
                BladeLightingMode.Fire => QuickLightingEngine.RenderFire(
                    elapsed, 100, QuickLightingEngine.Rows, QuickLightingEngine.Columns, _effect.Palette),
                BladeLightingMode.Wheel => QuickLightingEngine.RenderWheel(
                    elapsed,
                    _effect.Direction == BladeWaveDirection.Left
                        ? QuickLightingDirection.CounterClockwise
                        : QuickLightingDirection.Clockwise,
                    QuickLightingEngine.Rows, QuickLightingEngine.Columns, _effect.Palette,
                    _effect.AnimationSpeed),
                BladeLightingMode.Starlight => _starlight!.Render(elapsed),
                BladeLightingMode.Tidal => QuickLightingEngine.RenderTidal(
                    elapsed, _effect.Color, _effect.SecondColor, _effect.AnimationSpeed),
                _ => throw new InvalidOperationException("Unsupported Blade lighting mode."),
            };
            return ValueTask.FromResult(frame);
        }
    }

    private sealed class KeyboardEffectFrameSource(
        BladeLightingEffect effect,
        WindowsKeyboardLightingAdapter adapter) : ISoftwareLightingFrameSource
    {
        private static readonly TimeSpan Duration = TimeSpan.FromSeconds(1);
        private readonly List<QuickLightingKeyEvent> _events = [];

        public ValueTask<IReadOnlyList<RazerRgb>> RenderAsync(
            TimeSpan elapsed,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            adapter.DrainTo(_events);
            _events.RemoveAll(item => item.At <= elapsed - Duration);
            IReadOnlyList<RazerRgb> frame = effect.Mode switch
            {
                BladeLightingMode.Reactive =>
                    QuickLightingEngine.RenderReactive(elapsed, _events, effect.Color, Duration),
                BladeLightingMode.Ripple =>
                    QuickLightingEngine.RenderRipple(elapsed, _events, effect.Color, Duration),
                _ => throw new InvalidOperationException("The keyboard input source supports only Reactive or Ripple."),
            };
            return ValueTask.FromResult(frame);
        }
    }

    private sealed class AudioMeterFrameSource(
        WasapiAudioMeterAdapter adapter,
        LightingPalette? palette) : ISoftwareLightingFrameSource
    {
        private double _smoothedLevel;
        private TimeSpan _lastElapsed;

        public ValueTask<IReadOnlyList<RazerRgb>> RenderAsync(
            TimeSpan elapsed,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var level = adapter.ReadLevel();
            var delta = elapsed - _lastElapsed;
            _smoothedLevel = _lastElapsed == TimeSpan.Zero
                ? level
                : QuickLightingEngine.SmoothAudioLevel(_smoothedLevel, level, delta);
            _lastElapsed = elapsed;
            return ValueTask.FromResult<IReadOnlyList<RazerRgb>>(
                QuickLightingEngine.RenderAudioMeter(
                    _smoothedLevel,
                    colorBoost: 0,
                    QuickLightingEngine.Rows,
                    QuickLightingEngine.Columns,
                    palette));
        }
    }

    private sealed class AmbientFrameSource(WindowsDisplayCaptureAdapter adapter) : ISoftwareLightingFrameSource
    {
        public async ValueTask<IReadOnlyList<RazerRgb>> RenderAsync(
            TimeSpan elapsed,
            CancellationToken cancellationToken)
        {
            var frame = await adapter.ReadFrameAsync(cancellationToken).ConfigureAwait(false);
            return QuickLightingEngine.RenderAmbientAwareness(
                frame.Pixels,
                frame.Width,
                frame.Height);
        }
    }
}
