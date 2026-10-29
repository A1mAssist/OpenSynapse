using OpenSynapse.Core.Devices;
using OpenSynapse.Windows.Devices;
using OpenSynapse.Windows.Protocols;
using Xunit;

namespace OpenSynapse.Core.Tests;

public sealed class OpenRazerDockLinkTests
{
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
        });
        var service = new OpenRazerDeviceService(OpenRazerDeviceCatalog.BuiltIn, transport);
        var dock = OpenRazerDeviceCatalog.BuiltIn.Find(0x1532, 0x00A4)!;

        var ids = await service.ReadLinkedProductIdsAsync(dock, "dock-path", default);

        Assert.Equal([0x00AA, 0x00AB], ids);
        Assert.Equal(80, transport.Requests[0][6]);
        Assert.Equal(0x00, transport.Requests[0][7]);
        Assert.Equal(0xBF, transport.Requests[0][8]);
    }

    [Fact]
    public async Task LinkedMouseBecomesAReadyMouseCardWhenBatteryAnswers()
    {
        var transport = new RecordingTransport(request =>
        {
            if (request[7] != 0x07 || request[8] != 0x80)
            {
                throw new NotSupportedException();
            }
            return Reply(request, 0x02, 0x00, 0x80);
        });
        var service = new OpenRazerDeviceService(OpenRazerDeviceCatalog.BuiltIn, transport);

        var connection = await service.TryCreateLinkedConnectionAsync(
            0x00AA, ["dock-path"], "dock-key", default);

        Assert.NotNull(connection);
        Assert.Equal(0x00AA, connection.Definition.ProductId);
        Assert.Equal(DeviceCategory.Mouse, connection.Definition.Category);
        Assert.Equal("Razer Basilisk V3 Pro", connection.Definition.DisplayName);
        Assert.True(connection.IsReady);
        Assert.Equal("dock-path", connection.DevicePath);
        Assert.Contains("#linked-00AA", connection.InstanceId);
        Assert.Contains(OpenRazerBackendCapability.BatteryRead, connection.Capabilities);
        Assert.Contains(OpenRazerBackendCapability.DpiRead, connection.Capabilities);
        Assert.Contains(OpenRazerBackendCapability.PollingRateRead, connection.Capabilities);
    }

    [Fact]
    public async Task LinkedMouseIsNotShownWhenBatteryProbeFails()
    {
        var service = new OpenRazerDeviceService(
            OpenRazerDeviceCatalog.BuiltIn,
            new RecordingTransport(_ => throw new NotSupportedException()));

        var connection = await service.TryCreateLinkedConnectionAsync(
            0x00AA, ["dock-path"], "dock-key", default);

        Assert.Null(connection);
    }

    [Fact]
    public async Task LinkedMouseUsesTheCollectionThatAnswersBattery()
    {
        var transport = new PathAwareTransport((path, request) =>
        {
            if (path != "mouse-path")
            {
                throw new NotSupportedException();
            }
            return Reply(request, 0x02, 0x00, 0x80);
        });
        var service = new OpenRazerDeviceService(OpenRazerDeviceCatalog.BuiltIn, transport);

        var connection = await service.TryCreateLinkedConnectionAsync(
            0x00AA, ["dock-path", "mouse-path"], "dock-key", default);

        Assert.NotNull(connection);
        Assert.Equal("mouse-path", connection.DevicePath);
    }

    [Fact]
    public async Task DualLinkQueryIsSkippedForNonDockDevices()
    {
        var transport = new RecordingTransport(_ => throw new InvalidOperationException("must not query"));
        var service = new OpenRazerDeviceService(OpenRazerDeviceCatalog.BuiltIn, transport);
        var mouse = OpenRazerDeviceCatalog.BuiltIn.Find(0x1532, 0x00AA)!;

        var ids = await service.ReadLinkedProductIdsAsync(mouse, "mouse-path", default);

        Assert.Empty(ids);
        Assert.Equal(0, transport.Count);
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
        public List<byte[]> Requests { get; } = [];
        public int Count => Requests.Count;

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
