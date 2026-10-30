using OpenSynapse.Windows.Protocols;
using Xunit;

namespace OpenSynapse.Core.Tests;

public sealed class RazerAwBleProtocolTests
{
    [Fact]
    public void BatteryRequestUsesCommandTwentyOneWithoutResponse()
    {
        Assert.Equal(new byte[] { 0x21, 0x00, 0x00 }, RazerAwBleProtocol.CreateBatteryRequest());
    }

    [Fact]
    public void ServiceAndCharacteristicUuidsMatchTheAudioAwKittyBundle()
    {
        Assert.Equal("416d656c-2d52-617a-6572-424c4501f40a", RazerAwBleProtocol.ServiceUuid.ToString());
        Assert.Equal("416d0000-2d52-617a-6572-424c4501f40a", RazerAwBleProtocol.WriteCharacteristicUuid.ToString());
        Assert.Equal("416d0001-2d52-617a-6572-424c4501f40a", RazerAwBleProtocol.NotifyCharacteristicUuid.ToString());
    }

    [Fact]
    public void HeadsetReplyCarriesOneLevel()
    {
        var parsed = RazerAwBleProtocol.TryParseBattery([0x21, 0x02, 0x01, 87], out var state);

        Assert.True(parsed);
        Assert.Equal((byte)87, state.HeadsetPercent);
        Assert.Null(state.RightEarbudPercent);
        Assert.Null(state.LeftEarbudPercent);
    }

    [Fact]
    public void EarbudReplyCarriesRightThenLeft()
    {
        var parsed = RazerAwBleProtocol.TryParseBattery([0x21, 0x02, 0x02, 80, 79], out var state);

        Assert.True(parsed);
        Assert.Null(state.HeadsetPercent);
        Assert.Equal((byte)80, state.RightEarbudPercent);
        Assert.Equal((byte)79, state.LeftEarbudPercent);
    }

    [Theory]
    [InlineData(new byte[] { 0x21, 0x00, 0x01, 87 })]
    [InlineData(new byte[] { 0x25, 0x02, 0x01, 1 })]
    [InlineData(new byte[] { 0x21, 0x02, 0x01 })]
    [InlineData(new byte[] { 0x21, 0x02, 0x00 })]
    [InlineData(new byte[] { 0x21, 0x02, 0x03, 1, 2, 3 })]
    public void OtherOrTruncatedFramesAreRejected(byte[] frame)
    {
        Assert.False(RazerAwBleProtocol.TryParseBattery(frame, out _));
    }

    [Fact]
    public void ControlReadRequestsMatchTheBundleCommands()
    {
        Assert.Equal(new byte[] { 0x11, 0x00, 0x00 },
            RazerAwBleProtocol.CreateReadRequest(RazerAwBleProtocol.VolumeGetCommand));
        Assert.Equal(new byte[] { 0x19, 0x00, 0x00 },
            RazerAwBleProtocol.CreateReadRequest(RazerAwBleProtocol.SidetoneGetCommand));
        Assert.Equal(new byte[] { 0x14, 0x00, 0x00 },
            RazerAwBleProtocol.CreateReadRequest(RazerAwBleProtocol.GameModeGetCommand));
        Assert.Equal(new byte[] { 0x13, 0x00, 0x00 },
            RazerAwBleProtocol.CreateReadRequest(RazerAwBleProtocol.EqPresetGetCommand));
        Assert.Equal(new byte[] { 0x2C, 0x00, 0x00 },
            RazerAwBleProtocol.CreateReadRequest(RazerAwBleProtocol.AutoPowerOffGetCommand));
        Assert.Equal(new byte[] { 0x41, 0x00, 0x00 },
            RazerAwBleProtocol.CreateReadRequest(RazerAwBleProtocol.ChromaBrightnessGetCommand));
        Assert.Equal(new byte[] { 0x1B, 0x00, 0x00 },
            RazerAwBleProtocol.CreateReadRequest(RazerAwBleProtocol.BassGetCommand));
        Assert.Equal(new byte[] { 0x54, 0x00, 0x00 },
            RazerAwBleProtocol.CreateReadRequest(RazerAwBleProtocol.HapticsGetCommand));
        Assert.Equal(new byte[] { 0x16, 0x00, 0x00 },
            RazerAwBleProtocol.CreateReadRequest(RazerAwBleProtocol.MicEqPresetGetCommand));
        Assert.Equal(new byte[] { 0x15, 0x00, 0x00 },
            RazerAwBleProtocol.CreateReadRequest(RazerAwBleProtocol.CustomEqBandsGetCommand));
        Assert.Equal(new byte[] { 0x02, 0x00, 0x00 },
            RazerAwBleProtocol.CreateReadRequest(RazerAwBleProtocol.FirmwareGetCommand));
        Assert.Equal(new byte[] { 0x00, 0x00, 0x00 },
            RazerAwBleProtocol.CreateReadRequest(RazerAwBleProtocol.SerialGetCommand));
        Assert.Equal(new byte[] { 0x25, 0x00, 0x00 },
            RazerAwBleProtocol.CreateReadRequest(RazerAwBleProtocol.ConnectionGetCommand));
        Assert.Equal(new byte[] { 0x2A, 0x00, 0x00 },
            RazerAwBleProtocol.CreateReadRequest(RazerAwBleProtocol.ChargingGetCommand));
    }

    [Fact]
    public void ControlWriteRequestsCarryOnePayloadByte()
    {
        Assert.Equal(new byte[] { 0x91, 0x00, 0x01, 55 },
            RazerAwBleProtocol.CreateWriteRequest(RazerAwBleProtocol.VolumeSetCommand, 55));
        Assert.Equal(new byte[] { 0x99, 0x00, 0x01, 7 },
            RazerAwBleProtocol.CreateWriteRequest(RazerAwBleProtocol.SidetoneSetCommand, 7));
        Assert.Equal(new byte[] { 0x94, 0x00, 0x01, 1 },
            RazerAwBleProtocol.CreateWriteRequest(RazerAwBleProtocol.GameModeSetCommand, 1));
        Assert.Equal(new byte[] { 0x93, 0x00, 0x01, 10 },
            RazerAwBleProtocol.CreateWriteRequest(RazerAwBleProtocol.EqPresetSetCommand, 10));
        Assert.Equal(new byte[] { 0xAC, 0x00, 0x01, 30 },
            RazerAwBleProtocol.CreateWriteRequest(RazerAwBleProtocol.AutoPowerOffSetCommand, 30));
        Assert.Equal(new byte[] { 0xC1, 0x00, 0x01, 127 },
            RazerAwBleProtocol.CreateWriteRequest(RazerAwBleProtocol.ChromaBrightnessSetCommand, 127));
        Assert.Equal(new byte[] { 0x9B, 0x00, 0x01, 55 },
            RazerAwBleProtocol.CreateWriteRequest(RazerAwBleProtocol.BassSetCommand, 55));
        Assert.Equal(new byte[] { 0xD4, 0x00, 0x01, 6 },
            RazerAwBleProtocol.CreateWriteRequest(RazerAwBleProtocol.HapticsSetCommand, 6));
        Assert.Equal(new byte[] { 0x96, 0x00, 0x01, 2 },
            RazerAwBleProtocol.CreateWriteRequest(RazerAwBleProtocol.MicEqPresetSetCommand, 2));
    }

    [Fact]
    public void CustomEqRequestCarriesTenOffsetLevels()
    {
        sbyte[] levels = [-5, -4, -3, -2, -1, 0, 1, 2, 3, 4];
        Assert.Equal(
            new byte[] { 0x95, 0x00, 0x0A, 0, 1, 2, 3, 4, 5, 6, 7, 8, 9 },
            RazerAwBleProtocol.CreateEqBandsRequest(levels));
    }

    [Theory]
    [InlineData(0x11, 55)]
    [InlineData(0x19, 15)]
    [InlineData(0x14, 1)]
    [InlineData(0x13, 255)]
    [InlineData(0x2C, 30)]
    [InlineData(0x41, 127)]
    [InlineData(0x1B, 55)]
    [InlineData(0x54, 6)]
    [InlineData(0x16, 2)]
    [InlineData(0x2A, 1)]
    public void OneByteRepliesAreParsed(byte command, byte value)
    {
        Assert.True(RazerAwBleProtocol.TryParseByteReply(
            new byte[] { command, 0x02, 0x01, value }, command, out var parsed));
        Assert.Equal(value, parsed);
    }

    [Theory]
    [InlineData(new byte[] { 0x91, 0x00, 0x01, 0 })]
    [InlineData(new byte[] { 0x92, 0x02, 0x01, 0 })]
    [InlineData(new byte[] { 0x91, 0x02, 0x00 })]
    [InlineData(new byte[] { 0x91, 0x02, 0x02, 0, 0 })]
    [InlineData(new byte[] { 0x91, 0x02, 0x01 })]
    public void InvalidOneByteRepliesAreRejected(byte[] frame)
    {
        Assert.False(RazerAwBleProtocol.TryParseByteReply(frame, 0x91, out _));
        Assert.False(RazerAwBleProtocol.TryParseWriteAck(frame, 0x91, out _));
    }

    [Fact]
    public void WriteAcknowledgementCarriesTheStatusByte()
    {
        Assert.True(RazerAwBleProtocol.TryParseWriteAck(
            [0x91, 0x02, 0x01, 0], RazerAwBleProtocol.VolumeSetCommand, out var success));
        Assert.Equal(0, success);

        Assert.True(RazerAwBleProtocol.TryParseWriteAck(
            [0x91, 0x02, 0x01, 1], RazerAwBleProtocol.VolumeSetCommand, out var unsupported));
        Assert.Equal(1, unsupported);
    }

    [Fact]
    public void ReplyFramesCanCarryAValueInsteadOfAStatus()
    {
        Assert.True(RazerAwBleProtocol.IsReplyForCommand(
            [0x91, 0x02, 0x01, 55], RazerAwBleProtocol.VolumeSetCommand));
        Assert.True(RazerAwBleProtocol.IsReplyForCommand(
            [0x91, 0x02, 0x00], RazerAwBleProtocol.VolumeSetCommand));
        Assert.False(RazerAwBleProtocol.IsReplyForCommand(
            [0x91, 0x00, 0x01, 55], RazerAwBleProtocol.VolumeSetCommand));
        Assert.False(RazerAwBleProtocol.IsReplyForCommand(
            [0x92, 0x02, 0x01, 55], RazerAwBleProtocol.VolumeSetCommand));
        Assert.False(RazerAwBleProtocol.IsReplyForCommand(
            [0x91, 0x02, 0x02, 55], RazerAwBleProtocol.VolumeSetCommand));
    }

    [Fact]
    public void SidetoneAndChromaPercentConversionsMatchTheBundleScaling()
    {
        Assert.Equal((byte)0, RazerAwBleProtocol.SidetonePercentToRaw(0));
        Assert.Equal((byte)7, RazerAwBleProtocol.SidetonePercentToRaw(50));
        Assert.Equal((byte)15, RazerAwBleProtocol.SidetonePercentToRaw(100));
        Assert.Equal((byte)47, RazerAwBleProtocol.SidetoneRawToPercent(7));
        Assert.Equal((byte)100, RazerAwBleProtocol.SidetoneRawToPercent(15));

        Assert.Equal((byte)0, RazerAwBleProtocol.ChromaPercentToRaw(0));
        Assert.Equal((byte)127, RazerAwBleProtocol.ChromaPercentToRaw(50));
        Assert.Equal((byte)255, RazerAwBleProtocol.ChromaPercentToRaw(100));
        Assert.Equal((byte)50, RazerAwBleProtocol.ChromaRawToPercent(127));
        Assert.Equal((byte)100, RazerAwBleProtocol.ChromaRawToPercent(255));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(13)]
    [InlineData(254)]
    [InlineData(255)]
    public void KnownEqPresetValuesAreAccepted(byte preset)
    {
        Assert.True(RazerAwBleProtocol.IsKnownEqPreset(preset));
    }

    [Theory]
    [InlineData(14)]
    [InlineData(100)]
    [InlineData(253)]
    public void UnknownEqPresetValuesAreRejected(byte preset)
    {
        Assert.False(RazerAwBleProtocol.IsKnownEqPreset(preset));
    }

    [Fact]
    public void FirmwareReplyCarriesFourBytes()
    {
        Assert.True(RazerAwBleProtocol.TryParseFirmware(
            [0x02, 0x02, 0x04, 1, 2, 3, 4], out var version));
        Assert.Equal(new RazerAwFirmwareVersion(1, 2, 3, 4), version);
        Assert.Equal("1.2.3.4", version.ToString());
        Assert.False(RazerAwBleProtocol.TryParseFirmware(
            [0x02, 0x02, 0x03, 1, 2, 3], out _));
    }

    [Fact]
    public void SerialReplyKeepsOnlyPrintableAscii()
    {
        Assert.True(RazerAwBleProtocol.TryParseSerial(
            [0x00, 0x02, 0x06, 65, 66, 0, 67, 32, 68], out var serial));
        Assert.Equal("ABC D", serial);
        Assert.False(RazerAwBleProtocol.TryParseSerial(
            [0x00, 0x02, 0x02, 0, 1], out _));
    }

    [Fact]
    public void ConnectionReplyHandlesHeadsetAndEarbuds()
    {
        Assert.True(RazerAwBleProtocol.TryParseConnection(
            [0x25, 0x02, 0x01, 2], out var headset));
        Assert.Equal(RazerAwHeadsetConnectionStatus.DongleConnected, headset.Headset);
        Assert.Null(headset.Right);
        Assert.Null(headset.Left);

        Assert.True(RazerAwBleProtocol.TryParseConnection(
            [0x25, 0x02, 0x02, 0, 1], out var earbuds));
        Assert.Null(earbuds.Headset);
        Assert.Equal(RazerAwEarbudConnectionStatus.ConnectedAsMaster, earbuds.Right);
        Assert.Equal(RazerAwEarbudConnectionStatus.ConnectedAsSlave, earbuds.Left);

        Assert.False(RazerAwBleProtocol.TryParseConnection(
            [0x25, 0x02, 0x01, 3], out _));
        Assert.False(RazerAwBleProtocol.TryParseConnection(
            [0x25, 0x02, 0x03, 0, 1, 2], out _));
    }

    [Fact]
    public void ChargingReplyUsesTheBundleEnum()
    {
        Assert.True(RazerAwBleProtocol.TryParseCharging(
            [0x2A, 0x02, 0x01, 1], out var charging));
        Assert.Equal(RazerAwChargingStatus.Charging, charging);
        Assert.True(RazerAwBleProtocol.TryParseCharging(
            [0x2A, 0x02, 0x01, 2], out var failed));
        Assert.Equal(RazerAwChargingStatus.ChargingFailed, failed);
        Assert.False(RazerAwBleProtocol.TryParseCharging(
            [0x2A, 0x02, 0x01, 3], out _));
    }

    [Fact]
    public void CustomEqReplySubtractsTheBundleOffset()
    {
        Assert.True(RazerAwBleProtocol.TryParseEqBands(
            [0x15, 0x02, 0x0A, 0, 1, 2, 3, 4, 5, 6, 7, 8, 10], out var levels));
        Assert.Equal(
            new sbyte[] { -5, -4, -3, -2, -1, 0, 1, 2, 3, 5 },
            levels);

        Assert.False(RazerAwBleProtocol.TryParseEqBands(
            [0x15, 0x02, 0x09, 0, 1, 2, 3, 4, 5, 6, 7, 8], out _));
        Assert.False(RazerAwBleProtocol.TryParseEqBands(
            [0x15, 0x02, 0x0A, 0, 1, 2, 3, 4, 5, 6, 7, 8, 11], out _));
    }

    [Fact]
    public void CustomEqRequestRejectsOutOfRangeLevels()
    {
        sbyte[] tooHigh = [0, 0, 0, 0, 0, 0, 0, 0, 0, 6];
        Assert.Throws<ArgumentOutOfRangeException>(
            () => RazerAwBleProtocol.CreateEqBandsRequest(tooHigh));
        Assert.Throws<ArgumentException>(
            () => RazerAwBleProtocol.CreateEqBandsRequest([0, 0, 0]));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(255)]
    public void KnownMicEqPresetValuesAreAccepted(byte preset)
    {
        Assert.True(RazerAwBleProtocol.IsKnownMicEqPreset(preset));
    }

    [Theory]
    [InlineData(4)]
    [InlineData(100)]
    [InlineData(254)]
    public void UnknownMicEqPresetValuesAreRejected(byte preset)
    {
        Assert.False(RazerAwBleProtocol.IsKnownMicEqPreset(preset));
    }
}
