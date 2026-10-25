using OpenSynapse.Windows.Devices;
using OpenSynapse.Windows.Protocols;
using Xunit;

namespace OpenSynapse.Core.Tests;

public sealed class RazerHeadsetAudioServiceTests
{
    [Fact]
    public async Task ReadState_ReportsEveryAdvertisedValue()
    {
        var simulator = new HeadsetSimulator
        {
            Preset = 16,
            Bass = 7,
            MicMuted = true,
            MicVolume = 42,
            Source = RazerAudioSource.Usb,
            NoiseSuppression = true,
            AncState = RazerAncState.On,
            AncLevel = 4,
            Battery = 88,
            Charging = true,
        };
        var service = new RazerHeadsetAudioService(new FakeTransport(simulator.Handle), "test", TimeSpan.Zero);

        var state = await service.ReadStateAsync();

        Assert.Empty(state.Errors);
        Assert.Equal((byte)16, state.EqPreset);
        Assert.Equal<byte>(simulator.Bands, state.EqBands!);
        Assert.Equal((byte)7, state.Bass);
        Assert.True(state.MicMuted);
        Assert.Equal((byte)42, state.MicVolume);
        Assert.Equal(RazerAudioSource.Usb, state.AudioSource);
        Assert.True(state.NoiseSuppression);
        Assert.Equal(RazerAncState.On, state.AncState);
        Assert.Equal((byte)4, state.AncLevel);
        Assert.Equal((byte)88, state.BatteryPercent);
        Assert.True(state.Charging);
    }

    [Fact]
    public async Task SetEqPreset_RestoresThePreviousPresetWhenTheDeviceIgnoresTheWrite()
    {
        var simulator = new HeadsetSimulator { Preset = 1, IgnorePresetWrites = true };
        var transport = new FakeTransport(simulator.Handle);
        var service = new RazerHeadsetAudioService(transport, "test", TimeSpan.Zero);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.SetEqPresetAsync(3));

        var writes = transport.Requests.Where(request => request[8] == 0x02).ToArray();
        Assert.Equal(2, writes.Length);
        Assert.Equal((byte)1, writes[^1][10]);
    }

    [Fact]
    public async Task SetBass_WritesAndVerifiesTheReadback()
    {
        var simulator = new HeadsetSimulator { Bass = 1 };
        var transport = new FakeTransport(simulator.Handle);
        var service = new RazerHeadsetAudioService(transport, "test", TimeSpan.Zero);

        await service.SetBassAsync(9);

        Assert.Equal(9, simulator.Bass);
        Assert.Contains(transport.Requests, request => request[8] == 0x07 && request[10] == 9);
    }

    [Fact]
    public async Task ReadState_FallsBackToEightBandsWhenTenBandReadsFail()
    {
        var simulator = new HeadsetSimulator { TenBand = false };
        var service = new RazerHeadsetAudioService(new FakeTransport(simulator.Handle), "test", TimeSpan.Zero);

        var state = await service.ReadStateAsync();

        Assert.Equal(8, state.EqBands!.Count);
        Assert.Equal<byte>(simulator.Bands[..8], state.EqBands!);
    }

    [Fact]
    public async Task SetAnc_WritesStateAndLevel()
    {
        var simulator = new HeadsetSimulator();
        var transport = new FakeTransport(simulator.Handle);
        var service = new RazerHeadsetAudioService(transport, "test", TimeSpan.Zero);

        await service.SetAncAsync(RazerAncState.Ambient, 0x03);

        Assert.Equal(RazerAncState.Ambient, simulator.AncState);
        Assert.Equal(3, simulator.AncLevel);
        Assert.Contains(transport.Requests, request =>
            request[8] == 0x06 && request[10] == (byte)RazerAncState.Ambient && request[11] == 3);
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

    private sealed class HeadsetSimulator
    {
        public byte Preset { get; set; }
        public bool IgnorePresetWrites { get; init; }
        public bool TenBand { get; set; } = true;
        public byte[] Bands { get; set; } = [1, 2, 3, 4, 5, 6, 7, 8, 9, 10];
        public byte Bass { get; set; } = 3;
        public bool MicMuted { get; set; }
        public byte MicVolume { get; set; } = 50;
        public RazerAudioSource Source { get; set; } = RazerAudioSource.Usb;
        public bool NoiseSuppression { get; set; }
        public RazerAncState AncState { get; set; } = RazerAncState.Off;
        public byte AncLevel { get; set; } = 1;
        public byte Battery { get; set; } = 100;
        public bool Charging { get; set; }

        public byte[] Handle(byte[] request)
        {
            var command = request[8];
            var state = this;
            return command switch
            {
                0x82 => Response(request, 2, 0x00, state.Preset),
                0x02 => Write(request, () =>
                {
                    if (!state.IgnorePresetWrites)
                    {
                        state.Preset = request[10];
                    }
                }, state.Preset),
                0x84 when request[7] == 0x07 => Response(request, 2, 0x00, state.Charging ? (byte)1 : (byte)0),
                0x84 => EqBands(request),
                0x04 => SetEqBands(request),
                0x87 => Response(request, 2, 0x00, state.Bass),
                0x07 => Write(request, () => state.Bass = request[10], state.Bass),
                0x88 => Response(request, 2, 0x00, state.MicMuted ? (byte)1 : (byte)0),
                0x08 => Write(request, () => state.MicMuted = request[10] != 0, state.MicMuted ? (byte)1 : (byte)0),
                0x97 => Response(request, 2, 0x00, state.MicVolume),
                0x17 => Write(request, () => state.MicVolume = request[10], state.MicVolume),
                0x8A => Response(request, 2, 0x00, (byte)state.Source),
                0x0A => Write(request, () => state.Source = (RazerAudioSource)request[10], (byte)state.Source),
                0x9C => Response(request, 2, 0x00, state.NoiseSuppression ? (byte)1 : (byte)0),
                0x1C => Write(request, () => state.NoiseSuppression = request[10] != 0, state.NoiseSuppression ? (byte)1 : (byte)0),
                0x80 when request[7] == 0x07 => Response(request, 2, 0x00, state.Battery),
                0x81 => Response(request, 2, 1, 2),
                0x86 => Response(request, 3, 0x00, (byte)state.AncState, state.AncLevel),
                0x06 => Write(request, () =>
                {
                    state.AncState = (RazerAncState)request[10];
                    state.AncLevel = request[11];
                }, (byte)state.AncState, state.AncLevel),
                _ => throw new NotSupportedException($"Unexpected command 0x{command:X2}."),
            };
        }

        private byte[] EqBands(byte[] request)
        {
            if (request[6] == 11 && !TenBand)
            {
                throw new NotSupportedException("This headset exposes eight bands.");
            }

            var bands = TenBand ? Bands : Bands[..8];
            var response = new byte[91];
            response[1] = 0x02;
            response[2] = request[2];
            response[6] = (byte)(bands.Length + 1);
            response[7] = request[7];
            response[8] = request[8];
            response[9] = request[9];
            bands.CopyTo(response.AsSpan(10));
            response[89] = RazerFeatureReport.CalculateCrc(response);
            return response;
        }

        private byte[] SetEqBands(byte[] request)
        {
            var count = request[6] - 1;
            Bands = request[10..(10 + count)];
            TenBand = count == 10;
            return Response(request, request[6], request[9]);
        }

        private static byte[] Write(byte[] request, Action write, params byte[] values)
        {
            write();
            return Response(request, (byte)(values.Length + 1), [request[9], ..values]);
        }

        private static byte[] Response(byte[] request, byte dataSize, params byte[] arguments)
        {
            var response = new byte[91];
            response[1] = 0x02;
            response[2] = request[2];
            response[6] = dataSize;
            response[7] = request[7];
            response[8] = request[8];
            arguments.CopyTo(response.AsSpan(9));
            response[89] = RazerFeatureReport.CalculateCrc(response);
            return response;
        }
    }
}
