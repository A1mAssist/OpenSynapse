using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.Core.Tests;

public sealed class ViperObmProtocolTests
{
    [Fact]
    public void BuildsOfficialReadHeaders()
    {
        AssertHeader(ViperObmProtocol.CreateGetMaximumProfilesRequest(), 0x01, 0x05, 0x8A, []);
        AssertHeader(ViperObmProtocol.CreateGetProfileCountRequest(), 0x01, 0x05, 0x80, []);
        AssertHeader(ViperObmProtocol.CreateGetProfileIdsRequest(), 0x50, 0x05, 0x81, []);
        AssertHeader(ViperObmProtocol.CreateGetButtonIdsRequest(), 0x50, 0x02, 0x84, []);
        AssertHeader(
            ViperObmProtocol.CreateGetAssignmentRequest(2, 5, ViperObmMappingMode.HyperShift),
            0x50,
            0x02,
            0x8C,
            [2, 5, 1]);
    }

    [Fact]
    public void BuildsOfficialSetAssignmentWithFiveByteLayout()
    {
        var report = ViperObmProtocol.CreateSetAssignmentRequest(new(
            1,
            5,
            ViperObmMappingMode.HyperShift,
            ViperObmFunctionId.KeyCode,
            new byte[] { 0x02, 0x04 }));

        AssertHeader(report, 0x50, 0x02, 0x0C, [1, 5, 1, 2, 2, 2, 4, 0, 0, 0]);
        Assert.All(report[19..89], value => Assert.Equal((byte)0, value));
        Assert.Throws<ArgumentException>(() =>
            ViperObmProtocol.CreateSetAssignmentRequest(new(
                1, 5, ViperObmMappingMode.Normal, ViperObmFunctionId.ButtonCode,
                new byte[] { 1, 2, 3, 4, 5, 6 })));
    }

    [Fact]
    public void RejectsSharedFunctionsOutsideProduct184MappingWhitelist()
    {
        foreach (var function in new[]
        {
            ViperObmFunctionId.MacroTypeI,
            ViperObmFunctionId.Profile,
            ViperObmFunctionId.Lighting,
            ViperObmFunctionId.PowerKeys,
            ViperObmFunctionId.Controller,
            ViperObmFunctionId.RazerKey,
            ViperObmFunctionId.WindowsShortcutsKey,
        })
        {
            Assert.Throws<ArgumentException>(() =>
                ViperObmProtocol.CreateSetAssignmentRequest(new(
                    1, 5, ViperObmMappingMode.Normal, function, Array.Empty<byte>())));
        }

        Assert.Throws<ArgumentException>(() =>
            ViperObmProtocol.CreateSetAssignmentRequest(new(
                1, 5, ViperObmMappingMode.Normal, ViperObmFunctionId.ModeButtonKey,
                new byte[] { 89 })));
        Assert.Throws<ArgumentException>(() =>
            ViperObmProtocol.CreateSetAssignmentRequest(new(
                1, 5, ViperObmMappingMode.Normal, ViperObmFunctionId.Dpi,
                new byte[] { 3 })));
    }

    [Fact]
    public void ParsesProfilesButtonsAndBothMappingLayers()
    {
        Assert.Equal(5, ViperObmProtocol.ParseMaximumProfiles(
            Response(ViperObmProtocol.CreateGetMaximumProfilesRequest(), 5)));
        Assert.Equal(2, ViperObmProtocol.ParseProfileCount(
            Response(ViperObmProtocol.CreateGetProfileCountRequest(), 2)));
        Assert.Equal([1, 3], ViperObmProtocol.ParseProfileIds(
            VariableResponse(ViperObmProtocol.CreateGetProfileIdsRequest(), 2, 1, 3)));
        Assert.Equal([1, 2, 4, 5], ViperObmProtocol.ParseButtonIds(
            VariableResponse(ViperObmProtocol.CreateGetButtonIdsRequest(), 4, 1, 2, 4, 5)));

        var request = ViperObmProtocol.CreateGetAssignmentRequest(3, 5, ViperObmMappingMode.HyperShift);
        var assignment = ViperObmProtocol.ParseAssignment(
            AssignmentResponse(request, 3, 5, 1, (byte)ViperObmFunctionId.KeyCode, 2, 0x02, 0x04),
            3,
            5,
            ViperObmMappingMode.HyperShift);

        Assert.Equal(ViperObmFunctionId.KeyCode, assignment.Function);
        Assert.Equal([0x02, 0x04], assignment.FunctionData);
    }

    [Fact]
    public void RejectsMalformedOrMismatchedResponses()
    {
        Assert.Throws<InvalidOperationException>(() => ViperObmProtocol.ParseMaximumProfiles(
            VariableResponse(ViperObmProtocol.CreateGetMaximumProfilesRequest(), 5, 0)));
        Assert.Throws<InvalidOperationException>(() => ViperObmProtocol.ParseProfileIds(
            Response(ViperObmProtocol.CreateGetProfileIdsRequest(), 2, 1, 1)));
        Assert.Throws<InvalidOperationException>(() => ViperObmProtocol.ParseProfileIds(
            VariableResponse(ViperObmProtocol.CreateGetProfileIdsRequest(), 1, 1, 2)));
        Assert.Throws<InvalidOperationException>(() => ViperObmProtocol.ParseButtonIds(
            Response(ViperObmProtocol.CreateGetButtonIdsRequest(), 0)));

        var request = ViperObmProtocol.CreateGetAssignmentRequest(1, 2, ViperObmMappingMode.Normal);
        Assert.Throws<InvalidOperationException>(() => ViperObmProtocol.ParseAssignment(
            AssignmentResponse(request, 1, 3, 0, 1, 1, 1),
            1,
            2,
            ViperObmMappingMode.Normal));
        Assert.Throws<InvalidOperationException>(() => ViperObmProtocol.ParseAssignment(
            AssignmentResponse(request, 1, 2, 0, 0xFF, 0),
            1,
            2,
            ViperObmMappingMode.Normal));
        Assert.Throws<InvalidOperationException>(() => ViperObmProtocol.ParseAssignment(
            AssignmentResponse(request, 1, 2, 0, 1, 6),
            1,
            2,
            ViperObmMappingMode.Normal));
    }

    [Fact]
    public void RejectsAssignmentPayloadOutsideDeclaredLengthOrProductLayout()
    {
        var request = ViperObmProtocol.CreateGetAssignmentRequest(1, 2, ViperObmMappingMode.Normal);

        Assert.Throws<InvalidOperationException>(() => ViperObmProtocol.ParseAssignment(
            VariableResponse(request, 1, 2, 1, (byte)ViperObmFunctionId.ButtonCode, 1),
            1,
            2,
            ViperObmMappingMode.Normal));
        Assert.Throws<InvalidOperationException>(() => ViperObmProtocol.ParseAssignment(
            AssignmentResponse(request, 1, 2, 1, (byte)ViperObmFunctionId.Off, 1, 0),
            1,
            2,
            ViperObmMappingMode.Normal));

        var oversized = AssignmentResponse(
            request, 1, 2, 1, (byte)ViperObmFunctionId.ButtonCode, 1, 2);
        oversized[6] = 0x50;
        oversized[89] = RazerFeatureReport.CalculateCrc(oversized);
        Assert.Throws<InvalidOperationException>(() => ViperObmProtocol.ParseAssignment(
            oversized, 1, 2, ViperObmMappingMode.Normal));
    }

    [Fact]
    public void UsesRequestedLayerWhenProduct184ReturnsModeOneForNormal()
    {
        var request = ViperObmProtocol.CreateGetAssignmentRequest(1, 1, ViperObmMappingMode.Normal);

        var assignment = ViperObmProtocol.ParseAssignment(
            AssignmentResponse(request, 1, 1, 1, (byte)ViperObmFunctionId.ButtonCode, 1, 1),
            1,
            1,
            ViperObmMappingMode.Normal);

        Assert.Equal(ViperObmMappingMode.Normal, assignment.Mode);
    }

    [Fact]
    public void RejectsNonZeroFunctionDataPadding()
    {
        var request = ViperObmProtocol.CreateGetAssignmentRequest(1, 2, ViperObmMappingMode.Normal);
        var nonZeroPadding = AssignmentResponse(
            request, 1, 2, 1, (byte)ViperObmFunctionId.ButtonCode, 1, 1, 0x7F);
        Assert.Throws<InvalidOperationException>(() => ViperObmProtocol.ParseAssignment(
            nonZeroPadding, request, 1, 2, ViperObmMappingMode.Normal));
    }

    private static byte[] Response(byte[] request, params byte[] arguments)
    {
        var response = RazerFeatureReport.CreateRequest(
            request[2],
            request[6],
            request[7],
            request[8],
            arguments);
        response[1] = 0x02;
        return response;
    }

    private static byte[] VariableResponse(byte[] request, params byte[] arguments)
    {
        var response = RazerFeatureReport.CreateRequest(
            request[2],
            checked((byte)arguments.Length),
            request[7],
            request[8],
            arguments);
        response[1] = 0x02;
        return response;
    }

    private static byte[] AssignmentResponse(byte[] request, params byte[] arguments)
    {
        var response = RazerFeatureReport.CreateRequest(
            request[2],
            0x0A,
            request[7],
            request[8],
            arguments);
        response[1] = 0x02;
        return response;
    }

    private static void AssertHeader(
        byte[] report,
        byte dataSize,
        byte commandClass,
        byte commandId,
        byte[] arguments)
    {
        Assert.Equal(ViperProduct184Protocol.TransactionId, report[2]);
        Assert.Equal(dataSize, report[6]);
        Assert.Equal(commandClass, report[7]);
        Assert.Equal(commandId, report[8]);
        Assert.Equal(arguments, report[RazerFeatureReport.ArgumentsOffset..(RazerFeatureReport.ArgumentsOffset + arguments.Length)]);
        Assert.Equal(RazerFeatureReport.CalculateCrc(report), report[89]);
    }
}
