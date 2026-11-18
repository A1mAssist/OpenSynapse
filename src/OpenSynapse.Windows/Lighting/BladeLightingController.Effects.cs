using System.ComponentModel;
using OpenSynapse.Core.Devices;
using OpenSynapse.Windows.Devices;
using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.Windows.Lighting;

public sealed partial class BladeLightingController
{
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
                try
                {
                    await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
                    try
                    {
                        if (!_nativeEffectActive ||
                            Volatile.Read(ref _displayAvailable) == 0 ||
                            !StringComparer.OrdinalIgnoreCase.Equals(_nativeEffectDevicePath, devicePath))
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
                        catch (Exception exception) when (
                            !cancellationToken.IsCancellationRequested &&
                            exception is NotSupportedException or Win32Exception or IOException or
                                InvalidOperationException or UnauthorizedAccessException or TimeoutException)
                        {
                            // A transient read failure must not stop the read-only keepalive.
                        }
                    }
                    finally
                    {
                        _gate.Release();
                    }
                }
                catch (Exception exception) when (
                    !cancellationToken.IsCancellationRequested &&
                    exception is NotSupportedException or Win32Exception or IOException or
                        InvalidOperationException or UnauthorizedAccessException or TimeoutException)
                {
                    // A transient HID or firmware state must not permanently stop the keepalive.
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
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

        // Blade2 inherits rzDevice25LedMatrix.setChromaEffectCommon. The middleware
        // sends Set LED Matrix RGB Effect (0x03:0x0A) for firmware effects; custom
        // palettes and frames are the only paths that use 0x03:0x0B.
        if (_lightingDialect is BladeLightingDialect.LedMatrixBlade or BladeLightingDialect.LedMatrixBlade2)
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
                    BladeLightingLedMatrixBladeProtocol.CreateWaveRequest(
                        NextTransactionId(), ToMatrixWireDirection(effect.Direction)),
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
            BladeLightingMode.Wave => BladeLightingProtocol.CreateWaveRequest(
                ToMatrixWireDirection(effect.Direction)),
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

    // Razer's own UI flips the direction before writing: the 6737 effect adapter in the
    // 023A, 029F (Blade 16 2023), 02B7 and 02E0 bundles all send `1 === direction ? 2 : 1`.
    // The semantic value is therefore not the wire byte on any Blade matrix family;
    // users see the mirror image when it is sent raw. 2026 stays on the semantic enum
    // until a unit confirms its 03:0A byte.
    private BladeWaveDirection ToMatrixWireDirection(BladeWaveDirection direction) =>
        _lightingDialect is BladeLightingDialect.LedMatrix710 or BladeLightingDialect.LedMatrixBlade
            or BladeLightingDialect.LedMatrixBlade2
            ? direction == BladeWaveDirection.Right ? BladeWaveDirection.Left : BladeWaveDirection.Right
            : direction;

    private byte[] CreateOffRequest() =>
        _lightingDialect is BladeLightingDialect.LedMatrixBlade or BladeLightingDialect.LedMatrixBlade2
            ? BladeLightingLedMatrixBladeProtocol.CreateOffRequest(NextTransactionId())
            : BladeLightingProtocol.CreateOffRequest();

    private Func<IReadOnlyList<RazerRgb>, int, int, byte[][]> CreateFrameRequestBuilder() =>
        _lightingDialect switch
        {
            BladeLightingDialect.LedMatrixBlade or BladeLightingDialect.LedMatrixBlade2
                or BladeLightingDialect.LedMatrix710 =>
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

    // A rejected matrix write falls back to the model's selector effect (0F:02), which
    // every LedMatrixBlade manifest still declares. Blade2/710/2026 have no selector
    // contract, so they can only drop to Unknown.
    private BladeLightingDialect SelectFallbackDialect() =>
        _lightingDialect == BladeLightingDialect.LedMatrixBlade
            ? BladeLightingDialect.FirmwareEffect
            : BladeLightingDialect.Unknown;

    private async Task ValidateCurrentPathAsync(
        string devicePath,
        RazerDeviceManifest manifest,
        CancellationToken cancellationToken)
    {
        var request = manifest.GetRequiredCapability(CapabilityIds.KeyboardBrightnessGet);
        byte[] response;
        try
        {
            response = await _transport.QueryAsync(
                devicePath,
                request.TransactionId,
                request.MaximumDataSize,
                request.CommandClass,
                request.CommandId,
                request.Arguments,
                request.Wait,
                cancellationToken,
                request.AllowRemainingPacketsMismatch).ConfigureAwait(false);
        }
        catch (NotSupportedException)
        {
            // The firmware answered on this path; it just does not implement the
            // brightness probe. A stale handle throws IO/timeout, not this.
            return;
        }

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
        foreach (var request in CreateFrameRequestBuilder()(frame, _matrixRows, _matrixColumns))
        {
            // The EC answers every 03:0B row write. A batch that only calls
            // HidD_SetFeature leaves those replies queued for the next query.
            await SendAsync(
                devicePath, request, cancellationToken, allowRemainingPacketsMismatch: true)
                .ConfigureAwait(false);
        }
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
        CancellationToken cancellationToken,
        bool allowRemainingPacketsMismatch = false)
    {
        if (request[6] == 0x09 && request[7] == 0x03 && request[8] == 0x0A &&
            request[RazerFeatureReport.ArgumentsOffset] == 0x19)
        {
            return _transport.QueryPreparedAsync(
                devicePath,
                request,
                MatrixWait,
                cancellationToken,
                allowRemainingPacketsMismatch);
        }

        return _transport.QueryAsync(
            devicePath,
            request[2],
            request[6],
            request[7],
            request[8],
            request.AsMemory(RazerFeatureReport.ArgumentsOffset, request[6]),
            MatrixWait,
            cancellationToken,
            allowRemainingPacketsMismatch);
    }

    /// <summary>
    /// Only the 710/2025 path sends the 0F:02 lighting-engine gate. The Blade2
    /// bundle has no backlight or lighting-engine gate before its effect command.
    /// </summary>
    private async Task SendLightingEngineGateAsync(string devicePath, CancellationToken cancellationToken)
    {
        if (_lightingDialect is not BladeLightingDialect.LedMatrix710)
        {
            return;
        }

        await SendAsync(
            devicePath,
            BladeLightingProtocol.CreateLightingEngineGateRequest(NextTransactionId()),
            cancellationToken).ConfigureAwait(false);
    }

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
}
