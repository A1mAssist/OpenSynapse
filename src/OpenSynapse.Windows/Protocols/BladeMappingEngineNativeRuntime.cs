using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;

namespace OpenSynapse.Windows.Protocols;

public sealed record BladeUnsupportedMappingEvent(
    string InputJson,
    string OutputJson,
    ulong TimeTick);

public sealed record BladeInputNotificationEvent(
    string InputJson,
    ulong TimeTick);

/// <summary>
/// Opt-in host for the installed, source-verified Razer MappingEngine runtime.
/// The caller must supply a complete Product 710 storage graph captured or compiled
/// from the official schema; this class only owns the native lifecycle.
/// </summary>
public sealed class BladeMappingEngineNativeRuntime : IAsyncDisposable
{
    private const int ProductId = 710;
    private const int VendorId = 0x1532;
    private const int DeviceEventType = 5;
    private const string KnownDllSha256 =
        "82CF78080C78EB7092A12BEC89421E00AAC5A1047F41AF3D205ECE806980A15B";
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(10);
    private static readonly List<object> UncertainNativeLifetimes = [];
    private static readonly object UncertainNativeLifetimesLock = new();

    private readonly NativeApi _api;
    private readonly TimeSpan _timeout;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly List<Delegate> _callbacks = [];
    private readonly object _disposeSync = new();
    private Task? _disposeTask;
    private string? _deviceInfoJson;
    private bool _initializeAttempted;
    private bool _deviceAddAttempted;
    private bool _mappingEnableAttempted;
    private bool _inputNotificationRegistered;
    private bool _unsupportedMappingRegistered;
    private bool _canReleaseNative = true;
    private int _disposed;

    public event Action<BladeUnsupportedMappingEvent>? UnsupportedMappingReceived;
    public event Action<BladeInputNotificationEvent>? InputNotificationReceived;

    internal BladeMappingEngineNativeRuntime(NativeApi api, TimeSpan timeout)
    {
        _api = api ?? throw new ArgumentNullException(nameof(api));
        if (timeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(timeout));
        }
        _timeout = timeout;
    }

    /// <summary>
    /// Loads only the exact MappingEngine binary whose ABI was statically verified.
    /// Loading does not initialize or enable mapping; <see cref="StartAsync"/> is explicit.
    /// </summary>
    public static BladeMappingEngineNativeRuntime CreateVerified(
        string dllPath,
        TimeSpan? operationTimeout = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dllPath);
        var fullPath = Path.GetFullPath(dllPath);
        using var stream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        var hash = Convert.ToHexString(SHA256.HashData(stream));
        if (!hash.Equals(KnownDllSha256, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"MappingEngine DLL 哈希不匹配；拒绝加载未验证版本 {fullPath}。");
        }

        // Keep the verified file locked through NativeLibrary.Load so it cannot be
        // replaced between hashing and loading.
        return new BladeMappingEngineNativeRuntime(
            NativeApi.Load(fullPath),
            operationTimeout ?? DefaultTimeout);
    }

    public async Task StartAsync(
        string deviceInfoJson,
        string storageKey,
        string storageValueJson,
        CancellationToken cancellationToken = default)
    {
        ValidateSession(deviceInfoJson, storageKey, storageValueJson);
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
            if (_initializeAttempted)
            {
                throw new InvalidOperationException("Product 710 MappingEngine 已经启动。");
            }

            _deviceInfoJson = deviceInfoJson;
            _canReleaseNative = false;
            _initializeAttempted = true;
            var driverReady = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var deviceEvent = Keep(new DeviceEventCallback(
                (_, eventType, eventJson, _) => OnDeviceEvent(driverReady, eventType, eventJson)));

            try
            {
                await CallCompletionAsync(
                    "mappingEngineInitialize",
                    _api.Initialize,
                    cancellationToken).ConfigureAwait(false);

                _deviceAddAttempted = true;
                await CallDeviceResultAsync(
                    "addUsbDevice",
                    callback => _api.AddUsbDevice(deviceInfoJson, deviceEvent, callback),
                    cancellationToken).ConfigureAwait(false);
                await WaitAsync(driverReady.Task, "等待 MappingEngine driver ready", cancellationToken)
                    .ConfigureAwait(false);

                await CallSimpleResultAsync(
                    "localStorageSetItem",
                    callback => _api.LocalStorageSetItem(storageKey, storageValueJson, callback),
                    allowAlreadyEnabled: false,
                    cancellationToken).ConfigureAwait(false);

                _mappingEnableAttempted = true;
                await CallSimpleResultAsync(
                    "enableMapping",
                    _api.EnableMapping,
                    allowAlreadyEnabled: true,
                    cancellationToken).ConfigureAwait(false);

                if (_api.RegisterInputNotification is not null &&
                    _api.SetInputNotificationCallback is not null &&
                    _api.UnregisterInputNotification is not null)
                {
                    var inputCallback = Keep(new InputNotificationCallback(
                        (_, eventType, inputJson, timeTick) =>
                            OnInputNotification(eventType, inputJson, timeTick)));
                    await CallDeviceResultAsync(
                        "registerInputNotification",
                        callback => _api.RegisterInputNotification(deviceInfoJson, callback),
                        cancellationToken).ConfigureAwait(false);
                    _inputNotificationRegistered = true;
                    await CallDeviceResultAsync(
                        "setInputNotificationCallback",
                        callback => _api.SetInputNotificationCallback(
                            deviceInfoJson, inputCallback, callback),
                        cancellationToken).ConfigureAwait(false);
                }

                if (_api.RegisterUnsupportedMapping is not null &&
                    _api.SetUnsupportedMappingCallback is not null &&
                    _api.UnregisterUnsupportedMapping is not null)
                {
                    var unsupportedCallback = Keep(new UnsupportedMappingCallback(
                        (_, eventType, inputJson, outputJson, timeTick) =>
                            OnUnsupportedMapping(eventType, inputJson, outputJson, timeTick)));
                    await CallDeviceResultAsync(
                        "registerUnsupportedMapping",
                        callback => _api.RegisterUnsupportedMapping(deviceInfoJson, callback),
                        cancellationToken).ConfigureAwait(false);
                    _unsupportedMappingRegistered = true;
                    await CallDeviceResultAsync(
                        "setUnsupportedMappingCallback",
                        callback => _api.SetUnsupportedMappingCallback(
                            deviceInfoJson, unsupportedCallback, callback),
                        cancellationToken).ConfigureAwait(false);
                }
            }
            catch (Exception startException)
            {
                var cleanupException = await CleanupCoreAsync().ConfigureAwait(false);
                if (cleanupException is not null)
                {
                    throw new AggregateException(
                        "Product 710 MappingEngine 启动失败，且清理未完全成功。",
                        startException,
                        cleanupException);
                }
                throw;
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
            var cleanupException = await CleanupCoreAsync().ConfigureAwait(false);
            if (cleanupException is not null)
            {
                throw cleanupException;
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public ValueTask DisposeAsync()
    {
        lock (_disposeSync)
        {
            _disposeTask ??= DisposeCoreAsync();
            return new ValueTask(_disposeTask);
        }
    }

    private async Task DisposeCoreAsync()
    {
        Interlocked.Exchange(ref _disposed, 1);
        Exception? cleanupException = null;
        try
        {
            await StopAsync().ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            cleanupException = exception;
        }

        if (_canReleaseNative)
        {
            _api.Release();
        }
        else
        {
            // ponytail: keep one uncertain native lifetime rooted until process exit;
            // unloading code while a timed-out callback may still run can crash the process.
            lock (UncertainNativeLifetimesLock)
            {
                UncertainNativeLifetimes.Add(new object[] { _api, _callbacks.ToArray() });
            }
        }

        if (cleanupException is not null)
        {
            throw cleanupException;
        }
    }

    private async Task<Exception?> CleanupCoreAsync()
    {
        if (!_initializeAttempted)
        {
            return null;
        }

        var errors = new List<Exception>(4);
        if (_inputNotificationRegistered &&
            _deviceInfoJson is not null &&
            _api.UnregisterInputNotification is not null)
        {
            await CaptureCleanupErrorAsync(
                errors,
                () => CallDeviceResultAsync(
                    "unregisterInputNotification",
                    callback => _api.UnregisterInputNotification(_deviceInfoJson, callback),
                    CancellationToken.None)).ConfigureAwait(false);
        }
        if (_unsupportedMappingRegistered &&
            _deviceInfoJson is not null &&
            _api.UnregisterUnsupportedMapping is not null)
        {
            await CaptureCleanupErrorAsync(
                errors,
                () => CallDeviceResultAsync(
                    "unregisterUnsupportedMapping",
                    callback => _api.UnregisterUnsupportedMapping(_deviceInfoJson, callback),
                    CancellationToken.None)).ConfigureAwait(false);
        }
        if (_deviceAddAttempted && _deviceInfoJson is not null)
        {
            await CaptureCleanupErrorAsync(
                errors,
                () => CallDeviceResultAsync(
                    "removeUsbDevice",
                    callback => _api.RemoveUsbDevice(_deviceInfoJson, callback),
                    CancellationToken.None)).ConfigureAwait(false);
        }
        if (_mappingEnableAttempted)
        {
            await CaptureCleanupErrorAsync(
                errors,
                () => CallSimpleResultAsync(
                    "disableMapping",
                    _api.DisableMapping,
                    allowAlreadyEnabled: false,
                    CancellationToken.None)).ConfigureAwait(false);
        }

        try
        {
            await CallCompletionAsync(
                "mappingEngineShutdown",
                _api.Shutdown,
                CancellationToken.None).ConfigureAwait(false);
            _canReleaseNative = true;
        }
        catch (Exception exception)
        {
            errors.Add(exception);
        }
        finally
        {
            _initializeAttempted = false;
            _deviceAddAttempted = false;
            _mappingEnableAttempted = false;
            _inputNotificationRegistered = false;
            _unsupportedMappingRegistered = false;
            _deviceInfoJson = null;
        }

        return errors.Count switch
        {
            0 => null,
            1 => errors[0],
            _ => new AggregateException("Product 710 MappingEngine 清理失败。", errors),
        };
    }

    private async Task CallCompletionAsync(
        string operation,
        Action<CompletionCallback> invoke,
        CancellationToken cancellationToken)
    {
        var completion = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var callback = Keep(new CompletionCallback(() => completion.TrySetResult()));
        invoke(callback);
        await WaitAsync(completion.Task, operation, cancellationToken).ConfigureAwait(false);
    }

    private async Task CallDeviceResultAsync(
        string operation,
        Action<DeviceResultCallback> invoke,
        CancellationToken cancellationToken)
    {
        var completion = new TaskCompletionSource<NativeResult>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var callback = Keep(new DeviceResultCallback(
            (ok, reason, _) => completion.TrySetResult(new(ok, reason))));
        invoke(callback);
        RequireSuccess(
            operation,
            await WaitAsync(completion.Task, operation, cancellationToken).ConfigureAwait(false),
            allowAlreadyEnabled: false);
    }

    private async Task CallSimpleResultAsync(
        string operation,
        Action<SimpleResultCallback> invoke,
        bool allowAlreadyEnabled,
        CancellationToken cancellationToken)
    {
        var completion = new TaskCompletionSource<NativeResult>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var callback = Keep(new SimpleResultCallback(
            (ok, reason) => completion.TrySetResult(new(ok, reason))));
        invoke(callback);
        RequireSuccess(
            operation,
            await WaitAsync(completion.Task, operation, cancellationToken).ConfigureAwait(false),
            allowAlreadyEnabled);
    }

    private async Task WaitAsync(
        Task task,
        string operation,
        CancellationToken cancellationToken)
    {
        try
        {
            await task.WaitAsync(_timeout, cancellationToken).ConfigureAwait(false);
        }
        catch (TimeoutException exception)
        {
            throw new TimeoutException($"{operation} 在 {_timeout.TotalSeconds:g} 秒内未完成。", exception);
        }
    }

    private async Task<T> WaitAsync<T>(
        Task<T> task,
        string operation,
        CancellationToken cancellationToken)
    {
        try
        {
            return await task.WaitAsync(_timeout, cancellationToken).ConfigureAwait(false);
        }
        catch (TimeoutException exception)
        {
            throw new TimeoutException($"{operation} 在 {_timeout.TotalSeconds:g} 秒内未完成。", exception);
        }
    }

    private T Keep<T>(T callback) where T : Delegate
    {
        _callbacks.Add(callback);
        return callback;
    }

    private static void OnDeviceEvent(
        TaskCompletionSource completion,
        int eventType,
        string? eventJson)
    {
        if (eventType != DeviceEventType || string.IsNullOrWhiteSpace(eventJson))
        {
            return;
        }

        try
        {
            using var document = JsonDocument.Parse(eventJson);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("type", out var type) ||
                type.ValueKind != JsonValueKind.String)
            {
                throw new InvalidDataException("MappingEngine device event 缺少 type。");
            }

            if (type.GetString() == "error")
            {
                var reason = root.TryGetProperty("info", out var errorInfo) &&
                    errorInfo.ValueKind == JsonValueKind.String
                        ? errorInfo.GetString()
                        : null;
                completion.TrySetException(new InvalidOperationException(
                    string.IsNullOrWhiteSpace(reason)
                        ? "MappingEngine driver 初始化失败。"
                        : $"MappingEngine driver 初始化失败：{reason}"));
            }
            else if (type.GetString() == "info" &&
                root.TryGetProperty("info", out var info) &&
                info.ValueKind == JsonValueKind.String &&
                info.GetString() == "driver ready")
            {
                completion.TrySetResult();
            }
        }
        catch (JsonException exception)
        {
            completion.TrySetException(new InvalidDataException(
                "MappingEngine driver event 不是有效 JSON。",
                exception));
        }
        catch (InvalidDataException exception)
        {
            completion.TrySetException(exception);
        }
    }

    private void OnUnsupportedMapping(
        int eventType,
        string? inputJson,
        string? outputJson,
        ulong timeTick)
    {
        if (eventType != 2 || string.IsNullOrWhiteSpace(inputJson) || string.IsNullOrWhiteSpace(outputJson))
        {
            return;
        }

        try
        {
            UnsupportedMappingReceived?.Invoke(new(inputJson, outputJson, timeTick));
        }
        catch
        {
            // Never unwind a managed exception through the native callback boundary.
        }
    }

    private void OnInputNotification(
        int eventType,
        string? inputJson,
        ulong timeTick)
    {
        // Synapse accepts event type 1 as the input-notified event. Other native
        // device events belong to the separate hardware/event callback surface.
        if (eventType != 1 || string.IsNullOrWhiteSpace(inputJson))
        {
            return;
        }

        try
        {
            InputNotificationReceived?.Invoke(new(inputJson, timeTick));
        }
        catch
        {
            // Never unwind a managed exception through the native callback boundary.
        }
    }

    private static void RequireSuccess(
        string operation,
        NativeResult result,
        bool allowAlreadyEnabled)
    {
        if (result.Ok ||
            (allowAlreadyEnabled && result.Reason == "already enabled"))
        {
            return;
        }

        throw new InvalidOperationException(
            string.IsNullOrWhiteSpace(result.Reason)
                ? $"MappingEngine {operation} 失败。"
                : $"MappingEngine {operation} 失败：{result.Reason}");
    }

    private static async Task CaptureCleanupErrorAsync(
        ICollection<Exception> errors,
        Func<Task> cleanup)
    {
        try
        {
            await cleanup().ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            errors.Add(exception);
        }
    }

    private static void ValidateSession(
        string deviceInfoJson,
        string storageKey,
        string storageValueJson)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(deviceInfoJson);
        ArgumentException.ThrowIfNullOrWhiteSpace(storageKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(storageValueJson);

        using var device = ParseObject(deviceInfoJson, "deviceInfoJson");
        var root = device.RootElement;
        if (!root.TryGetProperty("vendorId", out var vendor) ||
            !vendor.TryGetInt32(out var vendorId) || vendorId != VendorId ||
            !root.TryGetProperty("productId", out var product) ||
            !product.TryGetInt32(out var productId) || productId != ProductId ||
            !root.TryGetProperty("containerId", out var container) ||
            container.ValueKind != JsonValueKind.String ||
            string.IsNullOrWhiteSpace(container.GetString()) ||
            !root.TryGetProperty("guid", out var guid) ||
            guid.ValueKind != JsonValueKind.String ||
            !Guid.TryParse(guid.GetString(), out _))
        {
            throw new ArgumentException(
                "deviceInfoJson 必须声明 Product 710 的 vendorId、productId、containerId 和有效 guid。",
                nameof(deviceInfoJson));
        }

        var expectedStorageKey = $"synapse_{ProductId}_{container.GetString()}";
        if (!storageKey.Equals(expectedStorageKey, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                $"Product 710 storage key 必须是 {expectedStorageKey}。",
                nameof(storageKey));
        }

        using var storage = ParseObject(storageValueJson, "storageValueJson");
        var storageRoot = storage.RootElement;
        if (!storageRoot.TryGetProperty("productId", out var storageProduct) ||
            !storageProduct.TryGetInt32(out var storageProductId) || storageProductId != ProductId ||
            !storageRoot.TryGetProperty("reportIDs", out var reportIds) ||
            reportIds.ValueKind != JsonValueKind.Object ||
            !HasStringValue(reportIds, "4", "razerKeyReportID") ||
            !HasStringValue(reportIds, "5", "hardwareEventReportID"))
        {
            throw new ArgumentException(
                "storageValueJson 必须是声明 Product 710 RazerKey/HardwareEvent reportIDs 的完整官方存储对象。",
                nameof(storageValueJson));
        }

        BladeMappingEngineProtocol.ValidateCompleteProduct710Storage(storageValueJson);
    }

    private static JsonDocument ParseObject(string json, string parameterName)
    {
        try
        {
            var document = JsonDocument.Parse(json, new JsonDocumentOptions
            {
                AllowTrailingCommas = false,
                CommentHandling = JsonCommentHandling.Disallow,
                MaxDepth = 256,
            });
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                document.Dispose();
                throw new ArgumentException("JSON 根节点必须是对象。", parameterName);
            }
            return document;
        }
        catch (JsonException exception)
        {
            throw new ArgumentException("JSON 格式无效。", parameterName, exception);
        }
    }

    private static bool HasStringValue(JsonElement parent, string name, string expected) =>
        parent.TryGetProperty(name, out var value) &&
        value.ValueKind == JsonValueKind.String &&
        value.GetString() == expected;

    private readonly record struct NativeResult(bool Ok, string? Reason);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate void CompletionCallback();

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate void DeviceEventCallback(
        [MarshalAs(UnmanagedType.LPUTF8Str)] string? deviceInfoJson,
        int eventType,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string? eventJson,
        ulong timeTick);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate void DeviceResultCallback(
        [MarshalAs(UnmanagedType.I1)] bool ok,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string? reason,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string? deviceInfoJson);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate void SimpleResultCallback(
        [MarshalAs(UnmanagedType.I1)] bool ok,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string? reason);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate void UnsupportedMappingCallback(
        [MarshalAs(UnmanagedType.LPUTF8Str)] string? deviceInfoJson,
        int eventType,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string? inputJson,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string? outputJson,
        ulong timeTick);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate void InputNotificationCallback(
        [MarshalAs(UnmanagedType.LPUTF8Str)] string? deviceInfoJson,
        int eventType,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string? inputJson,
        ulong timeTick);

    internal sealed class NativeApi
    {
        internal NativeApi(
            Action<CompletionCallback> initialize,
            Action<string, DeviceEventCallback, DeviceResultCallback> addUsbDevice,
            Action<string, string, SimpleResultCallback> localStorageSetItem,
            Action<SimpleResultCallback> enableMapping,
            Action<string, DeviceResultCallback> removeUsbDevice,
            Action<SimpleResultCallback> disableMapping,
            Action<CompletionCallback> shutdown,
            Action release,
            Action<string, DeviceResultCallback>? registerUnsupportedMapping = null,
            Action<string, UnsupportedMappingCallback, DeviceResultCallback>? setUnsupportedMappingCallback = null,
            Action<string, DeviceResultCallback>? unregisterUnsupportedMapping = null,
            Action<string, DeviceResultCallback>? registerInputNotification = null,
            Action<string, InputNotificationCallback, DeviceResultCallback>? setInputNotificationCallback = null,
            Action<string, DeviceResultCallback>? unregisterInputNotification = null)
        {
            Initialize = initialize;
            AddUsbDevice = addUsbDevice;
            LocalStorageSetItem = localStorageSetItem;
            EnableMapping = enableMapping;
            RemoveUsbDevice = removeUsbDevice;
            DisableMapping = disableMapping;
            Shutdown = shutdown;
            RegisterInputNotification = registerInputNotification;
            SetInputNotificationCallback = setInputNotificationCallback;
            UnregisterInputNotification = unregisterInputNotification;
            RegisterUnsupportedMapping = registerUnsupportedMapping;
            SetUnsupportedMappingCallback = setUnsupportedMappingCallback;
            UnregisterUnsupportedMapping = unregisterUnsupportedMapping;
            _release = release;
        }

        internal Action<CompletionCallback> Initialize { get; }
        internal Action<string, DeviceEventCallback, DeviceResultCallback> AddUsbDevice { get; }
        internal Action<string, string, SimpleResultCallback> LocalStorageSetItem { get; }
        internal Action<SimpleResultCallback> EnableMapping { get; }
        internal Action<string, DeviceResultCallback> RemoveUsbDevice { get; }
        internal Action<SimpleResultCallback> DisableMapping { get; }
        internal Action<CompletionCallback> Shutdown { get; }
        internal Action<string, DeviceResultCallback>? RegisterInputNotification { get; }
        internal Action<string, InputNotificationCallback, DeviceResultCallback>? SetInputNotificationCallback { get; }
        internal Action<string, DeviceResultCallback>? UnregisterInputNotification { get; }
        internal Action<string, DeviceResultCallback>? RegisterUnsupportedMapping { get; }
        internal Action<string, UnsupportedMappingCallback, DeviceResultCallback>? SetUnsupportedMappingCallback { get; }
        internal Action<string, DeviceResultCallback>? UnregisterUnsupportedMapping { get; }
        private readonly Action _release;

        internal static NativeApi Load(string path)
        {
            var handle = NativeLibrary.Load(path);
            try
            {
                var initialize = GetExport<InitializeNative>(handle, "mappingEngineInitialize");
                var add = GetExport<AddUsbDeviceNative>(handle, "addUsbDevice");
                var storage = GetExport<LocalStorageSetItemNative>(handle, "localStorageSetItem");
                var enable = GetExport<SimpleNative>(handle, "enableMapping");
                var remove = GetExport<RemoveUsbDeviceNative>(handle, "removeUsbDevice");
                var disable = GetExport<SimpleNative>(handle, "disableMapping");
                var shutdown = GetExport<ShutdownNative>(handle, "mappingEngineShutdown");
                var registerUnsupported = GetExport<RegisterUnsupportedMappingNative>(
                    handle, "registerUnsupportedMapping");
                var setUnsupportedCallback = GetExport<SetUnsupportedMappingCallbackNative>(
                    handle, "setUnsupportedMappingCallback");
                var unregisterUnsupported = GetExport<RegisterUnsupportedMappingNative>(
                    handle, "unregisterUnsupportedMapping");
                var registerInput = GetExport<RegisterInputNotificationNative>(
                    handle, "registerInputNotification");
                var setInputCallback = GetExport<SetInputNotificationCallbackNative>(
                    handle, "setInputNotificationCallback");
                var unregisterInput = GetExport<RegisterInputNotificationNative>(
                    handle, "unregisterInputNotification");

                var handleToRelease = handle;
                return new NativeApi(
                    callback => initialize(callback),
                    (device, deviceEvent, callback) => add(device, deviceEvent, callback),
                    (key, value, callback) => storage(key, value, callback),
                    callback => enable(callback),
                    (device, callback) => remove(device, callback),
                    callback => disable(callback),
                    callback => shutdown(callback),
                    () =>
                    {
                        var loadedHandle = Interlocked.Exchange(ref handleToRelease, 0);
                        if (loadedHandle != 0)
                        {
                            NativeLibrary.Free(loadedHandle);
                        }
                    },
                    (device, callback) => registerUnsupported(device, callback),
                    (device, unsupportedCallback, callback) =>
                        setUnsupportedCallback(device, unsupportedCallback, callback),
                    (device, callback) => unregisterUnsupported(device, callback),
                    (device, callback) => registerInput(device, callback),
                    (device, inputCallback, callback) =>
                        setInputCallback(device, inputCallback, callback),
                    (device, callback) => unregisterInput(device, callback));
            }
            catch
            {
                NativeLibrary.Free(handle);
                throw;
            }
        }

        internal void Release() => _release();

        private static T GetExport<T>(nint handle, string name) where T : Delegate =>
            Marshal.GetDelegateForFunctionPointer<T>(NativeLibrary.GetExport(handle, name));

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void InitializeNative(CompletionCallback callback);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void AddUsbDeviceNative(
            [MarshalAs(UnmanagedType.LPUTF8Str)] string deviceInfoJson,
            DeviceEventCallback deviceEvent,
            DeviceResultCallback callback);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void LocalStorageSetItemNative(
            [MarshalAs(UnmanagedType.LPUTF8Str)] string key,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string valueJson,
            SimpleResultCallback callback);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void SimpleNative(SimpleResultCallback callback);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void RemoveUsbDeviceNative(
            [MarshalAs(UnmanagedType.LPUTF8Str)] string deviceInfoJson,
            DeviceResultCallback callback);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void ShutdownNative(CompletionCallback callback);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void RegisterUnsupportedMappingNative(
            [MarshalAs(UnmanagedType.LPUTF8Str)] string deviceInfoJson,
            DeviceResultCallback callback);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void SetUnsupportedMappingCallbackNative(
            [MarshalAs(UnmanagedType.LPUTF8Str)] string deviceInfoJson,
            UnsupportedMappingCallback unsupportedCallback,
            DeviceResultCallback callback);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void RegisterInputNotificationNative(
            [MarshalAs(UnmanagedType.LPUTF8Str)] string deviceInfoJson,
            DeviceResultCallback callback);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void SetInputNotificationCallbackNative(
            [MarshalAs(UnmanagedType.LPUTF8Str)] string deviceInfoJson,
            InputNotificationCallback inputCallback,
            DeviceResultCallback callback);
    }
}
