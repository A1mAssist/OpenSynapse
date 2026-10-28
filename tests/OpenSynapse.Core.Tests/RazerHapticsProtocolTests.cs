using OpenSynapse.Windows.Protocols;
using Xunit;

namespace OpenSynapse.Core.Tests;

public sealed class RazerHapticsProtocolTests
{
    [Fact]
    public void SetHapticsControl_BuildsTheExactClass04Frame()
    {
        var request = RazerHapticsProtocol.CreateSetHapticsControlRequest(
            0x2A,
            enabled: true,
            strength: 0x1234);

        var expected = new byte[RazerFeatureReport.Length];
        expected[2] = 0x2A;
        expected[6] = 0x03;
        expected[7] = 0x04;
        expected[8] = 0x02;
        expected[9] = 0x01;
        expected[10] = 0x34;
        expected[11] = 0x12;
        expected[89] = 0x22;

        Assert.Equal(expected, request);
        Assert.Equal(RazerFeatureReport.CalculateCrc(request), request[89]);
    }

    [Fact]
    public void GetHapticsControl_BuildsAnEmptyClass04Request()
    {
        var request = RazerHapticsProtocol.CreateGetHapticsControlRequest(0x2A);

        var expected = new byte[RazerFeatureReport.Length];
        expected[2] = 0x2A;
        expected[7] = 0x04;
        expected[8] = 0x82;
        expected[89] = 0x86;

        Assert.Equal(expected, request);
        Assert.Equal(0, request[6]);
        Assert.Equal(0x86, request[89]);
    }

    [Fact]
    public void ParseHapticsControl_ReadsEnabledAndRawLittleEndianStrength()
    {
        var request = RazerHapticsProtocol.CreateGetHapticsControlRequest(0x2A);

        var state = RazerHapticsProtocol.ParseHapticsControl(
            Response(0x2A, 4, RazerHapticsProtocol.GetCommandId, 0x01, 0x34, 0x12, 0x00),
            request);

        Assert.True(state.Enabled);
        Assert.Equal(0x1234, state.Strength);
    }

    [Fact]
    public void ParseHapticsControl_AcceptsTheThreeByteMinimum()
    {
        var request = RazerHapticsProtocol.CreateGetHapticsControlRequest(0x2A);

        var state = RazerHapticsProtocol.ParseHapticsControl(
            Response(0x2A, 3, RazerHapticsProtocol.GetCommandId, 0x00, 0xFF, 0xFF),
            request);

        Assert.False(state.Enabled);
        Assert.Equal(0xFFFF, state.Strength);
    }

    [Fact]
    public void ParseHapticsControl_RejectsShortMismatchedOrCorruptResponses()
    {
        var request = RazerHapticsProtocol.CreateGetHapticsControlRequest(0x2A);

        Assert.Throws<InvalidOperationException>(
            () => RazerHapticsProtocol.ParseHapticsControl(new byte[20], request));
        Assert.Throws<InvalidOperationException>(
            () => RazerHapticsProtocol.ParseHapticsControl(
                Response(0x2A, 2, RazerHapticsProtocol.GetCommandId, 0x01, 0x34),
                request));
        Assert.Throws<InvalidOperationException>(
            () => RazerHapticsProtocol.ParseHapticsControl(
                Response(0x2A, 81, RazerHapticsProtocol.GetCommandId, 0x01, 0x34, 0x12),
                request));
        Assert.Throws<InvalidOperationException>(
            () => RazerHapticsProtocol.ParseHapticsControl(
                Response(0x2B, 3, RazerHapticsProtocol.GetCommandId, 0x01, 0x34, 0x12),
                request));
        Assert.Throws<InvalidOperationException>(
            () => RazerHapticsProtocol.ParseHapticsControl(
                Response(0x2A, 3, 0x03, 0x01, 0x34, 0x12),
                request));
        Assert.Throws<InvalidOperationException>(
            () => RazerHapticsProtocol.ParseHapticsControl(
                Response(0x2A, 3, RazerHapticsProtocol.SetCommandId, 0x01, 0x34, 0x12),
                request));

        var corrupt = Response(0x2A, 3, RazerHapticsProtocol.GetCommandId, 0x01, 0x34, 0x12);
        corrupt[89] ^= 0xFF;
        Assert.Throws<InvalidOperationException>(
            () => RazerHapticsProtocol.ParseHapticsControl(corrupt, request));
    }

    [Fact]
    public void SetHapticsControl_RejectsStrengthOutsideTheRawUInt16Range()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => RazerHapticsProtocol.CreateSetHapticsControlRequest(0x2A, true, -1));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => RazerHapticsProtocol.CreateSetHapticsControlRequest(0x2A, true, 0x10000));
    }

    [Fact]
    public void SetHapticsControl_EncodesDisabledZeroStrength()
    {
        var request = RazerHapticsProtocol.CreateSetHapticsControlRequest(0x2A, false, 0);

        Assert.Equal((0x03, 0x04, 0x02), Command(request));
        Assert.Equal(0x00, request[9]);
        Assert.Equal(0x00, request[10]);
        Assert.Equal(0x00, request[11]);
        Assert.Equal(RazerFeatureReport.CalculateCrc(request), request[89]);
    }

    private static (byte Size, byte Class, byte Command) Command(byte[] request) =>
        (request[6], request[7], request[8]);

    private static byte[] Response(
        byte transaction,
        byte dataSize,
        byte commandId,
        params byte[] arguments)
    {
        var response = new byte[RazerFeatureReport.Length];
        response[1] = 0x02;
        response[2] = transaction;
        response[6] = dataSize;
        response[7] = RazerHapticsProtocol.CommandClass;
        response[8] = commandId;
        arguments.CopyTo(response.AsSpan(RazerFeatureReport.ArgumentsOffset));
        response[89] = RazerFeatureReport.CalculateCrc(response);
        return response;
    }
}
