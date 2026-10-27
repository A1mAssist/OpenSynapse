using OpenSynapse.Windows.Protocols;
using Xunit;

namespace OpenSynapse.Core.Tests;

public sealed class RazerOnboardProfileProtocolTests
{
    [Fact]
    public void BuildersUseTheMiddlewareClass5Tuples()
    {
        var getMax = RazerOnboardProfileProtocol.CreateGetMaxProfilesSupportedRequest(0x21);
        var getCount = RazerOnboardProfileProtocol.CreateGetNumberOfProfilesRequest(0x22);
        var create = RazerOnboardProfileProtocol.CreateNewProfileRequest(0x23, 0x07);
        var delete = RazerOnboardProfileProtocol.CreateDeleteProfileRequest(0x24, 0x07);
        var getActive = RazerOnboardProfileProtocol.CreateGetActiveProfileIdRequest(0x25);
        var setActive = RazerOnboardProfileProtocol.CreateSetActiveProfileIdRequest(0x26, 0x07);
        var reset = RazerOnboardProfileProtocol.CreateSetProfileResetRequest(0x27, 0x07);
        var getColor = RazerOnboardProfileProtocol.CreateGetProfileColorRequest(0x28, 0x07);
        var setColor = RazerOnboardProfileProtocol.CreateSetProfileColorRequest(0x29, 0x07, 0x11, 0x22, 0x33);

        Assert.Equal((0x01, 0x05, 0x8A), Command(getMax));
        Assert.Equal((0x01, 0x05, 0x80), Command(getCount));
        Assert.Equal((0x01, 0x05, 0x02), Command(create));
        Assert.Equal((0x01, 0x05, 0x03), Command(delete));
        Assert.Equal((0x01, 0x05, 0x84), Command(getActive));
        Assert.Equal((0x01, 0x05, 0x04), Command(setActive));
        Assert.Equal((0x01, 0x05, 0x0B), Command(reset));
        Assert.Equal((0x04, 0x05, 0x8C), Command(getColor));
        Assert.Equal((0x04, 0x05, 0x0C), Command(setColor));

        Assert.Equal(0x21, getMax[2]);
        Assert.Equal(0x22, getCount[2]);
        Assert.Equal(0x23, create[2]);
        Assert.Equal(0x24, delete[2]);
        Assert.Equal(0x25, getActive[2]);
        Assert.Equal(0x26, setActive[2]);
        Assert.Equal(0x27, reset[2]);
        Assert.Equal(0x28, getColor[2]);
        Assert.Equal(0x29, setColor[2]);

        Assert.Equal(new byte[] { 0x07 }, create[9..10].ToArray());
        Assert.Equal(new byte[] { 0x07 }, delete[9..10].ToArray());
        Assert.Equal(new byte[] { 0x07 }, setActive[9..10].ToArray());
        Assert.Equal(new byte[] { 0x07 }, reset[9..10].ToArray());
        Assert.Equal(new byte[] { 0x07, 0x00, 0x00, 0x00 }, getColor[9..13].ToArray());
        Assert.Equal(new byte[] { 0x07, 0x11, 0x22, 0x33 }, setColor[9..13].ToArray());

        foreach (var report in new[] { getMax, getCount, getActive })
        {
            Assert.All(report[9..89].ToArray(), value => Assert.Equal(0, value));
        }

        foreach (var report in new[] { getMax, getCount, create, delete, getActive, setActive, reset, getColor, setColor })
        {
            Assert.Equal(RazerFeatureReport.Length, report.Length);
            Assert.Equal(0x00, report[0]);
            Assert.Equal(0x00, report[90]);
            Assert.Equal(RazerFeatureReport.CalculateCrc(report), report[89]);
        }
    }

    [Fact]
    public void ParsersReadTheDocumentedFields()
    {
        var getCountRequest = RazerOnboardProfileProtocol.CreateGetNumberOfProfilesRequest(0x21);
        var getMaxRequest = RazerOnboardProfileProtocol.CreateGetMaxProfilesSupportedRequest(0x22);
        var getActiveRequest = RazerOnboardProfileProtocol.CreateGetActiveProfileIdRequest(0x23);
        var setActiveRequest = RazerOnboardProfileProtocol.CreateSetActiveProfileIdRequest(0x24, 0x03);
        var resetRequest = RazerOnboardProfileProtocol.CreateSetProfileResetRequest(0x25, 0x01);
        var getColorRequest = RazerOnboardProfileProtocol.CreateGetProfileColorRequest(0x26, 0x07);
        var setColorRequest = RazerOnboardProfileProtocol.CreateSetProfileColorRequest(0x27, 0x07, 0x10, 0x20, 0x30);

        Assert.Equal(
            0x05,
            RazerOnboardProfileProtocol.ParseNumberOfProfiles(
                Response(0x21, 0x01, 0x80, [0x05]),
                getCountRequest));
        Assert.Equal(
            0x04,
            RazerOnboardProfileProtocol.ParseMaxProfilesSupported(
                Response(0x22, 0x01, 0x8A, [0x04]),
                getMaxRequest));
        Assert.Equal(
            0x02,
            RazerOnboardProfileProtocol.ParseProfileId(
                Response(0x23, 0x01, 0x84, [0x02]),
                getActiveRequest));
        Assert.Equal(
            0x03,
            RazerOnboardProfileProtocol.ParseProfileId(
                Response(0x24, 0x01, 0x04, [0x03]),
                setActiveRequest));
        Assert.Equal(
            0x01,
            RazerOnboardProfileProtocol.ParseProfileId(
                Response(0x25, 0x01, 0x0B, [0x01]),
                resetRequest));
        Assert.Equal(
            new RazerProfileColor(0x07, 0x10, 0x20, 0x30),
            RazerOnboardProfileProtocol.ParseProfileColor(
                Response(0x26, 0x04, 0x8C, [0x07, 0x10, 0x20, 0x30]),
                getColorRequest));
        Assert.Equal(
            new RazerProfileColor(0x07, 0x10, 0x20, 0x30),
            RazerOnboardProfileProtocol.ParseProfileColor(
                Response(0x27, 0x04, 0x0C, [0x07, 0x10, 0x20, 0x30]),
                setColorRequest));
    }

    [Fact]
    public void InvalidResponsesAreRejected()
    {
        var countRequest = RazerOnboardProfileProtocol.CreateGetNumberOfProfilesRequest(0x21);
        var colorRequest = RazerOnboardProfileProtocol.CreateGetProfileColorRequest(0x22, 0x07);

        Assert.Throws<InvalidOperationException>(
            () => RazerOnboardProfileProtocol.ParseNumberOfProfiles(new byte[20], countRequest));
        Assert.Throws<InvalidOperationException>(
            () => RazerOnboardProfileProtocol.ParseNumberOfProfiles(
                Response(0x21, 0x01, 0x84, [0x05]),
                countRequest));
        Assert.Throws<InvalidOperationException>(
            () => RazerOnboardProfileProtocol.ParseNumberOfProfiles(
                Response(0x22, 0x01, 0x80, [0x05]),
                countRequest));

        var failed = Response(0x21, 0x01, 0x80, [0x05], status: 0x03);
        Assert.Throws<InvalidOperationException>(
            () => RazerOnboardProfileProtocol.ParseNumberOfProfiles(failed, countRequest));

        var corrupted = Response(0x21, 0x01, 0x80, [0x05]);
        corrupted[89] ^= 0xFF;
        Assert.Throws<InvalidOperationException>(
            () => RazerOnboardProfileProtocol.ParseNumberOfProfiles(corrupted, countRequest));

        Assert.Throws<InvalidOperationException>(
            () => RazerOnboardProfileProtocol.ParseProfileColor(
                Response(0x22, 0x03, 0x8C, [0x07, 0x10, 0x20, 0x30]),
                colorRequest));
        Assert.Throws<InvalidOperationException>(
            () => RazerOnboardProfileProtocol.ParseProfileColor(new byte[20], colorRequest));
    }

    private static (byte Size, byte Class, byte Command) Command(byte[] request) =>
        (request[6], request[7], request[8]);

    private static byte[] Response(
        byte transaction,
        byte dataSize,
        byte commandId,
        byte[] arguments,
        byte status = 0x02)
    {
        var response = new byte[RazerFeatureReport.Length];
        response[1] = status;
        response[2] = transaction;
        response[6] = dataSize;
        response[7] = 0x05;
        response[8] = commandId;
        arguments.CopyTo(response.AsSpan(RazerFeatureReport.ArgumentsOffset));
        response[89] = RazerFeatureReport.CalculateCrc(response);
        return response;
    }
}
