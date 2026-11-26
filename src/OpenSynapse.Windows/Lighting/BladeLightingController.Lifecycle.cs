using System.ComponentModel;
using OpenSynapse.Core.Devices;
using OpenSynapse.Windows.Devices;
using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.Windows.Lighting;

public sealed partial class BladeLightingController
{
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
                _externalPlayback = null;
                return;
            }
            var (device, manifest) = FindReadyBlade(devices);
            await ValidateCurrentPathAsync(device.Id, manifest, cancellationToken).ConfigureAwait(false);
            await StopCoreAsync().ConfigureAwait(false);
            _transactionId = 0;
            if (_lightingDialect is not (BladeLightingDialect.LedMatrixBlade or BladeLightingDialect.LedMatrixBlade2))
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
                        BladeLightingRuntimeDialect.Downgrade(
                            device.ProductId,
                            SelectFallbackDialect(),
                            exception.Message);
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
                    token => RestoreAsync(device.Id, device.Name, reportFallback: false, token),
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
                    BladeLightingRuntimeDialect.Downgrade(
                        device.ProductId,
                        SelectFallbackDialect(),
                        exception.Message);
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
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
            if (Volatile.Read(ref _displayAvailable) == 0)
            {
                _externalPlayback = (devices.ToArray(), source, restorePersistentEffect);
                return;
            }
            if (ReferenceEquals(_externalPlayback?.Source, source) &&
                _runtime is not null &&
                !_runtimeCompletion.IsCompleted)
            {
                return;
            }

            await ApplyExternalCoreAsync(devices, source, restorePersistentEffect, cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task ApplyExternalCoreAsync(
        IReadOnlyList<DeviceDescriptor> devices,
        ISoftwareLightingFrameSource source,
        bool restorePersistentEffect,
        CancellationToken cancellationToken)
    {
        var (device, manifest) = FindReadyBlade(devices);
        await ValidateCurrentPathAsync(device.Id, manifest, cancellationToken).ConfigureAwait(false);
        await StopCoreAsync().ConfigureAwait(false);
        _transactionId = 0;
        if (_lightingDialect is not (BladeLightingDialect.LedMatrixBlade or BladeLightingDialect.LedMatrixBlade2))
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
                    ? token => RestoreAsync(device.Id, device.Name, reportFallback: true, token)
                    : token => ReleaseModeLeaseAsync(CancellationToken.None),
                _matrixRows,
                _matrixColumns,
                CreateFrameRequestBuilder());
            await StartRuntimeAsync(pump, source, null, cancellationToken).ConfigureAwait(false);
            _externalPlayback = ([device], source, restorePersistentEffect);
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

    public async Task SetDisplayAvailableAsync(
        bool available,
        IReadOnlyList<DeviceDescriptor>? devices = null)
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
            if (Volatile.Read(ref _displayAvailable) == (available ? 1 : 0))
            {
                return;
            }
            Volatile.Write(ref _displayAvailable, available ? 1 : 0);
            if (available)
            {
                if (_externalPlayback is { } playback)
                {
                    await ApplyExternalCoreAsync(
                        playback.Devices, playback.Source, playback.RestorePersistentEffect,
                        CancellationToken.None).ConfigureAwait(false);
                }
                return;
            }

            CancelNativeEffectKeepAlive();
            Interlocked.Exchange(ref _turnOffOnStop, 1);
            try
            {
                var modeDevicePath = _modeLease?.DevicePath;
                var runtime = _runtime;
                var offSent = false;
                _runtime = null;
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
                            offSent = true;
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
                            offSent = true;
                        }
                        finally
                        {
                            _nativeEffectActive = false;
                            _nativeEffectDevicePath = null;
                        }
                    }
                    // Display-off must reach the keyboard even when another app or the
                    // firmware owns the current effect and we have no live runtime.
                    if (!offSent &&
                        runtime is null &&
                        devices is not null &&
                        FindReadyBladeOrNull(devices) is { } blade)
                    {
                        if (_modeLease is null &&
                            _lightingDialect is not (BladeLightingDialect.LedMatrixBlade or BladeLightingDialect.LedMatrixBlade2))
                        {
                            _modeLease = await _modeCoordinator.AcquireAsync(
                                blade.Device.Id,
                                token => SendAsync(
                                    blade.Device.Id,
                                    BladeDeviceModeProtocol.CreateSetSoftwareRequest(NextTransactionId()),
                                    token),
                                () => SendAsync(
                                    blade.Device.Id,
                                    BladeDeviceModeProtocol.CreateSetNormalRequest(NextTransactionId()),
                                    CancellationToken.None),
                                CancellationToken.None).ConfigureAwait(false);
                        }
                        await SendAsync(
                            blade.Device.Id,
                            CreateOffRequest(),
                            CancellationToken.None).ConfigureAwait(false);
                    }
                }
                finally
                {
                    await ReleaseModeLeaseAsync(CancellationToken.None).ConfigureAwait(false);
                }
            }
            finally
            {
                Interlocked.Exchange(ref _turnOffOnStop, 0);
            }
        }
        finally
        {
            _gate.Release();
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
        _externalPlayback = null;
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
}
