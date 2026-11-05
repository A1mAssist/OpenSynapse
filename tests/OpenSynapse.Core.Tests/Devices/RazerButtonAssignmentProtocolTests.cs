using OpenSynapse.Windows.Protocols;
using Xunit;

namespace OpenSynapse.Core.Tests;

public sealed class RazerButtonAssignmentProtocolTests
{
    private const byte TransactionId = 0x1F;

    [Fact]
    public void BuildersMatchTheMiddlewareTuplesAndHeaderParams()
    {
        var buttonList = RazerButtonAssignmentProtocol.CreateGetButtonListRequest(TransactionId, 0x02);
        var assignmentList = RazerButtonAssignmentProtocol.CreateGetAssignmentListRequest(TransactionId, 0x02);
        var single = RazerButtonAssignmentProtocol.CreateGetSingleAssignmentRequest(TransactionId, 0x02, 0x04);
        var set = RazerButtonAssignmentProtocol.CreateSetSingleAssignmentRequest(
            TransactionId,
            0x02,
            0x04,
            RazerButtonAssignmentMode.NormalMode,
            RazerButtonFunctionType.ButtonCode,
            [0x05]);

        AssertFrame(buttonList, 0x00, 0x80, 0x00, 0x02);
        AssertFrame(assignmentList, 0x00, 0x83, 0x02, 0x00);
        AssertFrame(single, 0x00, 0x84, 0x02, 0x04);
        AssertFrame(set, 0x06, 0x04, 0x02, 0x04, 0x02, 0x04, 0x00, 0x01, 0x01, 0x05);
    }

    [Fact]
    public void ParsesButtonListAndBothAssignmentListLayouts()
    {
        Assert.Equal([0x01, 0x02, 0x03],
            RazerButtonAssignmentProtocol.ParseButtonList(Response(3, 0x01, 0x02, 0x03)));

        var normal = RazerButtonAssignmentProtocol.ParseAssignmentListEntry(
            Response(7, 0x04, 0xEE, 0x01, 0xEE, 0x05, 0xEE, 0x06),
            RazerButtonAssignmentMode.NormalMode);
        Assert.Equal(0x04, normal.ButtonId);
        Assert.Equal(RazerButtonFunctionType.ButtonCode, normal.FunctionId);
        Assert.Equal(2, normal.DataSize);
        Assert.Equal([0x05, 0x06], normal.Data);

        var hyperShift = RazerButtonAssignmentProtocol.ParseAssignmentListEntry(
            Response(10, 0x04, 0xEE, 0xEE, 0x02, 0xEE, 0x05, 0xEE, 0x06, 0xEE, 0x07),
            RazerButtonAssignmentMode.HypershiftMode);
        Assert.Equal(RazerButtonFunctionType.KeyCode, hyperShift.FunctionId);
        Assert.Equal(3, hyperShift.DataSize);
        Assert.Equal([0x05, 0x06, 0x07], hyperShift.Data);
    }

    [Fact]
    public void ParsesSingleAssignmentAtTheModeSpecificOffsets()
    {
        var normal = RazerButtonAssignmentProtocol.ParseSingleAssignment(
            Response(15,
                0x04, 0xEE, 0x01, 0xEE, 0x02, 0xEE,
                0x05, 0xEE, 0x06, 0xEE, 0x07, 0xEE, 0x08, 0xEE, 0x09),
            RazerButtonAssignmentMode.NormalMode);
        Assert.Equal(0x04, normal.ButtonId);
        Assert.Equal(RazerButtonFunctionType.ButtonCode, normal.FunctionId);
        Assert.Equal(2, normal.DataSize);
        Assert.Equal([0x05, 0x06, 0x07, 0x08, 0x09], normal.Data);

        var hyperShift = RazerButtonAssignmentProtocol.ParseSingleAssignment(
            Response(16,
                0x04, 0xEE, 0xEE, 0x02, 0xEE, 0x03, 0xEE,
                0x05, 0xEE, 0x06, 0xEE, 0x07, 0xEE, 0x08, 0xEE, 0x09),
            RazerButtonAssignmentMode.HypershiftMode);
        Assert.Equal(0x04, hyperShift.ButtonId);
        Assert.Equal(RazerButtonFunctionType.KeyCode, hyperShift.FunctionId);
        Assert.Equal(3, hyperShift.DataSize);
        Assert.Equal([0x05, 0x06, 0x07, 0x08, 0x09], hyperShift.Data);
    }

    [Fact]
    public void RejectsTruncatedResponsesAndOversizedSetData()
    {
        Assert.Throws<InvalidDataException>(() =>
            RazerButtonAssignmentProtocol.ParseSingleAssignment(
                Response(15, new byte[15]),
                RazerButtonAssignmentMode.HypershiftMode));
        Assert.Throws<InvalidDataException>(() =>
            RazerButtonAssignmentProtocol.ParseButtonList(new byte[10]));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            RazerButtonAssignmentProtocol.CreateSetSingleAssignmentRequest(
                TransactionId,
                0,
                4,
                RazerButtonAssignmentMode.NormalMode,
                RazerButtonFunctionType.Off,
                new byte[6]));
    }

    private static void AssertFrame(byte[] frame, byte dataSize, byte commandId, params byte[] arguments)
    {
        Assert.Equal(RazerFeatureReport.Length, frame.Length);
        Assert.Equal(
            new byte[] { 0x00, 0x00, TransactionId, 0x00, 0x00, 0x00, dataSize, 0x08, commandId },
            frame[..RazerFeatureReport.ArgumentsOffset]);
        Assert.Equal(arguments, frame.AsSpan(RazerFeatureReport.ArgumentsOffset, arguments.Length).ToArray());
        Assert.All(
            frame[(RazerFeatureReport.ArgumentsOffset + arguments.Length)..89],
            value => Assert.Equal(0, value));
        Assert.Equal(RazerFeatureReport.CalculateCrc(frame), frame[89]);
        Assert.Equal(0, frame[90]);
    }

    private static byte[] Response(byte dataSize, params byte[] arguments)
    {
        var response = new byte[RazerFeatureReport.Length];
        response[1] = 0x02;
        response[6] = dataSize;
        arguments.CopyTo(response.AsSpan(RazerFeatureReport.ArgumentsOffset));
        response[89] = RazerFeatureReport.CalculateCrc(response);
        return response;
    }
}
