using OpenSynapse.Windows.Devices;
using OpenSynapse.Windows.Protocols;
using Xunit;

namespace OpenSynapse.Core.Tests;

/// <summary>
/// The shared mapping page reaches every protocol 30 BLE device through
/// <see cref="RazerBleDeviceService"/>. These tests pin the 08:80 / 08:84 / 08:04
/// transport frames and the read-back contract without opening Bluetooth.
/// </summary>
public sealed class RazerBleMappingServiceTests
{
    [Fact]
    public async Task ButtonListRequestUses08_80AndReturnsNonZeroIds()
    {
        var transport = new FakeTransport((frame, _) =>
        {
            Assert.Equal(new byte[] { 0x00, 0x00, 0x00, 0x00, 0x08, 0x80, 0x00, 0x00 }, frame);
            return Response(frame[0], 0x08, 0x80, 0x02, 0x01, 0x04, 0x07);
        });
        using var session = new RazerBleSession(transport, retryDelay: TimeSpan.Zero);
        using var service = new RazerBleDeviceService(session, "Bluetooth device");

        var ids = await service.GetMappingButtonIdsAsync();

        Assert.Equal(new byte[] { 0x01, 0x04, 0x07 }, ids);
        Assert.Single(transport.Frames);
    }

    [Fact]
    public async Task GetButtonAssignmentUses08_84AndParsesTheReply()
    {
        var transport = new FakeTransport((frame, _) =>
        {
            Assert.Equal(new byte[] { 0x00, 0x00, 0x00, 0x00, 0x08, 0x84, 0x00, 0x04 }, frame);
            return Response(frame[0], 0x08, 0x84, 0x02,
                0x04, 0x00, 0x01, 0x00, 0x01, 0x00, 0x05);
        });
        using var session = new RazerBleSession(transport, retryDelay: TimeSpan.Zero);
        using var service = new RazerBleDeviceService(session, "Bluetooth device");

        var assignment = await service.GetButtonAssignmentAsync(4);

        Assert.Equal((byte)4, assignment.ButtonId);
        Assert.Equal((byte)1, assignment.FunctionId);
        Assert.Equal(new byte[] { 0x05 }, assignment.Data);
    }

    [Fact]
    public async Task SetButtonAssignmentWrites08_04AndVerifiesTheReadBack()
    {
        var applied = (FunctionId: (byte)0, Data: Array.Empty<byte>());
        var transport = new FakeTransport((frame, _) =>
        {
            switch (frame[5])
            {
                case 0x04:
                    Assert.Equal((byte)0x08, frame[4]);
                    Assert.Equal(0, frame[6]);
                    Assert.Equal(4, frame[7]);
                    Assert.Equal((byte)1, frame[10]);
                    applied = (frame[11], frame[13..(13 + frame[12])]);
                    return Response(frame[0], 0x08, 0x04, 0x02);
                case 0x84:
                    Assert.Equal(
                        new byte[] { 0x01, 0x00, 0x00, 0x00, 0x08, 0x84, 0x00, 0x04 },
                        frame);
                    return Response(frame[0], 0x08, 0x84, 0x02,
                        [0x04, 0x00, 0x00, applied.FunctionId, 0x00,
                            (byte)applied.Data.Length, 0x00, .. applied.Data]);
                default:
                    throw new InvalidOperationException($"Unexpected command 0x{frame[5]:X2}.");
            }
        });
        using var session = new RazerBleSession(transport, retryDelay: TimeSpan.Zero);
        using var service = new RazerBleDeviceService(session, "Bluetooth device");

        await service.SetButtonAssignmentAsync(4, 1, 2, new byte[] { 0x06 });

        Assert.Equal((byte)2, applied.FunctionId);
        Assert.Equal(new byte[] { 0x06 }, applied.Data);
        Assert.Equal(2, transport.Frames.Count);
        Assert.Equal(
            new byte[]
            {
                0x00, 0x06, 0x00, 0x00, 0x08, 0x04, 0x00, 0x04,
                0x00, 0x04, 0x01, 0x02, 0x01, 0x06,
            },
            transport.Frames[0]);
        Assert.Equal(
            new byte[] { 0x01, 0x00, 0x00, 0x00, 0x08, 0x84, 0x00, 0x04 },
            transport.Frames[1]);
    }

    [Fact]
    public async Task SetButtonAssignmentThrowsWhenTheDeviceRejectsTheWrite()
    {
        var transport = new FakeTransport((frame, _) =>
            Response(frame[0], 0x08, 0x04, 0x03));
        using var session = new RazerBleSession(transport, retryDelay: TimeSpan.Zero);
        using var service = new RazerBleDeviceService(session, "Bluetooth device");

        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.SetButtonAssignmentAsync(4, 0, 1, new byte[] { 0x05 }));

        Assert.Contains("set button assignment", error.Message);
        Assert.Equal(2, transport.Frames.Count);
        Assert.All(transport.Frames, frame => Assert.Equal((byte)0x04, frame[5]));
        Assert.DoesNotContain(transport.Frames, frame => frame[5] == 0x84);
    }

    [Theory]
    [InlineData(0x02, 0x05)]
    [InlineData(0x01, 0x06)]
    public async Task SetButtonAssignmentThrowsWhenTheReadBackDiffers(
        byte readBackFunction,
        byte readBackData)
    {
        var transport = new FakeTransport((frame, _) => frame[5] switch
        {
            0x04 => Response(frame[0], 0x08, 0x04, 0x02),
            0x84 => Response(frame[0], 0x08, 0x84, 0x02,
                0x04, 0x00, readBackFunction, 0x00, 0x01, 0x00, readBackData),
            _ => throw new InvalidOperationException($"Unexpected command 0x{frame[5]:X2}."),
        });
        using var session = new RazerBleSession(transport, retryDelay: TimeSpan.Zero);
        using var service = new RazerBleDeviceService(session, "Bluetooth device");

        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.SetButtonAssignmentAsync(4, 0, 1, new byte[] { 0x05 }));

        Assert.Contains("different button assignment", error.Message);
        Assert.Equal(2, transport.Frames.Count);
    }

    [Fact]
    public Task EmptyButtonListReturnsNoMappableButtonsAndNoAssignmentReads() =>
        AssertNoMappableButtonsAsync([]);

    [Fact]
    public Task ZeroOnlyButtonListReturnsNoMappableButtonsAndNoAssignmentReads() =>
        AssertNoMappableButtonsAsync([0]);

    private static async Task AssertNoMappableButtonsAsync(byte[] payload)
    {
        var transport = new FakeTransport((frame, _) =>
        {
            Assert.Equal(new byte[] { 0x00, 0x00, 0x00, 0x00, 0x08, 0x80, 0x00, 0x00 }, frame);
            return Response(frame[0], 0x08, 0x80, 0x02, payload);
        });
        using var session = new RazerBleSession(transport, retryDelay: TimeSpan.Zero);
        using var service = new RazerBleDeviceService(session, "Bluetooth device");

        var ids = await service.GetMappingButtonIdsAsync();

        Assert.DoesNotContain(ids, id => id != 0);
        Assert.Single(transport.Frames);
        Assert.DoesNotContain(transport.Frames, frame => frame[5] == 0x84);
    }

    private static byte[] Response(
        byte transactionId,
        byte commandClass,
        byte commandId,
        byte status,
        params byte[] payload)
    {
        var reply = new byte[20 + payload.Length];
        reply[0] = transactionId;
        reply[1] = (byte)(payload.Length & 0xFF);
        reply[2] = (byte)(payload.Length >> 8);
        reply[4] = commandClass;
        reply[5] = commandId;
        reply[7] = status;
        payload.CopyTo(reply, 20);
        return reply;
    }

    private sealed class FakeTransport(
        Func<byte[], TimeSpan, byte[]?> handler) : IRazerBleTransport
    {
        public List<byte[]> Frames { get; } = [];

        public Task<byte[]?> ExchangeAsync(
            ReadOnlyMemory<byte> frame,
            TimeSpan timeout,
            CancellationToken cancellationToken)
        {
            var copy = frame.ToArray();
            Frames.Add(copy);
            return Task.FromResult(handler(copy, timeout));
        }

        public void Dispose()
        {
        }
    }
}
