using OpenSynapse.Windows.Devices;
using OpenSynapse.Windows.Protocols;
using Xunit;

namespace OpenSynapse.Core.Tests;

public sealed class OpenRazerMappingTests
{
    [Fact]
    public void SetAssignmentUsesTheMiddlewarePayloadLayout()
    {
        var request = OpenRazerMappingProtocol.SetAssignment(
            0x04, OpenRazerMappingProtocol.NormalMode, OpenRazerMappingFunctions.ButtonCode, [0x05]);

        Assert.Equal(0x02, request.Report[7]);
        Assert.Equal(0x0C, request.Report[8]);
        Assert.Equal(80, request.Report[6]);
        Assert.Equal(
            new byte[] { 0x01, 0x04, 0x00, 0x01, 0x01, 0x05 },
            request.Report.AsSpan(RazerFeatureReport.ArgumentsOffset, 6).ToArray());
    }

    [Fact]
    public void GetAssignmentSendsProfileButtonAndMode()
    {
        var request = OpenRazerMappingProtocol.GetAssignment(0x03, OpenRazerMappingProtocol.HypershiftMode);

        Assert.Equal(0x02, request.Report[7]);
        Assert.Equal(0x8C, request.Report[8]);
        Assert.Equal(
            new byte[] { 0x01, 0x03, 0x01 },
            request.Report.AsSpan(RazerFeatureReport.ArgumentsOffset, 3).ToArray());
    }

    [Fact]
    public void AssignmentResponseParsesButtonModeFunctionAndData()
    {
        var response = Response([0x01, 0x04, 0x00, 0x01, 0x01, 0x05]);

        var assignment = OpenRazerMappingProtocol.ParseAssignment(response);

        Assert.Equal(0x04, assignment.ButtonId);
        Assert.Equal(0x00, assignment.Mode);
        Assert.Equal(OpenRazerMappingFunctions.ButtonCode, assignment.FunctionId);
        Assert.Equal([0x05], assignment.Data);
    }

    [Fact]
    public void ButtonListResponseCarriesTheDeviceIds()
    {
        var response = Response([0x03, 0x01, 0x02, 0x04]);

        Assert.Equal([0x01, 0x02, 0x04], OpenRazerMappingProtocol.ParseButtonList(response));
    }

    [Fact]
    public void MappingCapabilitiesFollowTheMiddlewareMouseList()
    {
        Assert.True(OpenRazerDeviceCatalog.HasOnboardMapping(0x00E5));
        Assert.True(OpenRazerDeviceCatalog.HasOnboardMapping(0x00A2));
        Assert.False(OpenRazerDeviceCatalog.HasOnboardMapping(0x00C1));
    }

    private static byte[] Response(byte[] arguments)
    {
        var response = new byte[RazerFeatureReport.Length];
        response[1] = 0x02;
        response[6] = (byte)arguments.Length;
        arguments.CopyTo(response, RazerFeatureReport.ArgumentsOffset);
        response[89] = RazerFeatureReport.CalculateCrc(response);
        return response;
    }
}
