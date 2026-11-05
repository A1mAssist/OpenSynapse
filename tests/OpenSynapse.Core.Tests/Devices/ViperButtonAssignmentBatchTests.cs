using OpenSynapse.Core.Devices;
using OpenSynapse.Windows.Devices;
using OpenSynapse.Windows.Protocols;
using Xunit;

namespace OpenSynapse.Core.Tests;

public sealed class ViperButtonAssignmentBatchTests
{
    [Fact]
    public void RequiresEveryProduct184ButtonAndLayerExactlyOnce()
    {
        byte[] buttonIds = [1, 2, 3, 4, 5, 9, 10, 96];
        var complete = buttonIds.SelectMany(buttonId => new[]
        {
            Assignment(buttonId, ViperButtonMappingLayer.Normal),
            Assignment(buttonId, ViperButtonMappingLayer.HyperShift),
        }).ToArray();

        var validated = ViperTelemetryReader.ValidateViperButtonAssignmentBatch(complete);

        Assert.Equal(16, validated.Count);
        Assert.Equal(ViperObmMappingMode.Normal, validated[0].Mode);
        Assert.Equal(ViperObmMappingMode.HyperShift, validated[1].Mode);

        complete[^1] = complete[0];
        Assert.Throws<ArgumentException>(() =>
            ViperTelemetryReader.ValidateViperButtonAssignmentBatch(complete));
    }

    [Fact]
    public void MediaKeyAssignmentUsesTheTwoByteUsage()
    {
        var request = ViperObmProtocol.CreateSetAssignmentRequest(
            new ViperObmAssignment(
                1,
                4,
                ViperObmMappingMode.Normal,
                ViperObmFunctionId.MediaKeys,
                [0x00, 0xEA]));

        var offset = RazerFeatureReport.ArgumentsOffset;
        Assert.Equal((byte)ViperObmFunctionId.MediaKeys, request[offset + 3]);
        Assert.Equal((byte)2, request[offset + 4]);
        Assert.Equal([0x00, 0xEA], request[(offset + 5)..(offset + 7)]);
    }

    private static ViperButtonAssignment Assignment(
        byte buttonId,
        ViperButtonMappingLayer layer) =>
        new(1, buttonId, layer, ViperButtonMappingFunction.Off, []);
}
