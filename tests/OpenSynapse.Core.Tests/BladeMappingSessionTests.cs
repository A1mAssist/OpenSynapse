using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.Core.Tests;

public sealed class BladeMappingSessionTests
{
    private const string Device =
        "{\"vendorId\":5426,\"containerId\":\"{00000000-0000-0000-FFFF-FFFFFFFFFFFF}\",\"productId\":710,\"guid\":\"0269b196-7645-4f88-8735-95f8888f7f9d\"}";
    private const string StorageKey =
        "synapse_710_{00000000-0000-0000-FFFF-FFFFFFFFFFFF}";
    private const string Storage = Product710MappingFixture.CompleteStorage;

    [Fact]
    public async Task StartsProcessesInternalReportAndReleasesOnStop()
    {
        var native = new FakeNativeApi();
        await using var session = CreateSession(native);

        await session.StartAsync(Device, StorageKey, Storage);
        Assert.Equal(
            [new BladeMappingOutputEvent(30, true)],
            session.ProcessReport([0x04, 0x03, 0x00]));
        Assert.Equal(
            [new BladeMappingOutputEvent(30, false)],
            await session.StopAsync());

        Assert.Equal(
            ["initialize", "add", "storage", "enable", "remove", "disable", "shutdown"],
            native.Calls);
    }

    [Fact]
    public async Task ReportBeforeStartIsRejectedWithoutNativeCalls()
    {
        var native = new FakeNativeApi();
        await using var session = CreateSession(native);

        Assert.Throws<InvalidOperationException>(() => session.ProcessReport([0x04, 0x03]));
        Assert.Empty(native.Calls);
    }

    [Fact]
    public async Task ConcurrentDisposeSharesNativeCleanup()
    {
        var native = new FakeNativeApi();
        var session = CreateSession(native);
        await session.StartAsync(Device, StorageKey, Storage);

        await Task.WhenAll(session.DisposeAsync().AsTask(), session.DisposeAsync().AsTask());

        Assert.Equal(1, native.Calls.Count(call => call == "shutdown"));
    }

    [Fact]
    public async Task ProcessesExternallyDecodedInputsAndFlushesThemOnStop()
    {
        var native = new FakeNativeApi();
        await using var session = CreateSession(native);

        await session.StartAsync(Device, StorageKey, Storage);
        Assert.Equal(
            [new BladeMappingOutputEvent(30, true)],
            session.ProcessInputs([new(BladeMappingInputKind.RazerKey, 0x03, true)]));
        Assert.Equal(
            [new BladeMappingOutputEvent(30, false)],
            await session.StopAsync());
    }

    private static BladeMappingSession CreateSession(FakeNativeApi native) =>
        new(
            new BladeMappingEngineNativeRuntime(native.CreateApi(), TimeSpan.FromSeconds(1)),
            new BladeMappingInputRuntime(
            [new(
                BladeMappingInputKind.RazerKey,
                0x03,
                false,
                BladeMappingOutputKind.Keyboard,
                30)]));

    private sealed class FakeNativeApi
    {
        internal List<string> Calls { get; } = [];

        internal BladeMappingEngineNativeRuntime.NativeApi CreateApi() =>
            new(
                callback =>
                {
                    Calls.Add("initialize");
                    callback();
                },
                (device, deviceEvent, callback) =>
                {
                    Calls.Add("add");
                    callback(true, string.Empty, device);
                    deviceEvent(device, 5, "{\"type\":\"info\",\"info\":\"driver ready\"}", 1);
                },
                (_, _, callback) =>
                {
                    Calls.Add("storage");
                    callback(true, string.Empty);
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
                    callback();
                },
                () => { });
    }
}
