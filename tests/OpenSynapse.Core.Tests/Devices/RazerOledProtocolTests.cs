using OpenSynapse.Windows.Protocols;
using Xunit;

namespace OpenSynapse.Core.Tests;

public sealed class RazerOledProtocolTests
{
    [Fact]
    public void BuildersUseTheProtocol25OledTupleTable()
    {
        AssertCommand(
            0x01,
            (1, 0x17, 0x83),
            RazerOledProtocol.CreateGetBrightnessRequest(0x01));
        AssertCommand(
            0x02,
            (1, 0x17, 0x03),
            RazerOledProtocol.CreateSetBrightnessRequest(0x02, 100));
        AssertCommand(
            0x03,
            (1, 0x17, 0x85),
            RazerOledProtocol.CreateGetLanguageRequest(0x03));
        AssertCommand(
            0x04,
            (1, 0x17, 0x05),
            RazerOledProtocol.CreateSetLanguageRequest(0x04, 1));
        AssertCommand(
            0x05,
            (2, 0x17, 0x0D),
            RazerOledProtocol.CreateSetHomeScreenActiveItemRequest(0x05, 7));
        AssertCommand(
            0x06,
            (2, 0x17, 0x96),
            RazerOledProtocol.CreateGetScreensaverOptionRequest(0x06));
        AssertCommand(
            0x07,
            (10, 0x17, 0x15),
            RazerOledProtocol.CreateSetHomeScreenImageStateRequest(
                0x07,
                [0, 1, 2, 3, 4, 5, 6, 7, 8, 9]));
        AssertCommand(
            0x08,
            (6, 0x17, 0x94),
            RazerOledProtocol.CreateGetHomeScreenAnimationStateRequest(0x08));
        AssertCommand(
            0x09,
            (1, 0x17, 0x06),
            RazerOledProtocol.CreateSetTimeToDimRequest(0x09, 30));
        AssertCommand(
            0x0A,
            (1, 0x17, 0x12),
            RazerOledProtocol.CreateSetLowBatteryWarningPercentageRequest(0x0A, 15));
        AssertCommand(
            0x0B,
            (2, 0x17, 0x0B),
            RazerOledProtocol.CreateSetUpdateVolumeRequest(0x0B, 64, muted: true));
        AssertCommand(
            0x0C,
            (3, 0x17, 0x98),
            RazerOledProtocol.CreateGetChromaBrightnessStatusRequest(0x0C, 2));
        AssertCommand(
            0x0D,
            (3, 0x17, 0x18),
            RazerOledProtocol.CreateSetChromaBrightnessStatusRequest(0x0D, 2, 3, 4));
    }

    [Fact]
    public void SetterPayloadsMatchTheMiddlewareWrites()
    {
        Assert.Equal(
            [0x4D],
            RazerOledProtocol.CreateSetBrightnessRequest(0, 0x4D)[9..10].ToArray());
        Assert.Equal(
            [100],
            RazerOledProtocol.CreateSetBrightnessRequest(0, 120)[9..10].ToArray());
        Assert.Equal(
            [0x02],
            RazerOledProtocol.CreateSetLanguageRequest(0, 0x02)[9..10].ToArray());
        Assert.Equal(
            [0x07, 0x07],
            RazerOledProtocol.CreateSetHomeScreenActiveItemRequest(0, 0x07)[9..11].ToArray());

        var imageStates = new byte[] { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9 };
        Assert.Equal(
            imageStates,
            RazerOledProtocol.CreateSetHomeScreenImageStateRequest(0, imageStates)[9..19].ToArray());
        Assert.Equal(
            [30],
            RazerOledProtocol.CreateSetTimeToDimRequest(0, 30)[9..10].ToArray());
        Assert.Equal(
            [15],
            RazerOledProtocol.CreateSetLowBatteryWarningPercentageRequest(0, 15)[9..10].ToArray());
        Assert.Equal(
            [64, 1],
            RazerOledProtocol.CreateSetUpdateVolumeRequest(0, 64, muted: true)[9..11].ToArray());
        Assert.Equal(
            [64, 0],
            RazerOledProtocol.CreateSetUpdateVolumeRequest(0, 64, muted: false)[9..11].ToArray());
        Assert.Equal(
            [2, 0, 0],
            RazerOledProtocol.CreateGetChromaBrightnessStatusRequest(0, 2)[9..12].ToArray());
        Assert.Equal(
            [2, 3, 4],
            RazerOledProtocol.CreateSetChromaBrightnessStatusRequest(0, 2, 3, 4)[9..12].ToArray());

        Assert.Throws<ArgumentException>(
            () => RazerOledProtocol.CreateSetHomeScreenImageStateRequest(0, new byte[9]));
    }

    [Fact]
    public void ParsersReadTheMiddlewarePayloads()
    {
        Assert.Equal(77, RazerOledProtocol.ReadBrightness(Response(1, 1, 0x83, 77)));
        Assert.Equal(2, RazerOledProtocol.ReadLanguage(Response(1, 1, 0x85, 2)));
        Assert.Equal(7, RazerOledProtocol.ReadHomeScreenActiveItem(Response(1, 2, 0x0D, 0, 7)));

        var screensaver = RazerOledProtocol.ReadScreensaverOption(Response(1, 2, 0x96, 3, 4));
        Assert.Equal(new RazerOledScreensaverOption(3, 4), screensaver);

        Assert.Equal(
            [1, 2, 3, 4, 5, 6],
            RazerOledProtocol.ReadHomeScreenAnimationState(
                Response(1, 6, 0x94, 1, 2, 3, 4, 5, 6)));
        Assert.Equal(
            [0, 1, 2, 3, 4, 5, 6, 7, 8, 9],
            RazerOledProtocol.ReadHomeScreenImageState(
                Response(1, 10, 0x15, 0, 1, 2, 3, 4, 5, 6, 7, 8, 9)));
        Assert.Equal(30, RazerOledProtocol.ReadTimeToDim(Response(1, 1, 0x06, 30)));
        Assert.Equal(
            15,
            RazerOledProtocol.ReadLowBatteryWarningPercentage(Response(1, 1, 0x12, 15)));
        Assert.Equal(64, RazerOledProtocol.ReadUpdateVolume(Response(1, 1, 0x0B, 64)));

        var chroma = RazerOledProtocol.ReadChromaBrightnessStatus(
            Response(1, 3, 0x98, 2, 3, 4));
        Assert.Equal(new RazerChromaBrightnessStatus(2, 3, 4), chroma);
    }

    [Fact]
    public void ParsersRejectTruncatedOrMalformedResponses()
    {
        Assert.Throws<InvalidDataException>(
            () => RazerOledProtocol.ReadBrightness(new byte[20]));
        Assert.Throws<InvalidDataException>(
            () => RazerOledProtocol.ReadHomeScreenAnimationState(
                Response(1, 5, 0x94, 1, 2, 3, 4, 5)));
        Assert.Throws<InvalidDataException>(
            () => RazerOledProtocol.ReadHomeScreenImageState(
                Response(1, 9, 0x15, 1, 2, 3, 4, 5, 6, 7, 8, 9)));
        Assert.Throws<InvalidDataException>(
            () => RazerOledProtocol.ReadChromaBrightnessStatus(
                Response(1, 2, 0x98, 2, 3)));
    }

    private static void AssertCommand(
        byte transaction,
        (byte Size, byte Class, byte Command) expected,
        byte[] request)
    {
        Assert.Equal(expected, (request[6], request[7], request[8]));
        Assert.Equal(transaction, request[2]);
        Assert.Equal(0x00, request[0]);
        Assert.Equal(0x00, request[90]);
        Assert.Equal(RazerFeatureReport.CalculateCrc(request), request[89]);
    }

    private static byte[] Response(byte transaction, byte dataSize, byte commandId, params byte[] arguments)
    {
        var response = new byte[RazerFeatureReport.Length];
        response[1] = 0x02;
        response[2] = transaction;
        response[6] = dataSize;
        response[7] = RazerOledProtocol.CommandClass;
        response[8] = commandId;
        arguments.CopyTo(response.AsSpan(RazerFeatureReport.ArgumentsOffset));
        response[89] = RazerFeatureReport.CalculateCrc(response);
        return response;
    }
}
