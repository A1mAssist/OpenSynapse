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
}

public sealed record BladeLightingEffect(
    BladeLightingMode Mode,
    RazerRgb Color = default,
    BladeWaveDirection Direction = BladeWaveDirection.Right)
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
}

public sealed class BladeLightingController : IBladeLightingController
{
    private static readonly TimeSpan FrameInterval = TimeSpan.FromMilliseconds(40);
    private static readonly RazerRgb DefaultRestoreColor = new(0x99, 0xDD, 0x72);
    private static readonly TimeSpan MatrixWait = TimeSpan.FromMilliseconds(1);

    private readonly IRazerFeatureTransport _transport;
    private readonly RazerDeviceRegistry _registry;
    private readonly RazerRgb[] _restoreFrame;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private SoftwareLightingRuntime? _runtime;
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
        RazerDeviceRegistry registry,
        RazerRgb restoreColor)
    {
        _transport = transport ?? throw new ArgumentNullException(nameof(transport));
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _restoreFrame = QuickLightingEngine.RenderSolid(restoreColor);
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
            var (device, manifest) = FindReadyBlade(devices);
            await ValidateCurrentPathAsync(device.Id, manifest, cancellationToken).ConfigureAwait(false);
            await StopCoreAsync().ConfigureAwait(false);
            _transactionId = 0;
            await SendAsync(
                device.Id,
                BladeDeviceModeProtocol.CreateSetSoftwareRequest(NextTransactionId()),
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
                var runtime = new SoftwareLightingRuntime(
                    pump,
                    new EffectFrameSource(effect),
                    FrameInterval);
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
            catch
            {
                try
                {
                    await SendAsync(
                        device.Id,
                        BladeDeviceModeProtocol.CreateSetNormalRequest(NextTransactionId()),
                        CancellationToken.None).ConfigureAwait(false);
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
        if (runtime is not null)
        {
            await runtime.DisposeAsync().ConfigureAwait(false);
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
        var requests = new byte[BladeLightingProtocol.Rows][];
        for (byte row = 0; row < BladeLightingProtocol.Rows; row++)
        {
            var offset = row * BladeLightingProtocol.Columns;
            requests[row] = BladeLightingProtocol.CreateMatrixRowRequest(
                (byte)(row + 1),
                row,
                0,
                frame.Skip(offset).Take(BladeLightingProtocol.Columns).ToArray());
        }

        await _transport.SendBatchAsync(devicePath, requests, MatrixWait, cancellationToken).ConfigureAwait(false);
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
                await SendAsync(
                    devicePath,
                    BladeDeviceModeProtocol.CreateSetNormalRequest(NextTransactionId()),
                    cancellationToken).ConfigureAwait(false);
            }
            catch (Exception modeFailure)
            {
                throw new AggregateException(frameFailure, modeFailure);
            }
            throw;
        }

        await SendAsync(
            devicePath,
            BladeDeviceModeProtocol.CreateSetNormalRequest(NextTransactionId()),
            cancellationToken).ConfigureAwait(false);
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
        if (effect.Mode == BladeLightingMode.Wave && !Enum.IsDefined(effect.Direction))
        {
            throw new ArgumentOutOfRangeException(nameof(effect));
        }
    }

    private sealed class EffectFrameSource(BladeLightingEffect effect) : ISoftwareLightingFrameSource
    {
        public ValueTask<IReadOnlyList<RazerRgb>> RenderAsync(
            TimeSpan elapsed,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            IReadOnlyList<RazerRgb> frame = effect.Mode switch
            {
                BladeLightingMode.Off => QuickLightingEngine.RenderSolid(default),
                BladeLightingMode.Static => QuickLightingEngine.RenderSolid(effect.Color),
                BladeLightingMode.Breathing => QuickLightingEngine.RenderBreathing(elapsed, effect.Color),
                BladeLightingMode.Spectrum => QuickLightingEngine.RenderSpectrum(elapsed),
                BladeLightingMode.Wave => QuickLightingEngine.RenderWave(elapsed, effect.Direction),
                BladeLightingMode.Fire => QuickLightingEngine.RenderFire(elapsed, 710),
                _ => throw new InvalidOperationException("不支持的 Blade 灯光模式。"),
            };
            return ValueTask.FromResult(frame);
        }
    }
}
