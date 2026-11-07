using OpenSynapse.Core.Devices;
using OpenSynapse.Windows.Devices;
using OpenSynapse.Windows.Protocols;
using Xunit;

namespace OpenSynapse.Core.Tests;

public sealed class RazerHidDiscoveryRegressionTests
{
    [Fact]
    public async Task OneFailedProbeDoesNotStopLaterProbes()
    {
        var definition = Find(0x025C);
        var transport = new ScriptedTransport
        {
            Handler = (_, request) => request[8] is 0x81 or 0x87
                ? throw new RazerCommandRejectedException("The device rejected the query (0x03).")
                : Echo(request),
        };
        var service = new RazerHidDeviceService(RazerHidDeviceCatalog.BuiltIn, transport);

        var result = await service.ResolveEndpointPathAsync(definition, ["candidate"]);

        Assert.Equal("candidate", result.Path);
        Assert.Null(result.Error);
        Assert.Contains(transport.Requests, request => request[8] == 0x82);
    }

    [Fact]
    public async Task FailedCandidatePathDoesNotHideTheNextPath()
    {
        var definition = Find(0x0088);
        var transport = new ScriptedTransport
        {
            Handler = (path, request) => path == "awake"
                ? Echo(request)
                : throw new IOException("wrong interface"),
        };
        var service = new RazerHidDeviceService(RazerHidDeviceCatalog.BuiltIn, transport);

        var result = await service.ResolveEndpointPathAsync(definition, ["asleep", "awake"]);

        Assert.Equal("awake", result.Path);
        Assert.Null(result.Error);
    }

    [Fact]
    public async Task EndpointFailureRetriesAndKeepsTheLastReason()
    {
        var definition = Find(0x0088);
        var awake = false;
        var transport = new ScriptedTransport
        {
            Handler = (_, request) => awake
                ? Echo(request)
                : throw new RazerDeviceTimeoutException("The device did not answer this command (0x04)."),
        };
        var service = new RazerHidDeviceService(RazerHidDeviceCatalog.BuiltIn, transport);

        var failed = await service.ResolveEndpointPathAsync(definition, ["candidate"]);
        Assert.Null(failed.Path);
        Assert.Contains("0x04", failed.Error!);

        awake = true;
        var recovered = await service.ResolveEndpointPathAsync(definition, ["candidate"]);
        Assert.Equal("candidate", recovered.Path);
        Assert.Null(recovered.Error);
    }

    [Fact]
    public async Task ProbeErrorsKeepTheFailureClass()
    {
        await AssertProbeError(
            new RazerCommandRejectedException("The device rejected the query (0x03)."), "0x03");
        await AssertProbeError(
            new RazerDeviceTimeoutException("The device did not answer this command (0x04)."), "0x04");
        await AssertProbeError(
            new RazerReportMismatchException(
                "The device returned a report that did not match the request: txn=0x05 (want 0x1F)."),
            "txn=0x05 (want 0x1F)");
    }

    [Fact]
    public async Task UnsupportedCommandStillProvesTheControlChannel()
    {
        var definition = Find(0x00A4);
        var transport = new ScriptedTransport
        {
            Handler = (_, _) => throw new NotSupportedException("The device does not support this command."),
        };
        var service = new RazerHidDeviceService(RazerHidDeviceCatalog.BuiltIn, transport);

        var result = await service.ResolveEndpointPathAsync(definition, ["candidate"]);

        Assert.Equal("candidate", result.Path);
        Assert.Null(result.Error);
        Assert.Single(transport.Requests);
    }

    [Fact]
    public async Task AnAnsweringPathIsPreferredOverA0x05OnlyPath()
    {
        var definition = Find(0x00A4);
        var transport = new ScriptedTransport
        {
            Handler = (path, request) => path == "control"
                ? Echo(request)
                : throw new NotSupportedException("The device does not support this command."),
        };
        var service = new RazerHidDeviceService(RazerHidDeviceCatalog.BuiltIn, transport);

        var result = await service.ResolveEndpointPathAsync(definition, ["partial", "control"]);

        Assert.Equal("control", result.Path);
        Assert.Null(result.Error);
        Assert.Contains(transport.Requests, request => request[8] == 0x81);
    }

    [Fact]
    public void LegacyWirelessDevicesKeepTheirPinnedTransactions()
    {
        AssertPinned("HID#VID_1532&PID_025C#keyboard", 0x9F, 0x9F);
        AssertPinned("HID#VID_1532&PID_025C#keyboard", 0x3F, 0x3F);
        AssertPinned("HID#VID_1532&PID_025C#keyboard", 0x1F, 0x1F);

        AssertPinned("HID#VID_1532&PID_0088#receiver", 0x00, 0x1F);
        AssertPinned("HID#VID_1532&PID_0088#receiver", 0x3F, 0x3F);
        AssertPinned("HID#VID_1532&PID_0088#receiver", 0x1F, 0x1F);
        AssertPinned("HID#VID_1532&PID_0086#wired", 0x00, 0x1F);
        AssertPinned("HID#VID_1532&PID_0086#wired", 0x3F, 0x3F);

        var otherSequence = -1;
        Assert.Equal((byte)0, RazerFeatureTransport.PrepareTransactionId(
            "HID#VID_1532&PID_00AA#mouse", 0x9F, 0x84, 0x84, ref otherSequence));

        // 00A4 standard reads/lighting follow OpenRazer's FF wildcard; linker and
        // child battery/charging commands keep the E0 | sequence.
        var dockSequence = -1;
        Assert.Equal((byte)0xFF, RazerFeatureTransport.PrepareTransactionId(
            "HID#VID_1532&PID_00A4#dock", 0x1F, 0x00, 0x84, ref dockSequence));
        Assert.Equal((byte)0xE0, RazerFeatureTransport.PrepareTransactionId(
            "HID#VID_1532&PID_00A4#dock", 0xE0, 0x00, 0xBF, ref dockSequence));
    }

    [Fact]
    public void KnownWirelessBatteryAndChargingTransactionsFollowOpenRazer()
    {
        var keyboardSequence = -1;
        Assert.Equal((byte)0x9F, RazerFeatureTransport.PrepareTransactionId(
            "HID#VID_1532&PID_025C#keyboard", 0x1F, 0x07, 0x80, ref keyboardSequence));
        Assert.Equal((byte)0x9F, RazerFeatureTransport.PrepareTransactionId(
            "HID#VID_1532&PID_025C#keyboard", 0x1F, 0x07, 0x84, ref keyboardSequence));

        var receiverSequence = -1;
        Assert.Equal((byte)0x1F, RazerFeatureTransport.PrepareTransactionId(
            "HID#VID_1532&PID_0088#receiver", 0x3F, 0x07, 0x80, ref receiverSequence));
    }

    [Theory]
    [InlineData(0x02C9)]
    [InlineData(0x02D2)]
    [InlineData(0x02C2)]
    [InlineData(0x02F5)]
    public void DualLinkKeyboardTransactionsUseTheHighBank(ushort productId)
    {
        var sequence = -1;
        var path = $"HID#VID_1532&PID_{productId:X4}#keyboard";

        for (var index = 0; index < 34; index++)
        {
            Assert.Equal(
                (byte)(0x80 | (index & 0x1F)),
                RazerFeatureTransport.PrepareTransactionId(path, 0x1F, 0x00, 0x87, ref sequence));
        }
    }

    [Theory]
    [InlineData(0x02B4)]
    [InlineData(0x02CC)]
    public void OtherKeyboardLinksKeepTheLowBank(ushort productId)
    {
        var sequence = -1;
        var path = $"HID#VID_1532&PID_{productId:X4}#keyboard";

        Assert.Equal(
            (byte)0x00,
            RazerFeatureTransport.PrepareTransactionId(path, 0x1F, 0x00, 0x87, ref sequence));
    }

    [Fact]
    public void DualLinkKeyboardPairingAndScanUseTheLowBank()
    {
        var sequence = -1;
        const string path = "HID#VID_1532&PID_02C9#keyboard";

        Assert.Equal(
            (byte)0x00,
            RazerFeatureTransport.PrepareTransactionId(path, 0x1F, 0x00, 0x41, ref sequence));
        Assert.Equal(
            (byte)0x01,
            RazerFeatureTransport.PrepareTransactionId(path, 0x1F, 0x00, 0x46, ref sequence));
    }

    [Fact]
    public void DockLinkerOnlySetsTheHighBitForLinkerCommands()
    {
        const string path = "HID#VID_1532&PID_00A4#dock";
        var sequence = -1;

        Assert.Equal((byte)0xE0, RazerFeatureTransport.PrepareTransactionId(
            path, 0xE0, 0x00, 0xBF, ref sequence));
        Assert.Equal((byte)0xE1, RazerFeatureTransport.PrepareTransactionId(
            path, 0xE0, 0x00, 0xC7, ref sequence));
        Assert.Equal((byte)0xE2, RazerFeatureTransport.PrepareTransactionId(
            path, 0xE0, 0x00, 0x47, ref sequence));
        Assert.Equal((byte)0x03, RazerFeatureTransport.PrepareTransactionId(
            path, 0xE0, 0x00, 0x46, ref sequence));
        Assert.Equal((byte)0x04, RazerFeatureTransport.PrepareTransactionId(
            path, 0xE0, 0x00, 0x41, ref sequence));
        Assert.Equal((byte)0xFF, RazerFeatureTransport.PrepareTransactionId(
            path, 0x1F, 0x0F, 0x84, ref sequence));

        var batterySequence = -1;
        Assert.Equal((byte)0xE0, RazerFeatureTransport.PrepareTransactionId(
            path, 0x1F, 0x07, 0x80, ref batterySequence));
        Assert.Equal((byte)0xE1, RazerFeatureTransport.PrepareTransactionId(
            path, 0x1F, 0x07, 0x84, ref batterySequence));
    }

    [Fact]
    public async Task WirelessKeyboardResolvesOnItsPinnedBatteryProbe()
    {
        var definition = Find(0x025C);
        var transport = new PreparingTransport((_, request) =>
        {
            // Model a collection where identity reads stay silent; the battery read is the
            // first probe that must use OpenRazer's 0x9F pin.
            if (request[7] == 0x00 && request[8] is 0x81 or 0x82 or 0x84)
                throw new RazerDeviceTimeoutException("The device did not answer this command (0x04).");
            if (request[7] == 0x07 && request[8] is 0x80 or 0x84 && request[2] == 0x9F)
                return Echo(request);
            throw new RazerCommandRejectedException("The device rejected the query (0x03).");
        });
        var service = new RazerHidDeviceService(RazerHidDeviceCatalog.BuiltIn, transport);

        var result = await service.ResolveEndpointPathAsync(
            definition, ["HID#VID_1532&PID_025C&MI_02&COL01#keyboard"]);

        Assert.Equal("HID#VID_1532&PID_025C&MI_02&COL01#keyboard", result.Path);
        Assert.Null(result.Error);
        Assert.Contains(transport.Requests, request => request[7] == 0x07 && request[8] == 0x80 && request[2] == 0x9F);
    }

    [Fact]
    public async Task BasiliskReceiverResolvesOnItsPinnedFirmwareProbe()
    {
        var definition = Find(0x0088);
        var transport = new PreparingTransport((_, request) =>
            request[7] == 0x00 && request[8] == 0x81 && request[2] == 0x1F
                ? Echo(request)
                : throw new RazerDeviceTimeoutException("The device did not answer this command (0x04)."));
        var service = new RazerHidDeviceService(RazerHidDeviceCatalog.BuiltIn, transport);

        var result = await service.ResolveEndpointPathAsync(
            definition, ["HID#VID_1532&PID_0088#receiver"]);

        Assert.Equal("HID#VID_1532&PID_0088#receiver", result.Path);
        Assert.Null(result.Error);
        Assert.Contains(transport.Requests, request => request[8] == 0x81 && request[2] == 0x1F);
    }

    [Fact]
    public async Task BasiliskReceiverResolvesOnItsBatteryProbeWhenIdentityReadsDoNotAnswer()
    {
        var definition = Find(0x0088);
        var transport = new PreparingTransport((_, request) =>
            request[7] == 0x07 && request[8] == 0x80 && request[2] == 0x1F
                ? Echo(request)
                : throw new RazerDeviceTimeoutException("The device did not answer this command (0x04)."));
        var service = new RazerHidDeviceService(RazerHidDeviceCatalog.BuiltIn, transport);

        var result = await service.ResolveEndpointPathAsync(
            definition, ["HID#VID_1532&PID_0088#receiver"]);

        Assert.Equal("HID#VID_1532&PID_0088#receiver", result.Path);
        Assert.Null(result.Error);
        Assert.Contains(transport.Requests, request => request[7] == 0x07 && request[8] == 0x80 && request[2] == 0x1F);
    }

    [Fact]
    public async Task DockProResolvesOnItsWildcardFirmwareProbe()
    {
        var definition = Find(0x00A4);
        var transport = new PreparingTransport((_, request) =>
            request[7] == 0x00 && request[8] == 0x81 && request[2] == 0xFF
                ? Echo(request)
                : throw new RazerDeviceTimeoutException("The device did not answer this command (0x04)."));
        var service = new RazerHidDeviceService(RazerHidDeviceCatalog.BuiltIn, transport);

        var result = await service.ResolveEndpointPathAsync(
            definition, ["HID#VID_1532&PID_00A4&MI_00&COL01#dock"]);

        Assert.Equal("HID#VID_1532&PID_00A4&MI_00&COL01#dock", result.Path);
        Assert.Null(result.Error);
        Assert.Contains(transport.Requests, request => request[8] == 0x81 && request[2] == 0xFF);
    }

    [Fact]
    public void CandidateOrderingPrefersTheOpenRazerControlInterfaceUntilAPathIsCached()
    {
        var dockInterface2 = Descriptor("HID#VID_1532&PID_00A4&MI_02&COL01#dock", 0x00A4);
        var dockInterface0 = Descriptor("HID#VID_1532&PID_00A4&MI_00&COL01#dock", 0x00A4);
        Assert.Same(dockInterface0, RazerHidDeviceService.OrderEndpointCandidates(
            0x00A4, [dockInterface2, dockInterface0], null)[0]);
        Assert.Same(dockInterface2, RazerHidDeviceService.OrderEndpointCandidates(
            0x00A4, [dockInterface2, dockInterface0], dockInterface2.DevicePath)[0]);

        var keyboardInterface0 = Descriptor("HID#VID_1532&PID_025C&MI_00&COL01#keyboard", 0x025C);
        var keyboardInterface2 = Descriptor("HID#VID_1532&PID_025C&MI_02&COL01#keyboard", 0x025C);
        Assert.Same(keyboardInterface2, RazerHidDeviceService.OrderEndpointCandidates(
            0x025C, [keyboardInterface0, keyboardInterface2], null)[0]);

        var receiverInterface1 = Descriptor("HID#VID_1532&PID_0088&MI_01&COL01#receiver", 0x0088);
        var receiverInterface0 = Descriptor("HID#VID_1532&PID_0088&MI_00&COL01#receiver", 0x0088);
        Assert.Same(receiverInterface0, RazerHidDeviceService.OrderEndpointCandidates(
            0x0088, [receiverInterface1, receiverInterface0], null)[0]);

        var wiredInterface1 = Descriptor("HID#VID_1532&PID_0086&MI_01&COL01#wired", 0x0086);
        var wiredInterface0 = Descriptor("HID#VID_1532&PID_0086&MI_00&COL01#wired", 0x0086);
        Assert.Same(wiredInterface0, RazerHidDeviceService.OrderEndpointCandidates(
            0x0086, [wiredInterface1, wiredInterface0], null)[0]);

        var ordinaryInterface2 = Descriptor("HID#VID_1532&PID_007E&MI_02&COL01#dock", 0x007E);
        var ordinaryInterface0 = Descriptor("HID#VID_1532&PID_007E&MI_00&COL01#dock", 0x007E);
        Assert.Same(ordinaryInterface2, RazerHidDeviceService.OrderEndpointCandidates(
            0x007E, [ordinaryInterface2, ordinaryInterface0], null)[0]);
    }

    [Theory]
    [InlineData(0x025C, "MI_00", "MI_02")]
    [InlineData(0x0088, "MI_01", "MI_00")]
    [InlineData(0x00A4, "MI_02", "MI_00")]
    public async Task PreferredControlInterfaceIsProbedBeforeFallbackCollections(
        int productId,
        string fallbackInterface,
        string preferredInterface)
    {
        var definition = Find((ushort)productId);
        var fallback = Descriptor(
            $"HID#VID_1532&PID_{productId:X4}&{fallbackInterface}&COL01#fallback",
            (ushort)productId);
        var preferred = Descriptor(
            $"HID#VID_1532&PID_{productId:X4}&{preferredInterface}&COL01#preferred",
            (ushort)productId);
        var ordered = RazerHidDeviceService.OrderEndpointCandidates(
            (ushort)productId, [fallback, preferred], null);
        var transport = new PreparingTransport((path, request) =>
            string.Equals(path, preferred.DevicePath, StringComparison.OrdinalIgnoreCase)
                ? Echo(request)
                : throw new IOException("not the control interface"));
        var service = new RazerHidDeviceService(RazerHidDeviceCatalog.BuiltIn, transport);

        var result = await service.ResolveEndpointPathAsync(
            definition, ordered.Select(candidate => candidate.DevicePath));

        Assert.Equal(preferred.DevicePath, result.Path);
        Assert.Null(result.Error);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void MissingPhysicalKeyFallsBackToTheEndpointPath(string? physicalDeviceKey)
    {
        const string path = @"\\?\hid#vid_1532&pid_0203&mi_02&col01#7&111111&0&0000#{guid}";
        var definition = Find(0x0203);

        var connection = Connection(definition, path, physicalDeviceKey);

        Assert.Equal(WindowsHidDiscovery.GetPhysicalDeviceKey(path), connection.InstanceId);
        Assert.NotEqual($"1532:{definition.ProductId:X4}", connection.InstanceId);
    }

    [Fact]
    public void SameModelDevicesKeepDistinctInstanceIdsWithoutAContainerKey()
    {
        const string firstPath = @"\\?\hid#vid_1532&pid_0203&mi_02&col01#7&111111&0&0000#{guid}";
        const string secondPath = @"\\?\hid#vid_1532&pid_0203&mi_02&col01#7&222222&0&0000#{guid}";
        var definition = Find(0x0203);

        var first = Connection(definition, firstPath, null);
        var second = Connection(definition, secondPath, null);
        var keyedFirst = Connection(definition, firstPath, "container:11111111-1111-1111-1111-111111111111");
        var keyedSecond = Connection(definition, secondPath, "container:22222222-2222-2222-2222-222222222222");

        Assert.NotEqual(first.InstanceId, second.InstanceId);
        Assert.Contains("7&111111&0&0000", first.InstanceId);
        Assert.Contains("7&222222&0&0000", second.InstanceId);
        Assert.NotEqual(keyedFirst.InstanceId, keyedSecond.InstanceId);
        Assert.Equal("container:11111111-1111-1111-1111-111111111111", keyedFirst.InstanceId);
    }

    private static RazerHidDeviceConnection Connection(
        RazerHidDeviceDefinition definition,
        string? devicePath,
        string? physicalDeviceKey) =>
        new(definition, devicePath, physicalDeviceKey, RazerHidEndpointState.Resolved,
            new HashSet<RazerHidBackendCapability>(),
            new Dictionary<RazerHidLedZone, RazerHidLightingZoneCapabilities>(), null);

    private static WindowsHidDiscovery.HidInterfaceDescriptor Descriptor(string path, ushort productId) =>
        new(path, "key", 0x1532, productId, 91, 91, 91, 0xFF00, 0x0002,
            DeviceAccessState.Available, string.Empty);

    private static async Task AssertProbeError(Exception failure, string expected)
    {
        var definition = Find(0x025C);
        var transport = new ScriptedTransport { Handler = (_, _) => throw failure };
        var service = new RazerHidDeviceService(RazerHidDeviceCatalog.BuiltIn, transport);

        var result = await service.ResolveEndpointPathAsync(definition, ["candidate"]);

        Assert.Null(result.Path);
        Assert.Contains(expected, result.Error!);
    }

    private static void AssertPinned(string path, byte requested, byte expected)
    {
        var sequence = -1;
        Assert.Equal(expected, RazerFeatureTransport.PrepareTransactionId(
            path, requested, 0x03, 0x84, ref sequence));
        Assert.Equal(-1, sequence);
    }

    private static RazerHidDeviceDefinition Find(ushort productId) =>
        RazerHidDeviceCatalog.BuiltIn.Find(RazerHidDeviceDefinition.VendorId, productId)!;

    private static byte[] Echo(byte[] request)
    {
        var response = request.ToArray();
        response[1] = 0x02;
        response[89] = RazerFeatureReport.CalculateCrc(response);
        return response;
    }

    private sealed class PreparingTransport(Func<string, byte[], byte[]> handler) : IRazerFeatureTransport
    {
        private readonly Dictionary<string, int> _sequences = new(StringComparer.OrdinalIgnoreCase);
        public List<byte[]> Requests { get; } = [];

        public Task<byte[]> QueryAsync(
            string devicePath,
            byte transactionId,
            byte dataSize,
            byte commandClass,
            byte commandId,
            ReadOnlyMemory<byte> arguments,
            TimeSpan deviceWait,
            CancellationToken cancellationToken,
            bool allowRemainingPacketsMismatch = false) =>
            throw new NotSupportedException();

        public Task<byte[]> QueryPreparedAsync(
            string devicePath,
            ReadOnlyMemory<byte> request,
            TimeSpan deviceWait,
            CancellationToken cancellationToken,
            bool allowRemainingPacketsMismatch = false)
        {
            var bytes = request.ToArray();
            var sequence = _sequences.TryGetValue(devicePath, out var value) ? value : -1;
            bytes[2] = RazerFeatureTransport.PrepareTransactionId(
                devicePath, bytes[2], bytes[7], bytes[8], ref sequence);
            _sequences[devicePath] = sequence;
            Requests.Add(bytes);
            return Task.FromResult(handler(devicePath, bytes));
        }
    }

    private sealed class ScriptedTransport : IRazerFeatureTransport
    {
        public required Func<string, byte[], byte[]> Handler { get; set; }
        public List<byte[]> Requests { get; } = [];

        public Task<byte[]> QueryAsync(
            string devicePath,
            byte transactionId,
            byte dataSize,
            byte commandClass,
            byte commandId,
            ReadOnlyMemory<byte> arguments,
            TimeSpan deviceWait,
            CancellationToken cancellationToken,
            bool allowRemainingPacketsMismatch = false) =>
            throw new NotSupportedException();

        public Task<byte[]> QueryPreparedAsync(
            string devicePath,
            ReadOnlyMemory<byte> request,
            TimeSpan deviceWait,
            CancellationToken cancellationToken,
            bool allowRemainingPacketsMismatch = false)
        {
            var bytes = request.ToArray();
            Requests.Add(bytes);
            return Task.FromResult(Handler(devicePath, bytes));
        }
    }
}
