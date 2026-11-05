using OpenSynapse.Windows.Devices;
using OpenSynapse.Windows.Protocols;
using Xunit;

namespace OpenSynapse.Core.Tests;

public sealed class RazerHeadsetAudioAdvancedTests
{
    [Fact]
    public async Task SetParametricEq_WritesTheSelectedBandPayload()
    {
        var simulator = new AudioSimulator();
        var transport = new FakeTransport(simulator.Handle);
        var service = new RazerHeadsetAudioService(transport, "test", TimeSpan.Zero);
        var setting = new RazerParametricEqBand(0, 0, 4, 1, 1_000, -25, 14, 1);

        await service.SetParametricEqAsync(setting);

        Assert.Equal(setting, simulator.ParametricBands[4]);
        var write = Assert.Single(transport.Requests, request => request[8] == 0x1A);
        Assert.Equal((0x09, 0x08, 0x1A), (write[6], write[7], write[8]));
        Assert.Equal([0, 0, 4, 1, 0x03, 0xE8, 0xE7, 14, 1], write[9..18]);
    }

    [Fact]
    public async Task SetMicCompressor_WritesAllThreeSettingsAsOnePayload()
    {
        var simulator = new AudioSimulator();
        var transport = new FakeTransport(simulator.Handle);
        var service = new RazerHeadsetAudioService(transport, "test", TimeSpan.Zero);
        var compressor = new RazerMicCompressor(1, 1, -20, 25, 10, 100);

        await service.SetMicCompressorAsync(compressor);

        Assert.Equal(compressor, simulator.MicCompressor);
        var write = Assert.Single(transport.Requests, request => request[8] == 0x1D);
        Assert.Equal((0x09, 0x08, 0x1D), (write[6], write[7], write[8]));
        Assert.Equal([1, 1, 0xEC, 0x00, 0x19, 0x00, 0x0A, 0x00, 0x64], write[9..18]);
    }

    [Fact]
    public async Task SetAudioVoicePrompts_WritesThePromptIndexAndEnabledState()
    {
        var simulator = new AudioSimulator { VoicePromptsEnabled = false };
        var transport = new FakeTransport(simulator.Handle);
        var service = new RazerHeadsetAudioService(transport, "test", TimeSpan.Zero);

        await service.SetAudioVoicePromptsAsync(true, promptIndex: 2);

        Assert.True(simulator.VoicePromptsEnabled);
        var write = Assert.Single(transport.Requests, request => request[8] == 0x10);
        Assert.Equal((0x03, 0x18, 0x10), (write[6], write[7], write[8]));
        Assert.Equal([0, 2, 1], write[9..12]);
    }

    [Fact]
    public async Task SetMicLimiter_WritesTheSignedThreshold()
    {
        var simulator = new AudioSimulator { MicLimiter = -6 };
        var transport = new FakeTransport(simulator.Handle);
        var service = new RazerHeadsetAudioService(transport, "test", TimeSpan.Zero);

        await service.SetMicLimiterAsync(-12);

        Assert.Equal(-12, simulator.MicLimiter);
        var write = Assert.Single(transport.Requests, request => request[8] == 0x20);
        Assert.Equal((0x02, 0x08, 0x20), (write[6], write[7], write[8]));
        Assert.Equal((0, 0xF4), (write[9], write[10]));
    }

    [Fact]
    public async Task ReadState_PopulatesEveryAdvancedSetting()
    {
        var simulator = new AudioSimulator();
        var service = new RazerHeadsetAudioService(
            new FakeTransport(simulator.Handle), "test", TimeSpan.Zero);

        var state = await service.ReadStateAsync();

        Assert.Empty(state.Errors);
        Assert.Equal(simulator.ParametricBands[0], state.ParametricEq);
        Assert.Equal(simulator.MicCompressor, state.MicCompressor);
        Assert.Equal((byte)65, state.MicDeEsser);
        Assert.Equal((RazerMicReverbRoomMode.Large, (byte)35), state.MicReverb);
        Assert.Equal((sbyte)-12, state.MicLimiter);
        Assert.True(state.Dnd);
        Assert.Equal(((byte)2, true), state.VoicePrompts);
        Assert.True(state.AutoPause);
        Assert.True(state.GestureControl);
        Assert.True(state.Dolby);
        Assert.True(state.NightMode);
    }

    [Fact]
    public async Task ReadState_RecordsARejectedAdvancedReadAndLeavesItsFieldNull()
    {
        var simulator = new AudioSimulator { RejectCommand = (0x18, 0x85) };
        var service = new RazerHeadsetAudioService(
            new FakeTransport(simulator.Handle), "test", TimeSpan.Zero);

        var state = await service.ReadStateAsync();

        Assert.Null(state.Dnd);
        Assert.Single(state.Errors);
        Assert.Contains("audio DND", state.Errors[0]);
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
            var response = handler(copy);
            // Several protocol-25 getters answer with more arguments than their request declares.
            var valid = response[6] == copy[6]
                ? RazerFeatureReport.IsSuccessfulResponse(copy, response, response[6])
                : RazerFeatureReport.Matches(copy, response) && response[1] is 0x01 or 0x02;
            if (!valid)
            {
                throw new InvalidOperationException($"Device response status: 0x{response[1]:X2}.");
            }

            return Task.FromResult(response);
        }
    }

    private sealed class AudioSimulator
    {
        public byte[] EqBandValues { get; } = [1, 2, 3, 4, 5, 6, 7, 8, 9, 10];
        public RazerParametricEqBand[] ParametricBands { get; } =
            Enumerable.Range(0, RazerHeadsetAudioProtocol.ParametricEqBandCount)
                .Select(index => new RazerParametricEqBand(
                    0, 0, (byte)index, 0, (ushort)(31 << index), 0, 10, 1))
                .ToArray();

        public RazerMicCompressor MicCompressor { get; set; } = new(0, 0, 0, 10, 0, 0);
        public byte MicDeEsser { get; set; } = 65;
        public (RazerMicReverbRoomMode RoomMode, byte Intensity) MicReverb { get; set; } =
            (RazerMicReverbRoomMode.Large, 35);
        public sbyte MicLimiter { get; set; } = -12;
        public bool Dnd { get; set; } = true;
        public byte VoicePromptIndex { get; set; } = 2;
        public bool VoicePromptsEnabled { get; set; } = true;
        public bool AutoPause { get; set; } = true;
        public bool GestureControl { get; set; } = true;
        public bool Dolby { get; set; } = true;
        public bool NightMode { get; set; } = true;
        public (byte Class, byte Command)? RejectCommand { get; set; }

        public AudioSimulator()
        {
            ParametricBands[0] = new(0, 0, 0, 1, 31, -12, 10, 1);
            ParametricBands[4] = new(0, 0, 4, 0, 1_000, 0, 14, 1);
        }

        public byte[] Handle(byte[] request)
        {
            var commandClass = request[7];
            var command = request[8];
            if (RejectCommand == (commandClass, command))
            {
                var rejected = Response(request, request[6]);
                rejected[1] = 0x03;
                rejected[89] = RazerFeatureReport.CalculateCrc(rejected);
                return rejected;
            }

            return (commandClass, command) switch
            {
                (0x00, 0x81) => Response(request, 2, 1, 2),
                (0x07, 0x80) => Response(request, 2, 0x05, 255),
                (0x07, 0x84) => Response(request, 2, 0, 1),
                (0x08, 0x82) => Response(request, 2, 0, 16),
                (0x08, 0x84) => EqBands(request),
                (0x08, 0x87) => Response(request, 2, 0, 7),
                (0x08, 0x88) => Response(request, 2, 0, 1),
                (0x08, 0x97) => Response(request, 2, 0, 42),
                (0x08, 0x8A) => Response(request, 2, 0, (byte)RazerAudioSource.Usb),
                (0x08, 0x96) => Response(request, 3, 0, 3, 1),
                (0x18, 0x86) => Response(request, 3, 0, (byte)RazerAncState.On, 4),
                (0x08, 0x9A) => ParametricEq(request),
                (0x08, 0x1A) => SetParametricEq(request),
                (0x08, 0x9D) => Compressor(request),
                (0x08, 0x1D) => SetCompressor(request),
                (0x08, 0x9E) => Response(request, 2, 0, MicDeEsser),
                (0x08, 0x9F) => Response(
                    request, 3, 0, (byte)MicReverb.RoomMode, MicReverb.Intensity),
                (0x08, 0xA0) => Response(request, 2, 0, unchecked((byte)MicLimiter)),
                (0x08, 0x20) => SetLimiter(request),
                (0x18, 0x85) => Response(request, 2, 0, Dnd ? (byte)1 : (byte)0),
                (0x18, 0x90) => Response(
                    request, 3, 0, VoicePromptIndex, VoicePromptsEnabled ? (byte)1 : (byte)0),
                (0x18, 0x10) => SetVoicePrompts(request),
                (0x18, 0x9E) => Response(request, 2, 0, AutoPause ? (byte)1 : (byte)0),
                (0x18, 0xA2) => Response(request, 2, 0, GestureControl ? (byte)1 : (byte)0),
                (0x08, 0x8C) => Response(request, 11, 0, Dolby ? (byte)1 : (byte)0),
                (0x08, 0x92) => Response(request, 2, 0, NightMode ? (byte)1 : (byte)0),
                _ => throw new NotSupportedException(
                    $"Unexpected command {commandClass:X2}:{command:X2}."),
            };
        }

        private byte[] EqBands(byte[] request)
        {
            var arguments = new byte[EqBandValues.Length + 1];
            EqBandValues.CopyTo(arguments, 1);
            return Response(request, (byte)arguments.Length, arguments);
        }

        private byte[] ParametricEq(byte[] request)
        {
            var band = ParametricBands[request[11]];
            return Response(
                request,
                9,
                band.Profile,
                band.Direction,
                band.Index,
                band.BandStatus,
                (byte)(band.Frequency >> 8),
                (byte)band.Frequency,
                unchecked((byte)band.GainTenths),
                band.QFactorTenths,
                band.FilterType);
        }

        private byte[] SetParametricEq(byte[] request)
        {
            ParametricBands[request[11]] = new RazerParametricEqBand(
                request[9],
                request[10],
                request[11],
                request[12],
                (ushort)((request[13] << 8) | request[14]),
                unchecked((sbyte)request[15]),
                request[16],
                request[17]);
            return Response(request, 1, 0);
        }

        private byte[] Compressor(byte[] request) =>
            Response(
                request,
                9,
                MicCompressor.Profile,
                MicCompressor.Mode,
                unchecked((byte)MicCompressor.Db),
                (byte)(MicCompressor.RatioTenths >> 8),
                (byte)MicCompressor.RatioTenths,
                (byte)(MicCompressor.AttackTime >> 8),
                (byte)MicCompressor.AttackTime,
                (byte)(MicCompressor.ReleaseTime >> 8),
                (byte)MicCompressor.ReleaseTime);

        private byte[] SetCompressor(byte[] request)
        {
            MicCompressor = new RazerMicCompressor(
                request[9],
                request[10],
                unchecked((sbyte)request[11]),
                (ushort)((request[12] << 8) | request[13]),
                (ushort)((request[14] << 8) | request[15]),
                (ushort)((request[16] << 8) | request[17]));
            return Response(request, 1, 0);
        }

        private byte[] SetLimiter(byte[] request)
        {
            MicLimiter = unchecked((sbyte)request[10]);
            return Response(request, 1, 0);
        }

        private byte[] SetVoicePrompts(byte[] request)
        {
            VoicePromptIndex = request[10];
            VoicePromptsEnabled = request[11] != 0;
            return Response(request, 1, 0);
        }

        private static byte[] Response(byte[] request, byte dataSize, params byte[] arguments)
        {
            var response = new byte[RazerFeatureReport.Length];
            response[1] = 0x02;
            response[2] = request[2];
            response[3] = request[3];
            response[4] = request[4];
            response[6] = dataSize;
            response[7] = request[7];
            response[8] = request[8];
            arguments.CopyTo(response.AsSpan(RazerFeatureReport.ArgumentsOffset));
            response[89] = RazerFeatureReport.CalculateCrc(response);
            return response;
        }
    }
}
