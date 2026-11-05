using OpenSynapse.Core.Devices;
using OpenSynapse.Windows.Devices;
using OpenSynapse.Windows.Protocols;
using Xunit;

namespace OpenSynapse.Core.Tests;

public sealed class RazerHidDockLinkTests
{
    private const string ChildPath = "HID#VID_1532&PID_00AA#child";
    [Fact]
    public async Task DualLinkStatusReadReturnsEveryPairedProductId()
    {
        var transport = new RecordingTransport(request =>
        {
            var arguments = new byte[80];
            arguments[0] = 3;
            arguments[1] = 0x01;
            arguments[2] = 0x00;
            arguments[3] = 0xAA;
            arguments[4] = 0x00;
            arguments[5] = 0x00;
            arguments[6] = 0xAB;
            arguments[7] = 0x01;
            arguments[8] = 0xFF;
            arguments[9] = 0xFF;
            return Reply(request, 0x02, arguments);
        })
        {
            PrepareTransactions = true,
        };
        var service = new RazerHidDeviceService(RazerHidDeviceCatalog.BuiltIn, transport);
        var dock = RazerHidDeviceCatalog.BuiltIn.Find(0x1532, 0x00A4)!;

        var ids = await service.ReadLinkedProductIdsAsync(
            dock, "HID#VID_1532&PID_00A4#dock", default);

        Assert.Equal([0x00AA, 0x00AB], ids);
        Assert.Equal(80, transport.Requests[0][6]);
        Assert.Equal(0xE0, transport.Requests[0][2]);
        Assert.Equal(0x00, transport.Requests[0][7]);
        Assert.Equal(0xBF, transport.Requests[0][8]);
    }

    [Fact]
    public async Task IndependentlyIdentifiedChildBatteryAnswerAdmitsOnlyBatteryRead()
    {
        var transport = new RecordingTransport(request =>
        {
            if (request[7] != 0x07 || request[8] != 0x80)
            {
                throw new NotSupportedException();
            }
            return Reply(request, 0x02, 0x00, 0x80);
        });
        var service = new RazerHidDeviceService(RazerHidDeviceCatalog.BuiltIn, transport);

        var connection = await service.TryCreateLinkedConnectionAsync(
            0x00AA, [ChildPath], "dock-key", default);

        Assert.NotNull(connection);
        Assert.Equal(0x00AA, connection.Definition.ProductId);
        Assert.Equal(DeviceCategory.Mouse, connection.Definition.Category);
        Assert.Equal("Razer Basilisk V3 Pro", connection.Definition.DisplayName);
        Assert.True(connection.IsReady);
        Assert.Equal(ChildPath, connection.DevicePath);
        Assert.Contains("#linked-00AA", connection.InstanceId);
        Assert.Contains(RazerHidBackendCapability.BatteryRead, connection.Capabilities);
        Assert.Single(connection.Capabilities);
        Assert.Empty(connection.LightingZones);
    }

    [Fact]
    public async Task LinkedMouseIsNotShownWhenBatteryProbeFails()
    {
        var service = new RazerHidDeviceService(
            RazerHidDeviceCatalog.BuiltIn,
            new RecordingTransport(_ => throw new NotSupportedException()));

        var connection = await service.TryCreateLinkedConnectionAsync(
            0x00AA, [ChildPath], "dock-key", default);

        Assert.Null(connection);
    }

    [Fact]
    public async Task LinkedMouseUsesTheCollectionThatAnswersBattery()
    {
        var transport = new PathAwareTransport((path, request) =>
        {
            if (path != ChildPath)
            {
                throw new NotSupportedException();
            }
            return Reply(request, 0x02, 0x00, 0x80);
        });
        var service = new RazerHidDeviceService(RazerHidDeviceCatalog.BuiltIn, transport);

        var connection = await service.TryCreateLinkedConnectionAsync(
            0x00AA, ["HID#VID_1532&PID_00A4#dock", ChildPath], "dock-key", default);

        Assert.NotNull(connection);
        Assert.Equal(ChildPath, connection.DevicePath);
    }

    [Fact]
    public async Task DualLinkQueryIsSkippedForNonDockDevices()
    {
        var transport = new RecordingTransport(_ => throw new InvalidOperationException("must not query"));
        var service = new RazerHidDeviceService(RazerHidDeviceCatalog.BuiltIn, transport);
        var mouse = RazerHidDeviceCatalog.BuiltIn.Find(0x1532, 0x00AA)!;

        var ids = await service.ReadLinkedProductIdsAsync(mouse, "mouse-path", default);

        Assert.Empty(ids);
        Assert.Equal(0, transport.Count);
    }

    [Fact]
    public void OnlyTheVerifiedDockUsesTheDualLinkParentPath()
    {
        Assert.True(RazerHidDeviceService.IsDualLinkParent(0x00A4));
        Assert.False(RazerHidDeviceService.IsDualLinkParent(0x0088));
        Assert.False(RazerHidDeviceService.IsDualLinkParent(0x0086));
    }

    [Fact]
    public async Task UnprovenBasiliskReceiverDoesNotInheritTheSiblingLinker()
    {
        var transport = new RecordingTransport(request =>
        {
            var arguments = new byte[80];
            arguments[0] = 1;
            arguments[1] = 0x01;
            arguments[2] = 0x00;
            arguments[3] = 0x86;
            arguments[4] = 0xFF;
            arguments[5] = 0xFF;
            return Reply(request, 0x02, arguments);
        });
        var service = new RazerHidDeviceService(RazerHidDeviceCatalog.BuiltIn, transport);
        var receiver = RazerHidDeviceCatalog.BuiltIn.Find(0x1532, 0x0088)!;

        var ids = await service.ReadLinkedProductIdsAsync(receiver, "receiver-path", default);

        Assert.Empty(ids);
        Assert.Empty(transport.Requests);
    }

    [Fact]
    public async Task ParentBatteryReplyCannotAdmitMultipleUnidentifiedChildren()
    {
        var transport = new RecordingTransport(request => Reply(request, 0x02, 0x00, 0xFF));
        var service = new RazerHidDeviceService(RazerHidDeviceCatalog.BuiltIn, transport);
        foreach (var productId in new ushort[] { 0x00AA, 0x00AB })
            Assert.Null(await service.TryCreateLinkedConnectionAsync(productId,
                ["HID#VID_1532&PID_00A4#dock", "HID#VID_1532&PID_0088#receiver"], "dock-key", default));
        Assert.Empty(transport.Requests);
    }

    [Fact]
    public async Task ChildEndpointMustAnswerInTheCurrentAttempt()
    {
        var awake = true;
        var transport = new RecordingTransport(request => awake
            ? Reply(request, 0x02, 0x00, 0x80)
            : throw new NotSupportedException());
        var service = new RazerHidDeviceService(RazerHidDeviceCatalog.BuiltIn, transport);
        Assert.NotNull(await service.TryCreateLinkedConnectionAsync(0x00AA, [ChildPath], "dock-key", default));
        awake = false;
        Assert.Null(await service.TryCreateLinkedConnectionAsync(0x00AA, [ChildPath], "dock-key", default));
        awake = true;
        Assert.NotNull(await service.TryCreateLinkedConnectionAsync(0x00AA, [ChildPath], "dock-key", default));
    }

    [Theory]
    [InlineData("other-device", false)]
    [InlineData("DOCK-KEY", true)]
    public void DuplicateRemovalRequiresTheSamePhysicalEntity(string directKey, bool removesLinked)
    {
        var definition = RazerHidDeviceCatalog.BuiltIn.Find(0x1532, 0x00AA)!;
        RazerHidDeviceConnection Connection(string key) => new(definition, ChildPath, key,
            RazerHidEndpointState.Resolved, new HashSet<RazerHidBackendCapability>(),
            new Dictionary<RazerHidLedZone, RazerHidLightingZoneCapabilities>(), null);
        var linked = Connection("dock-key#linked-00AA");
        var direct = Connection(directKey);
        var connections = new List<RazerHidDeviceConnection> { linked, direct };
        RazerHidDeviceService.RemoveDuplicateLinkedConnections(connections);
        Assert.Equal(removesLinked ? 1 : 2, connections.Count);
        Assert.Contains(direct, connections);
        Assert.Equal(!removesLinked, connections.Contains(linked));
    }

    private static byte[] Reply(byte[] request, byte status, params byte[] arguments)
    {
        var response = new byte[RazerFeatureReport.Length];
        response[1] = status;
        response[2] = request[2];
        response[3] = request[3];
        response[4] = request[4];
        response[6] = request[6];
        response[7] = request[7];
        response[8] = request[8];
        arguments.CopyTo(response, RazerFeatureReport.ArgumentsOffset);
        response[89] = RazerFeatureReport.CalculateCrc(response);
        return response;
    }

    private sealed class RecordingTransport(Func<byte[], byte[]> handler) : IRazerFeatureTransport
    {
        private readonly Dictionary<string, int> _sequences = new(StringComparer.OrdinalIgnoreCase);

        public List<byte[]> Requests { get; } = [];
        public int Count => Requests.Count;
        public bool PrepareTransactions { get; init; }

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
            if (PrepareTransactions)
            {
                var sequence = _sequences.TryGetValue(devicePath, out var value) ? value : -1;
                bytes[2] = RazerFeatureTransport.PrepareTransactionId(
                    devicePath, bytes[2], bytes[7], bytes[8], ref sequence);
                _sequences[devicePath] = sequence;
            }
            Requests.Add(bytes);
            return Task.FromResult(handler(bytes));
        }
    }

    private sealed class PathAwareTransport(Func<string, byte[], byte[]> handler) : IRazerFeatureTransport
    {
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
            bool allowRemainingPacketsMismatch = false) =>
            Task.FromResult(handler(devicePath, request.ToArray()));
    }
}
