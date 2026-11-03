using OpenSynapse.Windows.Protocols;
using Xunit;

namespace OpenSynapse.Core.Tests;

public sealed class RazerHidP2ProtocolTests
{
    [Fact]
    public void P2ReadPacketsUseTheSixByteHeaderAndCheckThePayloadLength()
    {
        var active = RazerHidP2Protocol.CreateReadActiveProfilePacket();
        Assert.Equal(11, active.Length);
        Assert.Equal(0x05, active[0]);
        Assert.Equal(5, active[1]);
        Assert.Equal(1, active[2]);
        Assert.Equal(new byte[3], active[3..6]);
        Assert.Equal(new byte[5], active[6..]);

        var data = RazerHidP2Protocol.CreateReadProfileDataPacket(0x03);
        Assert.Equal(78, data.Length);
        Assert.Equal(0x06, data[0]);
        Assert.Equal(72, data[1]);
        Assert.Equal(1, data[2]);
        Assert.Equal(0x03, data[6]);
        Assert.Equal(new byte[71], data[7..]);

        var name = RazerHidP2Protocol.CreateReadProfileNamePacket(0x02);
        Assert.Equal(47, name.Length);
        Assert.Equal(0x22, name[0]);
        Assert.Equal(41, name[1]);
        Assert.Equal(1, name[2]);
        Assert.Equal(0x02, name[6]);
        Assert.Equal(new byte[40], name[7..]);

        var callback = RazerHidP2Protocol.CreateReadCallbackStatePacket();
        Assert.Equal(7, callback.Length);
        Assert.Equal(0x29, callback[0]);
        Assert.Equal(1, callback[1]);
        Assert.Equal(1, callback[2]);
        Assert.Equal(0, callback[6]);
    }

    [Fact]
    public void P2ProfileNameKeepsTheFortyByteNameFieldAfterTheIndex()
    {
        var response = new byte[47];
        response[0] = 0x22;
        response[1] = 41;
        response[2] = 1;
        response[6] = 0x02;
        for (var index = 0; index < 40; index++)
        {
            response[7 + index] = (byte)('A' + index % 26);
        }

        Assert.Equal(response[7..47], RazerHidP2Protocol.ParseProfileName(response));
    }

    [Fact]
    public void P2ProfileDataAndActiveProfileReturnTheDeclaredPayload()
    {
        var profile = new byte[78];
        profile[0] = 0x06;
        profile[1] = 72;
        profile[2] = 1;
        profile[6] = 0x02;
        for (var index = 1; index < 72; index++)
        {
            profile[6 + index] = (byte)index;
        }

        Assert.Equal(profile[6..], RazerHidP2Protocol.ParseProfileData(profile));

        var active = new byte[11];
        active[0] = 0x05;
        active[1] = 5;
        active[2] = 1;
        active[6] = 0x02;
        Assert.Equal(new byte[] { 0x02, 0, 0, 0, 0 }, RazerHidP2Protocol.ParseActiveProfile(active));
    }

    [Fact]
    public void P2ResponseValidationRejectsAMismatchedPayloadLength()
    {
        var response = new byte[78];
        response[0] = 0x06;
        response[1] = 71;
        response[2] = 1;

        Assert.Throws<InvalidDataException>(() => RazerHidP2Protocol.ParseProfileData(response));
    }

    [Fact]
    public void P2TransportFrameUsesThe91ByteEnvelope()
    {
        var packet = RazerHidP2Protocol.CreateReadProfileDataPacket(0x03);
        var frame = RazerHidP2Protocol.CreateTransportFrame(packet);

        Assert.Equal(91, frame.Length);
        Assert.Equal(0, frame[0]);
        Assert.Equal(1, frame[1]);
        Assert.Equal(0, frame[2]);
        Assert.Equal(1, frame[3]);
        Assert.Equal(0x06, frame[4]);
        Assert.Equal(72, frame[5]);
        Assert.Equal(0, frame[6]);
        Assert.Equal(0, frame[7]);
        Assert.Equal(0, frame[8]);
        Assert.Equal(0x03, frame[9]);
        Assert.Equal(new byte[71], frame[10..81]);
        Assert.Equal(RazerFeatureReport.CalculateCrc(frame), frame[89]);
        Assert.Equal(1, frame[90]);
    }

    [Fact]
    public void P2TransportFrameRoundTripsTheProfileNameResponse()
    {
        var packet = RazerHidP2Protocol.CreateReadProfileNamePacket(0x02);
        var frame = RazerHidP2Protocol.CreateTransportFrame(packet);
        var name = Enumerable.Range(0, 40).Select(index => (byte)('A' + index % 26)).ToArray();
        frame[1] = 0x02;
        frame[9] = 0x02;
        name.CopyTo(frame.AsSpan(10));
        frame[89] = RazerFeatureReport.CalculateCrc(frame);

        var response = RazerHidP2Protocol.ParseTransportFrame(frame, packet);

        Assert.Equal(47, response.Length);
        Assert.Equal(0x22, response[0]);
        Assert.Equal(41, response[1]);
        Assert.Equal(1, response[2]);
        Assert.Equal(0x02, response[6]);
        Assert.Equal(name, RazerHidP2Protocol.ParseProfileName(response));
    }

    [Fact]
    public void P2ReadTransportRejectsASetPacketBeforeOpeningTheDriver()
    {
        var write = RazerHidP2Protocol.CreateReadProfileDataPacket(0x01);
        write[2] = 0;

        Assert.Throws<ArgumentException>(() =>
            RazerHidP2Transport.ReadAsync(@"\\.\RzUdd", write, CancellationToken.None));
    }
}
