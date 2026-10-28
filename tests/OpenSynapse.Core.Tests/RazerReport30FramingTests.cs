using OpenSynapse.Windows.Protocols;
using Xunit;

namespace OpenSynapse.Core.Tests;

public sealed class RazerReport30FramingTests
{
    [Fact]
    public void KnobConfigurationRequestMatchesTheMiddlewareFrame()
    {
        var frame = RazerReport30Framing.CreateRequest(
            0x06,
            0x8D,
            new byte[50],
            transactionId: 0x05,
            header6: 0x03,
            header7: 0x24);

        byte[] expected =
        [
            0x05, 0x32, 0x00, 0x00, 0x06, 0x8D, 0x03, 0x24,
            .. new byte[50],
        ];

        Assert.Equal(expected, frame);
    }

    [Fact]
    public void KnobModeRequestMatchesTheMiddlewareFrame()
    {
        var frame = RazerReport30Framing.CreateRequest(
            0x06,
            0x8E,
            new byte[8],
            transactionId: 0x06,
            header6: 0x03,
            header7: 0x24);

        byte[] expected =
        [
            0x06, 0x08, 0x00, 0x00, 0x06, 0x8E, 0x03, 0x24,
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
        ];

        Assert.Equal(expected, frame);
    }

    [Fact]
    public void RequestLengthUsesTwentyFourBitLittleEndian()
    {
        var frame = RazerReport30Framing.CreateRequest(0x01, 0x02, new byte[0x0102]);

        Assert.Equal(0x02, frame[1]);
        Assert.Equal(0x01, frame[2]);
        Assert.Equal(0x00, frame[3]);
        Assert.Equal(0x01, frame[4]);
        Assert.Equal(0x02, frame[5]);
        Assert.Equal(0x0102 + RazerReport30Framing.RequestHeaderLength, frame.Length);
    }

    [Fact]
    public void ValidatorPrefersTheTwentyByteHeader()
    {
        var request = RazerReport30Framing.CreateRequest(
            0x06,
            0x8D,
            new byte[50],
            transactionId: 0x05,
            header6: 0x03,
            header7: 0x24);
        var response = Response(request, headerLength: 20, status: 0x02, [0x03, 0x24, 0x01]);

        Assert.True(RazerReport30Framing.TryValidateResponse(request, response, out var payload));
        Assert.Equal(new byte[] { 0x03, 0x24, 0x01 }, payload.ToArray());
    }

    [Fact]
    public void ValidatorFallsBackToTheEightByteHeader()
    {
        var request = RazerReport30Framing.CreateRequest(
            0x06,
            0x8E,
            new byte[8],
            transactionId: 0x06,
            header6: 0x03,
            header7: 0x24);
        var response = Response(request, headerLength: 8, status: 0x02, [0x03, 0x24, 0x07]);

        Assert.True(RazerReport30Framing.TryValidateResponse(request, response, out var payload));
        Assert.Equal(new byte[] { 0x03, 0x24, 0x07 }, payload.ToArray());
    }

    [Fact]
    public void ValidatorRejectsMalformedOrOutOfOrderReplies()
    {
        var request = RazerReport30Framing.CreateRequest(
            0x06,
            0x8D,
            new byte[50],
            transactionId: 0x05,
            header6: 0x03,
            header7: 0x24);

        Assert.False(RazerReport30Framing.TryValidateResponse(request, new byte[7], out _));

        var wrongTransaction = Response(request, 20, 0x02, [0x03]);
        wrongTransaction[0] ^= 0x01;
        Assert.False(RazerReport30Framing.TryValidateResponse(request, wrongTransaction, out _));

        var wrongClass = Response(request, 20, 0x02, [0x03]);
        wrongClass[4] ^= 0x01;
        Assert.False(RazerReport30Framing.TryValidateResponse(request, wrongClass, out _));

        var wrongId = Response(request, 20, 0x02, [0x03]);
        wrongId[5] ^= 0x01;
        Assert.False(RazerReport30Framing.TryValidateResponse(request, wrongId, out _));

        var wrongStatus = Response(request, 20, 0x01, [0x03]);
        Assert.False(RazerReport30Framing.TryValidateResponse(request, wrongStatus, out _));

        var wrongLength = Response(request, 20, 0x02, [0x03]);
        wrongLength[1] = 0x02;
        Assert.False(RazerReport30Framing.TryValidateResponse(request, wrongLength, out _));
    }

    private static byte[] Response(
        byte[] request,
        int headerLength,
        byte status,
        byte[] payload)
    {
        var response = new byte[headerLength + payload.Length];
        response[0] = request[0];
        response[1] = (byte)payload.Length;
        response[2] = (byte)(payload.Length >> 8);
        response[3] = (byte)(payload.Length >> 16);
        response[4] = request[4];
        response[5] = request[5];
        response[7] = status;
        payload.CopyTo(response, headerLength);
        return response;
    }
}
