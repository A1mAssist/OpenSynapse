using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.Core.Tests;

public sealed class BladeMappingEngineNativeRuntimeTests
{
    private const string Device =
        "{\"vendorId\":5426,\"containerId\":\"{00000000-0000-0000-FFFF-FFFFFFFFFFFF}\",\"productId\":710,\"guid\":\"0269b196-7645-4f88-8735-95f8888f7f9d\"}";
    private const string StorageKey =
        "synapse_710_{00000000-0000-0000-FFFF-FFFFFFFFFFFF}";
    private const string Storage = Product710MappingFixture.CompleteStorage;

    [Fact]
    public async Task StartAndStopFollowVerifiedLifecycleOrder()
    {
        var native = new FakeNativeApi();
        var runtime = CreateRuntime(native);

        await runtime.StartAsync(Device, StorageKey, Storage);
        await runtime.StopAsync();
        await runtime.DisposeAsync();

        Assert.Equal(
            ["initialize", "add", "storage", "enable", "remove", "disable", "shutdown"],
            native.Calls);
        Assert.Equal(1, native.ReleaseCount);
    }

    [Fact]
    public async Task StaleSessionRecoveryDisablesAndShutsDownBeforeStart()
    {
        var native = new FakeNativeApi();
        await using var runtime = CreateRuntime(native);

        Assert.True(await runtime.TryRecoverStaleSessionAsync(Device));
        await runtime.DisposeAsync();

        Assert.Equal(["initialize", "remove", "disable", "shutdown"], native.Calls);
        Assert.Equal(1, native.ReleaseCount);
    }

    [Fact]
    public async Task StorageWaitsForDriverReadyAndTimeoutCleansUp()
    {
        var native = new FakeNativeApi { RaiseDriverReady = false };
        var runtime = CreateRuntime(native, TimeSpan.FromMilliseconds(20));

        await Assert.ThrowsAsync<TimeoutException>(
            () => runtime.StartAsync(Device, StorageKey, Storage));
        await runtime.DisposeAsync();

        Assert.Equal(["initialize", "add", "remove", "shutdown"], native.Calls);
        Assert.DoesNotContain("storage", native.Calls);
        Assert.Equal(1, native.ReleaseCount);
    }

    [Fact]
    public async Task FailedStorageRemovesDeviceAndShutsDownWithoutDisable()
    {
        var native = new FakeNativeApi { StorageSucceeds = false };
        var runtime = CreateRuntime(native);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => runtime.StartAsync(Device, StorageKey, Storage));
        await runtime.DisposeAsync();

        Assert.Contains("bad graph", exception.Message, StringComparison.Ordinal);
        Assert.Equal(["initialize", "add", "storage", "remove", "shutdown"], native.Calls);
        Assert.Equal(1, native.ReleaseCount);
    }

    [Fact]
    public async Task InvalidProductGraphNeverCallsNativeApi()
    {
        var native = new FakeNativeApi();
        await using var runtime = CreateRuntime(native);

        await Assert.ThrowsAsync<ArgumentException>(() => runtime.StartAsync(
            Device,
            StorageKey,
            "{\"productId\":710,\"reportIDs\":{\"4\":\"razerKeyReportID\"}}"));

        Assert.Empty(native.Calls);
    }

    [Fact]
    public async Task ConcurrentDisposeSharesCleanupAndRelease()
    {
        var native = new FakeNativeApi();
        var runtime = CreateRuntime(native);
        await runtime.StartAsync(Device, StorageKey, Storage);

        await Task.WhenAll(runtime.DisposeAsync().AsTask(), runtime.DisposeAsync().AsTask());

        Assert.Equal(1, native.Calls.Count(call => call == "shutdown"));
        Assert.Equal(1, native.ReleaseCount);
    }

    [Fact]
    public async Task ShutdownTimeoutDoesNotReleasePossiblyRunningNativeCode()
    {
        var native = new FakeNativeApi { ShutdownCompletes = false };
        var runtime = CreateRuntime(native, TimeSpan.FromMilliseconds(20));
        await runtime.StartAsync(Device, StorageKey, Storage);

        await Assert.ThrowsAsync<TimeoutException>(() => runtime.DisposeAsync().AsTask());

        Assert.Equal(0, native.ReleaseCount);
    }

    [Fact]
    public async Task UnsupportedMappingIsRegisteredDeliveredAndUnregisteredBeforeDeviceRemoval()
    {
        var native = new FakeNativeApi { SupportsUnsupportedMapping = true };
        await using var runtime = CreateRuntime(native);
        BladeUnsupportedMappingEvent? received = null;
        runtime.UnsupportedMappingReceived += mappingEvent => received = mappingEvent;

        await runtime.StartAsync(Device, StorageKey, Storage);
        native.RaiseUnsupportedMapping(
            "{\"type\":\"keyboard\"}",
            "{\"type\":\"bladeTrackpad\"}",
            42);
        await runtime.StopAsync();

        Assert.Equal(
            ["initialize", "add", "storage", "enable", "register-unsupported", "set-unsupported", "unregister-unsupported", "remove", "disable", "shutdown"],
            native.Calls);
        Assert.Equal("{\"type\":\"bladeTrackpad\"}", received?.OutputJson);
        Assert.Equal((ulong)42, received?.TimeTick);
    }

    [Fact]
    public async Task InputNotificationIsRegisteredDeliveredAndUnregisteredBeforeDeviceRemoval()
    {
        var native = new FakeNativeApi { SupportsInputNotification = true };
        await using var runtime = CreateRuntime(native);
        BladeInputNotificationEvent? received = null;
        runtime.InputNotificationReceived += inputEvent => received = inputEvent;

        await runtime.StartAsync(Device, StorageKey, Storage);
        native.RaiseInputNotification("{\"type\":\"keyboard\",\"scancode\":59,\"flag\":0}", 41);
        native.RaiseNonInputNotification("{\"type\":\"keyboard\"}", 42);
        await runtime.StopAsync();

        Assert.Equal(
            ["initialize", "add", "storage", "enable", "register-input", "set-input", "unregister-input", "remove", "disable", "shutdown"],
            native.Calls);
        Assert.Equal("{\"type\":\"keyboard\",\"scancode\":59,\"flag\":0}", received?.InputJson);
        Assert.Equal((ulong)41, received?.TimeTick);
    }

    [Fact]
    public async Task FailedInputCallbackRegistrationRestoresNativeLifecycle()
    {
        var native = new FakeNativeApi
        {
            SupportsInputNotification = true,
            InputCallbackSucceeds = false,
        };
        var runtime = CreateRuntime(native);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            runtime.StartAsync(Device, StorageKey, Storage));
        await runtime.DisposeAsync();

        Assert.Equal(
            ["initialize", "add", "storage", "enable", "register-input", "set-input", "unregister-input", "remove", "disable", "shutdown"],
            native.Calls);
        Assert.Equal(1, native.ReleaseCount);
    }

    [Fact]
    public async Task FailedUnsupportedCallbackRegistrationRestoresNativeLifecycle()
    {
        var native = new FakeNativeApi
        {
            SupportsUnsupportedMapping = true,
            UnsupportedCallbackSucceeds = false,
        };
        var runtime = CreateRuntime(native);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            runtime.StartAsync(Device, StorageKey, Storage));
        await runtime.DisposeAsync();

        Assert.Equal(
            ["initialize", "add", "storage", "enable", "register-unsupported", "set-unsupported", "unregister-unsupported", "remove", "disable", "shutdown"],
            native.Calls);
        Assert.Equal(1, native.ReleaseCount);
    }

    private static BladeMappingEngineNativeRuntime CreateRuntime(
        FakeNativeApi native,
        TimeSpan? timeout = null) =>
        new(native.CreateApi(), timeout ?? TimeSpan.FromSeconds(1));

    private sealed class FakeNativeApi
    {
        internal List<string> Calls { get; } = [];
        internal bool RaiseDriverReady { get; init; } = true;
        internal bool StorageSucceeds { get; init; } = true;
        internal bool ShutdownCompletes { get; init; } = true;
        internal bool SupportsUnsupportedMapping { get; init; }
        internal bool UnsupportedCallbackSucceeds { get; init; } = true;
        internal bool SupportsInputNotification { get; init; }
        internal bool InputCallbackSucceeds { get; init; } = true;
        internal int ReleaseCount { get; private set; }
        private BladeMappingEngineNativeRuntime.UnsupportedMappingCallback? _unsupportedMappingCallback;
        private BladeMappingEngineNativeRuntime.InputNotificationCallback? _inputNotificationCallback;

        internal BladeMappingEngineNativeRuntime.NativeApi CreateApi()
        {
            Action<string, BladeMappingEngineNativeRuntime.DeviceResultCallback>? register = null;
            Action<string, BladeMappingEngineNativeRuntime.UnsupportedMappingCallback,
                BladeMappingEngineNativeRuntime.DeviceResultCallback>? setCallback = null;
            Action<string, BladeMappingEngineNativeRuntime.DeviceResultCallback>? unregister = null;
            Action<string, BladeMappingEngineNativeRuntime.DeviceResultCallback>? registerInput = null;
            Action<string, BladeMappingEngineNativeRuntime.InputNotificationCallback,
                BladeMappingEngineNativeRuntime.DeviceResultCallback>? setInputCallback = null;
            Action<string, BladeMappingEngineNativeRuntime.DeviceResultCallback>? unregisterInput = null;
            if (SupportsUnsupportedMapping)
            {
                register = (device, callback) =>
                {
                    Calls.Add("register-unsupported");
                    callback(true, string.Empty, device);
                };
                setCallback = (device, unsupportedCallback, callback) =>
                {
                    Calls.Add("set-unsupported");
                    _unsupportedMappingCallback = unsupportedCallback;
                    callback(
                        UnsupportedCallbackSucceeds,
                        UnsupportedCallbackSucceeds ? string.Empty : "callback rejected",
                        device);
                };
                unregister = (device, callback) =>
                {
                    Calls.Add("unregister-unsupported");
                    callback(true, string.Empty, device);
                };
            }
            if (SupportsInputNotification)
            {
                registerInput = (device, callback) =>
                {
                    Calls.Add("register-input");
                    callback(true, string.Empty, device);
                };
                setInputCallback = (device, inputCallback, callback) =>
                {
                    Calls.Add("set-input");
                    _inputNotificationCallback = inputCallback;
                    callback(
                        InputCallbackSucceeds,
                        InputCallbackSucceeds ? string.Empty : "callback rejected",
                        device);
                };
                unregisterInput = (device, callback) =>
                {
                    Calls.Add("unregister-input");
                    callback(true, string.Empty, device);
                };
            }

            return new(
                callback =>
                {
                    Calls.Add("initialize");
                    callback();
                },
                (device, deviceEvent, callback) =>
                {
                    Calls.Add("add");
                    callback(true, string.Empty, device);
                    if (RaiseDriverReady)
                    {
                        deviceEvent(device, 5, "{\"type\":\"info\",\"info\":\"driver ready\"}", 1);
                    }
                },
                (_, _, callback) =>
                {
                    Calls.Add("storage");
                    callback(StorageSucceeds, StorageSucceeds ? string.Empty : "bad graph");
                },
                callback =>
                {
                    Calls.Add("enable");
                    callback(true, string.Empty);
                },
                (device, callback) =>
                {
                    Calls.Add("remove");
                    callback(true, string.Empty, device);
                },
                callback =>
                {
                    Calls.Add("disable");
                    callback(true, string.Empty);
                },
                callback =>
                {
                    Calls.Add("shutdown");
                    if (ShutdownCompletes)
                    {
                        callback();
                    }
                },
                () => ReleaseCount++,
                register,
                setCallback,
                unregister,
                registerInput,
                setInputCallback,
                unregisterInput);
        }

        internal void RaiseUnsupportedMapping(string inputJson, string outputJson, ulong timeTick) =>
            _unsupportedMappingCallback?.Invoke(Device, 2, inputJson, outputJson, timeTick);

        internal void RaiseInputNotification(string inputJson, ulong timeTick) =>
            _inputNotificationCallback?.Invoke(Device, 1, inputJson, timeTick);

        internal void RaiseNonInputNotification(string inputJson, ulong timeTick) =>
            _inputNotificationCallback?.Invoke(Device, 3, inputJson, timeTick);
    }
}
