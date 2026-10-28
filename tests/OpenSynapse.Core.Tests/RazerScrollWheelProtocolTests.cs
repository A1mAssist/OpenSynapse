using OpenSynapse.Windows.Protocols;
using Xunit;

namespace OpenSynapse.Core.Tests;

public sealed class RazerScrollWheelProtocolTests
{
    [Fact]
    public void BuildersUseTheMiddlewareTuplesAndArguments()
    {
        var getFreeNotched = RazerScrollWheelProtocol.CreateGetScrollFreeNotchedModeRequest(0x05, 0x03);
        var setFreeNotched = RazerScrollWheelProtocol.CreateSetScrollFreeNotchedModeRequest(
            0x06,
            RazerScrollFreeNotchedMode.FreeSpinMode,
            0x03);
        var getAcceleration = RazerScrollWheelProtocol.CreateGetScrollVirtualAccelerationLevelRequest(0x07, 0x04);
        var setAcceleration = RazerScrollWheelProtocol.CreateSetScrollVirtualAccelerationLevelRequest(0x08, 0x02, 0x04);

        Assert.Equal(0, (byte)RazerScrollFreeNotchedMode.NotchedMode);
        Assert.Equal(1, (byte)RazerScrollFreeNotchedMode.FreeSpinMode);

        AssertFrame(getFreeNotched, 0x05, 0x94, [0x03, 0x00]);
        AssertFrame(setFreeNotched, 0x06, 0x14, [0x03, 0x01]);
        AssertFrame(getAcceleration, 0x07, 0xA2, [0x04, 0x00]);
        AssertFrame(setAcceleration, 0x08, 0x22, [0x04, 0x02]);
    }

    [Fact]
    public void ParsersReadClassAndValueAtArgumentsOffset()
    {
        var freeNotchedRequest = RazerScrollWheelProtocol.CreateGetScrollFreeNotchedModeRequest(0x05, 0x03);
        var freeNotched = RazerScrollWheelProtocol.ParseScrollFreeNotchedMode(
            Response(0x05, 0x94, 0x02, 0x03, 0x01),
            freeNotchedRequest);

        Assert.Equal(0x03, freeNotched.ClassId);
        Assert.Equal(RazerScrollFreeNotchedMode.FreeSpinMode, freeNotched.Mode);

        var accelerationRequest = RazerScrollWheelProtocol.CreateGetScrollVirtualAccelerationLevelRequest(0x06, 0x04);
        var acceleration = RazerScrollWheelProtocol.ParseScrollVirtualAccelerationLevel(
            Response(0x06, 0xA2, 0x02, 0x04, 0x02),
            accelerationRequest);

        Assert.Equal(0x04, acceleration.ClassId);
        Assert.Equal(0x02, acceleration.Level);
    }

    [Fact]
    public void TruncatedResponsesAreRejected()
    {
        var freeNotchedRequest = RazerScrollWheelProtocol.CreateGetScrollFreeNotchedModeRequest(0x05, 0x03);
        var accelerationRequest = RazerScrollWheelProtocol.CreateGetScrollVirtualAccelerationLevelRequest(0x06, 0x04);

        Assert.Throws<InvalidOperationException>(
            () => RazerScrollWheelProtocol.ParseScrollFreeNotchedMode(new byte[20], freeNotchedRequest));
        Assert.Throws<InvalidOperationException>(
            () => RazerScrollWheelProtocol.ParseScrollFreeNotchedMode(
                Response(0x05, 0x94, 0x01, 0x03),
                freeNotchedRequest));
        Assert.Throws<InvalidOperationException>(
            () => RazerScrollWheelProtocol.ParseScrollVirtualAccelerationLevel(new byte[20], accelerationRequest));
        Assert.Throws<InvalidOperationException>(
            () => RazerScrollWheelProtocol.ParseScrollVirtualAccelerationLevel(
                Response(0x06, 0xA2, 0x01, 0x04),
                accelerationRequest));
    }

    [Fact]
    public void UnknownFreeNotchedModeIsRejected()
    {
        var request = RazerScrollWheelProtocol.CreateGetScrollFreeNotchedModeRequest(0x05, 0x03);

        Assert.Throws<InvalidOperationException>(
            () => RazerScrollWheelProtocol.ParseScrollFreeNotchedMode(
                Response(0x05, 0x94, 0x02, 0x03, 0x02),
                request));
    }

    private static void AssertFrame(byte[] frame, byte transaction, byte commandId, params byte[] arguments)
    {
        Assert.Equal(RazerFeatureReport.Length, frame.Length);
        Assert.Equal(
            new byte[] { 0x00, 0x00, transaction, 0x00, 0x00, 0x00, 0x02, 0x02, commandId },
            frame[..RazerFeatureReport.ArgumentsOffset]);
        Assert.Equal(
            arguments,
            frame.AsSpan(RazerFeatureReport.ArgumentsOffset, arguments.Length).ToArray());
        Assert.All(
            frame[(RazerFeatureReport.ArgumentsOffset + arguments.Length)..89],
            value => Assert.Equal(0, value));
        Assert.Equal(RazerFeatureReport.CalculateCrc(frame), frame[89]);
        Assert.Equal(0, frame[90]);
    }

    private static byte[] Response(byte transaction, byte commandId, byte dataSize, params byte[] arguments)
    {
        var response = new byte[RazerFeatureReport.Length];
        response[1] = 0x02;
        response[2] = transaction;
        response[6] = dataSize;
        response[7] = 0x02;
        response[8] = commandId;
        arguments.CopyTo(response.AsSpan(RazerFeatureReport.ArgumentsOffset));
        response[89] = RazerFeatureReport.CalculateCrc(response);
        return response;
    }
}
