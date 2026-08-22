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
}

public sealed record BladeLightingEffect(
    BladeLightingMode Mode,
    RazerRgb Color = default,
    BladeWaveDirection Direction = BladeWaveDirection.Right,
    RazerRgb SecondColor = default)
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

    Task ApplyExternalAsync(
        IReadOnlyList<DeviceDescriptor> devices,
        ChromaExternalFrameSource source,
        CancellationToken cancellationToken = default);
}

public sealed class BladeLightingController : IBladeLightingController
{
    // The matrix path is seven feature reports per frame. Keep only the latest
    // frame if the device cannot sustain this target; never build a stale queue.
    private static readonly TimeSpan FrameInterval = TimeSpan.FromMilliseconds(1000d / 60d);
    private static readonly RazerRgb DefaultRestoreColor = new(0x99, 0xDD, 0x72);
    private static readonly TimeSpan MatrixWait = TimeSpan.FromMilliseconds(1);

    private readonly IRazerFeatureTransport _transport;
    private readonly RazerDeviceRegistry _registry;
    private readonly RazerRgb[] _restoreFrame;
    private readonly BladeSoftwareModeCoordinator _modeCoordinator;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private SoftwareLightingRuntime? _runtime;
    private ChromaExternalFrameSource? _externalSource;
    private BladeSoftwareModeCoordinator.BladeSoftwareModeLease? _modeLease;
    private Task _runtimeCompletion = Task.CompletedTask;
    private byte _transactionId;
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
        RazerRgb restoreColor,
        BladeSoftwareModeCoordinator? modeCoordinator = null)
    {
        _transport = transport ?? throw new ArgumentNullException(nameof(transport));
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _restoreFrame = QuickLightingEngine.RenderSolid(restoreColor);
        _modeCoordinator = modeCoordinator ?? new BladeSoftwareModeCoordinator();
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
            if (_externalSource is not null && _runtime is not null && !_runtimeCompletion.IsCompleted)
            {
                return;
            }
            var (device, manifest) = FindReadyBlade(devices);
            await ValidateCurrentPathAsync(device.Id, manifest, cancellationToken).ConfigureAwait(false);
            await StopCoreAsync().ConfigureAwait(false);
            _transactionId = 0;
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
            try
            {
                await SendAsync(
                    device.Id,
                    BladeLightingProtocol.CreateLightingEngineGateRequest(NextTransactionId()),
                    cancellationToken).ConfigureAwait(false);
                var pump = new BladeMatrixFramePump(
                    _transport,
                    device.Id,
                    token => RestoreAsync(device.Id, token));
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
                    source = new AudioMeterFrameSource(audioInput);
                }
                else if (effect.Mode == BladeLightingMode.Ambient)
                {
                    var displayInput = new WindowsDisplayCaptureAdapter();
                    inputAdapter = displayInput;
                    source = new AmbientFrameSource(displayInput);
                }
                else
                {
                    source = new EffectFrameSource(effect);
                }
                await StartRuntimeAsync(pump, source, inputAdapter, cancellationToken).ConfigureAwait(false);
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

    public async Task ApplyExternalAsync(
        IReadOnlyList<DeviceDescriptor> devices,
        ChromaExternalFrameSource source,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(devices);
        ArgumentNullException.ThrowIfNull(source);
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
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
            _modeLease = await _modeCoordinator.AcquireAsync(
                device.Id,
                token => SendAsync(device.Id,
                    BladeDeviceModeProtocol.CreateSetSoftwareRequest(NextTransactionId()), token),
                () => SendAsync(device.Id,
                    BladeDeviceModeProtocol.CreateSetNormalRequest(NextTransactionId()),
                    CancellationToken.None),
                cancellationToken).ConfigureAwait(false);
            try
            {
                await SendAsync(device.Id,
                    BladeLightingProtocol.CreateLightingEngineGateRequest(NextTransactionId()),
                    cancellationToken).ConfigureAwait(false);
                var pump = new BladeMatrixFramePump(
                    _transport,
                    device.Id,
                    token => RestoreAsync(device.Id, token));
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

        await ReleaseModeLeaseAsync(CancellationToken.None).ConfigureAwait(false);
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
            _externalSource = source as ChromaExternalFrameSource;
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

    private (DeviceDescriptor Device, RazerDeviceManifest Manifest) FindReadyBlade(
        IReadOnlyList<DeviceDescriptor> devices)
    {
        foreach (var device in devices)
        {
            var manifest = _registry.Find(device.VendorId, device.ProductId);
            if (manifest?.ProtocolFamily == "blade-710" &&
                device.Access == DeviceAccessState.Available &&
                device.Capability == DeviceCapabilityState.PendingValidation &&
                device.FeatureReportByteLength == manifest.Collection.FeatureReportLength &&
                device.UsagePage == manifest.Collection.UsagePage &&
                device.Usage == manifest.Collection.Usage)
            {
                return (device, manifest);
            }
        }

        throw new InvalidOperationException("未找到可写入的 Blade 键盘灯光 feature collection。");
    }

    private async Task ValidateCurrentPathAsync(
        string devicePath,
        RazerDeviceManifest manifest,
        CancellationToken cancellationToken)
    {
        var request = manifest.GetRequiredCapability("keyboard-brightness.get");
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
            throw new InvalidOperationException("键盘亮度读回长度不足，拒绝启动矩阵灯光。");
        }
    }

    private async Task SendFrameAsync(
        string devicePath,
        IReadOnlyList<RazerRgb> frame,
        CancellationToken cancellationToken)
    {
        await _transport.SendBatchAsync(
            devicePath,
            BladeLightingProtocol.CreateMatrixFrameRequests(frame),
            MatrixWait,
            cancellationToken).ConfigureAwait(false);
    }

    private async Task RestoreAsync(string devicePath, CancellationToken cancellationToken)
    {
        try
        {
            await SendFrameAsync(devicePath, _restoreFrame, cancellationToken).ConfigureAwait(false);
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
        CancellationToken cancellationToken) =>
        _transport.QueryAsync(
            devicePath,
            request[2],
            request[6],
            request[7],
            request[8],
            request.AsMemory(RazerFeatureReport.ArgumentsOffset, request[6]),
            MatrixWait,
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
                BladeLightingMode.Spectrum => QuickLightingEngine.RenderSpectrum(elapsed),
                BladeLightingMode.Wave => QuickLightingEngine.RenderWave(elapsed, _effect.Direction),
                BladeLightingMode.Fire => QuickLightingEngine.RenderFire(elapsed, 100),
                BladeLightingMode.Wheel => QuickLightingEngine.RenderWheel(
                    elapsed,
                    _effect.Direction == BladeWaveDirection.Left
                        ? QuickLightingDirection.CounterClockwise
                        : QuickLightingDirection.Clockwise),
                BladeLightingMode.Starlight => _starlight!.Render(elapsed),
                BladeLightingMode.Tidal => QuickLightingEngine.RenderTidal(
                    elapsed, _effect.Color, _effect.SecondColor),
                _ => throw new InvalidOperationException("不支持的 Blade 灯光模式。"),
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
                _ => throw new InvalidOperationException("键盘输入源仅支持 Reactive 或 Ripple。"),
            };
            return ValueTask.FromResult(frame);
        }
    }

    private sealed class AudioMeterFrameSource(WasapiAudioMeterAdapter adapter) : ISoftwareLightingFrameSource
    {
        public ValueTask<IReadOnlyList<RazerRgb>> RenderAsync(
            TimeSpan elapsed,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult<IReadOnlyList<RazerRgb>>(
                QuickLightingEngine.RenderAudioMeter(adapter.ReadLevel(), colorBoost: 0));
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
