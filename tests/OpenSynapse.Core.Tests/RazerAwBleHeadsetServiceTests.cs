using OpenSynapse.Windows.Devices;
using OpenSynapse.Windows.Protocols;
using Xunit;

namespace OpenSynapse.Core.Tests;

public sealed class RazerAwBleHeadsetServiceTests
{
    [Fact]
    public async Task ReadBattery_ReturnsTheHeadsetLevel()
    {
        var transport = new FakeTransport(new byte[] { 0x21, 0x02, 0x01, 87 });
        var service = CreateService(transport);

        var state = await service.ReadBatteryAsync();

        Assert.Equal((byte)87, state.HeadsetPercent);
        Assert.Equal(new byte[] { 0x21, 0x00, 0x00 }, Assert.Single(transport.Frames));
    }

    [Fact]
    public async Task ReadBattery_ReturnsBothEarbudLevels()
    {
        var service = CreateService(new FakeTransport(new byte[] { 0x21, 0x02, 0x02, 80, 79 }));

        var state = await service.ReadBatteryAsync();

        Assert.Equal((byte)80, state.RightEarbudPercent);
        Assert.Equal((byte)79, state.LeftEarbudPercent);
    }

    [Fact]
    public async Task ReadBattery_FailsWhenTheDeviceDoesNotAnswer()
    {
        var service = CreateService(new FakeTransport(new byte[]?[] { null }));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.ReadBatteryAsync());

        Assert.Contains("did not answer", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReadBattery_FailsOnAnInvalidReply()
    {
        var service = CreateService(new FakeTransport(new byte[] { 0x25, 0x02, 0x01, 1 }));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.ReadBatteryAsync());

        Assert.Contains("invalid battery reply", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReadVolume_ReturnsTheBundleValue()
    {
        var transport = new FakeTransport(new byte[] { 0x11, 0x02, 0x01, 55 });
        var service = CreateService(transport);

        Assert.Equal((byte)55, await service.ReadVolumeAsync());
        Assert.Equal(new byte[] { 0x11, 0x00, 0x00 }, Assert.Single(transport.Frames));
    }

    [Fact]
    public async Task SetVolume_WritesAcknowledgesAndReadsBack()
    {
        var transport = new FakeTransport(
            new byte[] { 0x11, 0x02, 0x01, 40 },
            new byte[] { 0x91, 0x02, 0x01, 55 },
            new byte[] { 0x11, 0x02, 0x01, 55 });
        var service = CreateService(transport);

        Assert.Equal((byte)55, await service.SetVolumeAsync(55));
        Assert.Equal(new byte[] { 0x11, 0x00, 0x00 }, transport.Frames[0]);
        Assert.Equal(new byte[] { 0x91, 0x00, 0x01, 55 }, transport.Frames[1]);
        Assert.Equal(new byte[] { 0x11, 0x00, 0x00 }, transport.Frames[2]);
    }

    [Fact]
    public async Task SetVolume_SkipsTheWriteWhenTheValueAlreadyMatches()
    {
        var transport = new FakeTransport(new byte[] { 0x11, 0x02, 0x01, 55 });
        var service = CreateService(transport);

        Assert.Equal((byte)55, await service.SetVolumeAsync(55));
        Assert.Single(transport.Frames);
    }

    [Fact]
    public async Task SetGameMode_RestoresThePreviousValueWhenTheStatusIsRejected()
    {
        var transport = new FakeTransport(
            new byte[] { 0x14, 0x02, 0x01, 0 },
            new byte[] { 0x94, 0x02, 0x01, 1 },
            new byte[] { 0x94, 0x02, 0x01, 0 });
        var service = CreateService(transport);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.SetGameModeAsync(true));

        Assert.Equal(3, transport.Frames.Count);
        Assert.Equal(new byte[] { 0x94, 0x00, 0x01, 0 }, transport.Frames[2]);
    }

    [Fact]
    public async Task SetVolume_RestoresThePreviousValueWhenReadbackDiffers()
    {
        var transport = new FakeTransport(
            new byte[] { 0x11, 0x02, 0x01, 40 },
            new byte[] { 0x91, 0x02, 0x01, 0 },
            new byte[] { 0x11, 0x02, 0x01, 41 },
            new byte[] { 0x91, 0x02, 0x01, 0 });
        var service = CreateService(transport);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.SetVolumeAsync(55));

        Assert.Contains("readback did not match", exception.Message, StringComparison.Ordinal);
        Assert.Equal(4, transport.Frames.Count);
        Assert.Equal(new byte[] { 0x91, 0x00, 0x01, 40 }, transport.Frames[3]);
    }

    [Fact]
    public async Task SidetoneUsesTheBundleZeroToFifteenScaling()
    {
        var transport = new FakeTransport(
            new byte[] { 0x19, 0x02, 0x01, 0 },
            new byte[] { 0x99, 0x02, 0x01, 7 },
            new byte[] { 0x19, 0x02, 0x01, 7 });
        var service = CreateService(transport);

        Assert.Equal((byte)47, await service.SetSidetoneAsync(50));
        Assert.Equal(new byte[] { 0x99, 0x00, 0x01, 7 }, transport.Frames[1]);
    }

    [Fact]
    public async Task GameModeWritesZeroOrOne()
    {
        var transport = new FakeTransport(
            new byte[] { 0x14, 0x02, 0x01, 0 },
            new byte[] { 0x94, 0x02, 0x01, 0 },
            new byte[] { 0x14, 0x02, 0x01, 1 });
        var service = CreateService(transport);

        Assert.True(await service.SetGameModeAsync(true));
        Assert.Equal(new byte[] { 0x94, 0x00, 0x01, 1 }, transport.Frames[1]);
    }

    [Fact]
    public async Task EqPresetWritesTheBundlePresetByte()
    {
        var transport = new FakeTransport(
            new byte[] { 0x13, 0x02, 0x01, 0 },
            new byte[] { 0x93, 0x02, 0x01, 0 },
            new byte[] { 0x13, 0x02, 0x01, 10 });
        var service = CreateService(transport);

        Assert.Equal((byte)10, await service.SetEqPresetAsync(10));
        Assert.Equal(new byte[] { 0x93, 0x00, 0x01, 10 }, transport.Frames[1]);
    }

    [Fact]
    public async Task UnknownEqPresetIsRejectedBeforeTouchingTheTransport()
    {
        var transport = new FakeTransport();
        var service = CreateService(transport);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => service.SetEqPresetAsync(14));
        Assert.Empty(transport.Frames);
    }

    [Fact]
    public async Task AutoPowerOffWritesTheBundleStepIndex()
    {
        var transport = new FakeTransport(
            new byte[] { 0x27, 0x02, 0x01, 0 },
            new byte[] { 0xA7, 0x02, 0x01, 0 },
            new byte[] { 0x27, 0x02, 0x01, 3 });
        var service = CreateService(transport);

        Assert.Equal(30, await service.SetAutoPowerOffMinutesAsync(30));
        Assert.Equal(new byte[] { 0xA7, 0x00, 0x01, 3 }, transport.Frames[1]);
    }

    [Fact]
    public async Task ChromaBrightnessUsesTheBundlePercentScaling()
    {
        var transport = new FakeTransport(
            new byte[] { 0x41, 0x02, 0x01, 0 },
            new byte[] { 0xC1, 0x02, 0x01, 127 },
            new byte[] { 0x41, 0x02, 0x01, 127 });
        var service = CreateService(transport);

        Assert.Equal((byte)50, await service.SetChromaBrightnessAsync(50));
        Assert.Equal(new byte[] { 0xC1, 0x00, 0x01, 127 }, transport.Frames[1]);
    }

    [Fact]
    public async Task ReadFirmware_ReturnsTheFourByteVersion()
    {
        var transport = new FakeTransport(new byte[] { 0x02, 0x02, 0x04, 1, 2, 3, 4 });
        var service = CreateService(transport);

        var version = await service.ReadFirmwareAsync();

        Assert.Equal("1.2.3.4", version.ToString());
        Assert.Equal(new byte[] { 0x02, 0x00, 0x00 }, Assert.Single(transport.Frames));
    }

    [Fact]
    public async Task ReadSerial_ReturnsPrintableAscii()
    {
        var service = CreateService(new FakeTransport(
            new byte[] { 0x00, 0x02, 0x04, 65, 66, 67, 68 }));

        Assert.Equal("ABCD", await service.ReadSerialAsync());
    }

    [Fact]
    public async Task ReadConnection_HandlesHeadsetAndEarbuds()
    {
        var headset = CreateService(new FakeTransport(
            new byte[] { 0x25, 0x02, 0x01, 1 }));
        var headsetState = await headset.ReadConnectionAsync();
        Assert.Equal(RazerAwHeadsetConnectionStatus.BleConnected, headsetState.Headset);

        var earbuds = CreateService(new FakeTransport(
            new byte[] { 0x25, 0x02, 0x02, 0, 2 }));
        var earbudState = await earbuds.ReadConnectionAsync();
        Assert.Equal(RazerAwEarbudConnectionStatus.ConnectedAsMaster, earbudState.Right);
        Assert.Equal(RazerAwEarbudConnectionStatus.NoConnection, earbudState.Left);
    }

    [Fact]
    public async Task ReadCharging_ReturnsTheBundleEnum()
    {
        var service = CreateService(new FakeTransport(
            new byte[] { 0x29, 0x02, 0x01, 1 }));

        Assert.Equal(RazerAwChargingStatus.Charging, await service.ReadChargingAsync());
    }

    [Fact]
    public async Task Bass_UsesReadWriteReadback()
    {
        var transport = new FakeTransport(
            new byte[] { 0x1B, 0x02, 0x01, 40 },
            new byte[] { 0x9B, 0x02, 0x01, 55 },
            new byte[] { 0x1B, 0x02, 0x01, 55 });
        var service = CreateService(transport);

        Assert.Equal((byte)55, await service.SetBassAsync(55));
        Assert.Equal(new byte[] { 0x9B, 0x00, 0x01, 55 }, transport.Frames[1]);
    }

    [Fact]
    public async Task Haptics_UsesReadWriteReadback()
    {
        var transport = new FakeTransport(
            new byte[] { 0x54, 0x02, 0x01, 2 },
            new byte[] { 0xD4, 0x02, 0x01, 5 },
            new byte[] { 0x54, 0x02, 0x01, 5 });
        var service = CreateService(transport);

        Assert.Equal((byte)5, await service.SetHapticsAsync(5));
        Assert.Equal(new byte[] { 0xD4, 0x00, 0x01, 5 }, transport.Frames[1]);
    }

    [Fact]
    public async Task Haptics_RejectsLevelsOutsideTheBundleRange()
    {
        var service = CreateService(new FakeTransport(
            new byte[] { 0x54, 0x02, 0x01, 7 }));

        await Assert.ThrowsAsync<InvalidDataException>(() => service.ReadHapticsAsync());
    }

    [Fact]
    public async Task MicEqPreset_UsesTheBundlePresetByte()
    {
        var transport = new FakeTransport(
            new byte[] { 0x16, 0x02, 0x01, 0 },
            new byte[] { 0x96, 0x02, 0x01, 0xFF },
            new byte[] { 0x16, 0x02, 0x01, 2 });
        var service = CreateService(transport);

        Assert.Equal((byte)2, await service.SetMicEqPresetAsync(2));
        Assert.Equal(new byte[] { 0x96, 0x00, 0x01, 2 }, transport.Frames[1]);
    }

    [Fact]
    public async Task CustomEq_UsesTheTenByteOffsetLayout()
    {
        sbyte[] target = [-5, -4, -3, -2, -1, 0, 1, 2, 3, 5];
        var transport = new FakeTransport(
            new byte[] { 0x15, 0x02, 0x0A, 5, 5, 5, 5, 5, 5, 5, 5, 5, 5 },
            new byte[] { 0x95, 0x02, 0x01, 0xFF },
            new byte[] { 0x15, 0x02, 0x0A, 0, 1, 2, 3, 4, 5, 6, 7, 8, 10 });
        var service = CreateService(transport);

        Assert.Equal(target, await service.SetCustomEqBandsAsync(target));
        Assert.Equal(
            new byte[] { 0x95, 0x00, 0x0A, 0, 1, 2, 3, 4, 5, 6, 7, 8, 10 },
            transport.Frames[1]);
    }

    [Fact]
    public async Task CustomEq_RestoresThePreviousBandsWhenReadbackDiffers()
    {
        sbyte[] target = [-5, -4, -3, -2, -1, 0, 1, 2, 3, 5];
        var transport = new FakeTransport(
            new byte[] { 0x15, 0x02, 0x0A, 5, 5, 5, 5, 5, 5, 5, 5, 5, 5 },
            new byte[] { 0x95, 0x02, 0x01, 0 },
            new byte[] { 0x15, 0x02, 0x0A, 5, 5, 5, 5, 5, 5, 5, 5, 5, 5 },
            new byte[] { 0x95, 0x02, 0x01, 0 });
        var service = CreateService(transport);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.SetCustomEqBandsAsync(target));

        Assert.Equal(4, transport.Frames.Count);
        Assert.Equal(
            new byte[] { 0x95, 0x00, 0x0A, 5, 5, 5, 5, 5, 5, 5, 5, 5, 5 },
            transport.Frames[3]);
    }

    [Fact]
    public async Task CustomEq_SwitchesToTheCustomPresetBeforeWritingBands()
    {
        sbyte[] target = [-5, -4, -3, -2, -1, 0, 1, 2, 3, 5];
        var transport = new FakeTransport(
            new byte[] { 0x13, 0x02, 0x01, 0 },
            new byte[] { 0x13, 0x02, 0x01, 0 },
            new byte[] { 0x93, 0x02, 0x01, 0 },
            new byte[] { 0x13, 0x02, 0x01, 255 },
            new byte[] { 0x15, 0x02, 0x0A, 5, 5, 5, 5, 5, 5, 5, 5, 5, 5 },
            new byte[] { 0x95, 0x02, 0x01, 0 },
            new byte[] { 0x15, 0x02, 0x0A, 0, 1, 2, 3, 4, 5, 6, 7, 8, 10 });
        var service = CreateService(transport);

        Assert.Equal(target, await service.SetCustomEqAsync(target, true));

        Assert.Equal(new byte[] { 0x93, 0x00, 0x01, 255 }, transport.Frames[2]);
        Assert.Equal(
            new byte[] { 0x95, 0x00, 0x0A, 0, 1, 2, 3, 4, 5, 6, 7, 8, 10 },
            transport.Frames[5]);
    }

    [Fact]
    public async Task MicCustomEq_SwitchesToTheCustomPresetBeforeWritingBands()
    {
        sbyte[] target = [-5, -4, -3, -2, -1, 0, 1, 2, 3, 5];
        var transport = new FakeTransport(
            new byte[] { 0x16, 0x02, 0x01, 0 },
            new byte[] { 0x16, 0x02, 0x01, 0 },
            new byte[] { 0x96, 0x02, 0x01, 0 },
            new byte[] { 0x16, 0x02, 0x01, 255 },
            new byte[] { 0x17, 0x02, 0x0A, 5, 5, 5, 5, 5, 5, 5, 5, 5, 5 },
            new byte[] { 0x97, 0x02, 0x01, 0 },
            new byte[] { 0x17, 0x02, 0x0A, 0, 1, 2, 3, 4, 5, 6, 7, 8, 10 });
        var service = CreateService(transport);

        Assert.Equal(target, await service.SetMicCustomEqAsync(target, true));

        Assert.Equal(new byte[] { 0x96, 0x00, 0x01, 255 }, transport.Frames[2]);
        Assert.Equal(
            new byte[] { 0x97, 0x00, 0x0A, 0, 1, 2, 3, 4, 5, 6, 7, 8, 10 },
            transport.Frames[5]);
    }

    [Fact]
    public async Task ChromaStaticEffectWritesRgbAndReadsItBack()
    {
        var transport = new FakeTransport(
            new byte[] { 0xC3, 0x02, 0x01, 0 },
            new byte[] { 0x40, 0x02, 0x01, 3 },
            new byte[] { 0x43, 0x02, 0x03, 1, 2, 3 });
        var service = CreateService(transport);

        var state = await service.SetChromaEffectAsync(
            RazerAwChromaEffect.Static, [1, 2, 3], []);

        Assert.Equal(RazerAwChromaEffect.Static, state.Effect);
        Assert.Equal(new byte[] { 1, 2, 3 }, state.PrimaryColor);
        Assert.Equal(new byte[] { 0xC3, 0x00, 0x03, 1, 2, 3 }, transport.Frames[0]);
    }

    [Fact]
    public async Task AControlWithoutAnAnswerFailsSoThePageCanHideIt()
    {
        var service = CreateService(new FakeTransport(new byte[]?[] { null }));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.ReadVolumeAsync());

        Assert.Contains("did not answer the volume request", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnInvalidControlReplyFailsSoThePageCanHideIt()
    {
        var service = CreateService(new FakeTransport(new byte[] { 0x11, 0x02, 0x02, 1, 2 }));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.ReadVolumeAsync());

        Assert.Contains("invalid volume reply", exception.Message, StringComparison.Ordinal);
    }

    private static RazerAwBleHeadsetService CreateService(FakeTransport transport) =>
        new(transport, "test", TimeSpan.Zero, transport.Diagnostic);

    private sealed class FakeTransport(params byte[]?[] replies) : IRazerBleTransport
    {
        private readonly Queue<byte[]?> _replies = new(replies);

        public List<byte[]> Frames { get; } = [];
        public Action<string> Diagnostic { get; } = _ => { };

        public Task<byte[]?> ExchangeAsync(
            ReadOnlyMemory<byte> frame,
            TimeSpan timeout,
            CancellationToken cancellationToken)
        {
            Frames.Add(frame.ToArray());
            return Task.FromResult(_replies.Count > 0 ? _replies.Dequeue() : null);
        }

        public void Dispose()
        {
        }
    }
}
