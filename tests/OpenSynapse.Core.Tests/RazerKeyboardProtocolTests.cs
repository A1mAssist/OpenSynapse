using OpenSynapse.Windows.Protocols;
using Xunit;

namespace OpenSynapse.Core.Tests;

public sealed class RazerKeyboardProtocolTests
{
    [Fact]
    public void GameModeAndPollingRequestsMatchTheMiddlewareTable()
    {
        var getMode = RazerKeyboardProtocol.GetGameModeState(0x03);
        var setMode = RazerKeyboardProtocol.SetGameModeState(0x04, 1, 0, 1);
        var getPolling = RazerKeyboardProtocol.GetPollingRate(0x05);
        var setPolling = RazerKeyboardProtocol.SetPollingRate(0x06, 0x01);

        Assert.Equal((0x04, 0x00, 0x88), Command(getMode));
        Assert.Equal((0x04, 0x00, 0x08), Command(setMode));
        Assert.Equal((0x02, 0x00, 0x8E), Command(getPolling));
        Assert.Equal((0x02, 0x00, 0x0E), Command(setPolling));
        Assert.Equal([1, 0, 1], setMode[9..12]);
        Assert.Equal(0x01, setPolling[9]);
        Assert.Equal(RazerFeatureReport.CalculateCrc(setPolling), setPolling[89]);
    }

    [Fact]
    public void PollingCodesRoundTrip()
    {
        Assert.Equal((byte)0x01, RazerKeyboardProtocol.PollingCode(8_000));
        Assert.Equal((byte)0x08, RazerKeyboardProtocol.PollingCode(1_000));
        Assert.Equal((byte)0x40, RazerKeyboardProtocol.PollingCode(125));
        Assert.Null(RazerKeyboardProtocol.PollingCode(3_000));
        Assert.Equal(4_000, RazerKeyboardProtocol.ReadPollingRate(Response(1, 2, 0x00, 0x02)));
    }

    [Fact]
    public void ReadersUseTheClassSevenLayoutWithoutAProfileByte()
    {
        Assert.Equal((byte)100, RazerKeyboardProtocol.ReadBatteryPercent(Response(1, 1, 0xFF)));
        Assert.Equal((byte)50, RazerKeyboardProtocol.ReadBatteryPercent(Response(1, 1, 0x80)));
        Assert.True(RazerKeyboardProtocol.ReadChargingStatus(Response(1, 1, 1)));
        Assert.Equal(new Version(2, 7), RazerKeyboardProtocol.ReadFirmware(Response(1, 2, 2, 7)));
        Assert.Equal("PM1234", RazerKeyboardProtocol.ReadSerialNumber(
            Response(1, 22, [.. "PM1234"u8, .. new byte[16]])));
    }

    [Fact]
    public void GameModeStateKeepsTheCoverAndLiftedFlags()
    {
        var state = RazerKeyboardProtocol.ReadGameModeState(Response(1, 4, 1, 2, 3));

        Assert.Equal(1, state.GameMode);
        Assert.Equal(2, state.KeyCover);
        Assert.Equal(3, state.Lifted);
    }

    [Fact]
    public void SerialUsesTheClassZeroCommandAndTheAlternative()
    {
        Assert.Equal((0x16, 0x00, 0x82), Command(RazerKeyboardProtocol.GetSerialNumber(0)));
        Assert.Equal((0x00, 0x01, 0x83), Command(RazerKeyboardProtocol.GetSerialNumberAlt(0)));
        Assert.Equal((0x02, 0x07, 0x80), Command(RazerKeyboardProtocol.GetBatteryLevel(0)));
        Assert.Equal((0x02, 0x07, 0x84), Command(RazerKeyboardProtocol.GetChargingStatus(0)));
        Assert.Equal((0x04, 0x00, 0x87), Command(RazerKeyboardProtocol.GetExtendedFirmware(0)));
    }

    private static (byte Size, byte Class, byte Command) Command(byte[] request) =>
        (request[6], request[7], request[8]);

    private static byte[] Response(byte transaction, byte dataSize, params byte[] arguments)
    {
        var response = new byte[RazerFeatureReport.Length];
        response[1] = 0x02;
        response[2] = transaction;
        response[6] = dataSize;
        arguments.CopyTo(response.AsSpan(RazerFeatureReport.ArgumentsOffset));
        response[89] = RazerFeatureReport.CalculateCrc(response);
        return response;
    }
}
