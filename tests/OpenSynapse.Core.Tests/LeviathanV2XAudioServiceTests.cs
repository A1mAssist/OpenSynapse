using OpenSynapse.Windows.Devices;
using OpenSynapse.Windows.Protocols;
using Xunit;

namespace OpenSynapse.Core.Tests;

public sealed class LeviathanV2XAudioServiceTests
{
    [Fact]
    public async Task SetEqPreset_RestoresThePreviousPresetWhenReadbackDoesNotChange()
    {
        var simulator = new AudioSimulator
        {
            Preset = (byte)LeviathanV2XEqPreset.Game,
            IgnorePresetWrites = true,
        };
        var transport = new FakeTransport(simulator.Handle);
        var service = new LeviathanV2XAudioService(transport, "test", TimeSpan.Zero);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.SetEqPresetAsync(LeviathanV2XEqPreset.Movie));

        var presetWrites = transport.Requests.Where(request => request[8] == 0x02).ToArray();
        Assert.Equal(2, presetWrites.Length);
        Assert.Equal((byte)LeviathanV2XEqPreset.Game, presetWrites[^1][10]);
    }

    [Fact]
    public async Task SetEqBands_SelectsCustomAndWritesTheTenBandProfile()
    {
        var simulator = new AudioSimulator();
        var transport = new FakeTransport(simulator.Handle);
        var service = new LeviathanV2XAudioService(transport, "test", TimeSpan.Zero);
        byte[] desired = [5, 15, 25, 35, 45, 55, 65, 75, 85, 95];

        await service.SetEqBandsAsync(desired);

        Assert.Equal((byte)LeviathanV2XEqPreset.Custom, simulator.Preset);
        Assert.Equal(desired, simulator.Bands);
        Assert.Contains(transport.Requests, request =>
            request[8] == 0x02 &&
            request[9] == LeviathanV2XProtocol.ReadProfile &&
            request[10] == (byte)LeviathanV2XEqPreset.Custom);
        Assert.Contains(transport.Requests, request =>
            request[8] == 0x04 &&
            request[9] == LeviathanV2XProtocol.WriteProfile &&
            request[10..20].SequenceEqual(desired));
        Assert.All(transport.Requests.Where(request => request[8] is 0x82 or 0x84),
            request => Assert.Equal(LeviathanV2XProtocol.ReadProfile, request[9]));
    }

    [Fact]
    public async Task SetEqBands_RestoresTheCustomCurveWhenThePreviousPresetWasCustom()
    {
        byte[] original = [1, 2, 3, 4, 5, 6, 7, 8, 9, 10];
        var simulator = new AudioSimulator
        {
            Preset = (byte)LeviathanV2XEqPreset.Custom,
            Bands = original,
            IgnoreBandWrites = true,
        };
        var transport = new FakeTransport(simulator.Handle);
        var service = new LeviathanV2XAudioService(transport, "test", TimeSpan.Zero);
        byte[] desired = [5, 15, 25, 35, 45, 55, 65, 75, 85, 95];

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.SetEqBandsAsync(desired));

        var bandWrites = transport.Requests.Where(request => request[8] == 0x04).ToArray();
        Assert.Equal(2, bandWrites.Length);
        Assert.Equal(original, bandWrites[^1][10..20]);
    }

    [Fact]
    public async Task SetBass_WritesProfileOneAndReadsProfileZero()
    {
        var simulator = new AudioSimulator { Bass = 3 };
        var transport = new FakeTransport(simulator.Handle);
        var service = new LeviathanV2XAudioService(transport, "test", TimeSpan.Zero);

        await service.SetBassAsync(4);

        Assert.Equal(4, simulator.Bass);
        var write = Assert.Single(transport.Requests, request => request[8] == 0x07);
        Assert.Equal(LeviathanV2XProtocol.WriteProfile, write[9]);
        Assert.Equal(4, write[10]);
        Assert.All(transport.Requests.Where(request => request[8] == 0x87),
            request => Assert.Equal(LeviathanV2XProtocol.ReadProfile, request[9]));
    }

    private sealed class FakeTransport(Func<byte[], byte[]> handler) : IRazerFeatureTransport
    {
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
            var copy = request.ToArray();
            Requests.Add(copy);
            return Task.FromResult(handler(copy));
        }
    }

    private sealed class AudioSimulator
    {
        public byte Preset { get; set; }
        public byte[] Bands { get; set; } = Enumerable.Repeat((byte)50, 10).ToArray();
        public int Bass { get; set; } = 3;
        public bool IgnorePresetWrites { get; init; }
        public bool IgnoreBandWrites { get; init; }

        public byte[] Handle(byte[] request)
        {
            var transaction = request[2];
            var command = request[8];
            var arguments = request.AsSpan(9, request[6]).ToArray();
            return command switch
            {
                0x82 => Response(transaction, 0x02, 0x08, 0x82, arguments[0], Preset),
                0x02 => HandleSetPreset(arguments, transaction),
                0x84 => HandleGetBands(arguments, transaction),
                0x04 => HandleSetBands(arguments, transaction),
                0x87 => Response(transaction, 0x02, 0x08, 0x87, arguments[0], (byte)Bass),
                0x07 => HandleSetBass(arguments, transaction),
                _ => Response(transaction, 0x02, 0x08, command, arguments[0], 0x00),
            };
        }

        private byte[] HandleSetPreset(byte[] arguments, byte transaction)
        {
            if (!IgnorePresetWrites)
            {
                Preset = arguments[1];
            }

            return Response(transaction, 0x02, 0x08, 0x02, arguments[0], Preset);
        }

        private byte[] HandleGetBands(byte[] arguments, byte transaction) =>
            Response(transaction, 0x0B, 0x08, 0x84, [arguments[0], .. Bands]);

        private byte[] HandleSetBands(byte[] arguments, byte transaction)
        {
            if (!IgnoreBandWrites)
            {
                Bands = arguments[1..11];
            }

            return Response(transaction, 0x0B, 0x08, 0x04, [arguments[0], .. Bands]);
        }

        private byte[] HandleSetBass(byte[] arguments, byte transaction)
        {
            Bass = arguments[1];
            return Response(transaction, 0x02, 0x08, 0x07, arguments);
        }
    }

    private static byte[] Response(
        byte transaction,
        byte dataSize,
        byte commandClass,
        byte commandId,
        params byte[] arguments)
    {
        var response = new byte[RazerFeatureReport.Length];
        response[0] = LeviathanV2XProtocol.ReportId;
        response[1] = 0x02;
        response[2] = transaction;
        response[6] = dataSize;
        response[7] = commandClass;
        response[8] = commandId;
        arguments.CopyTo(response, RazerFeatureReport.ArgumentsOffset);
        response[89] = RazerFeatureReport.CalculateCrc(response);
        return response;
    }
}
