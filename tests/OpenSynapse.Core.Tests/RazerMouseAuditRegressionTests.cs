using OpenSynapse.Core.Devices;
using OpenSynapse.Windows.Devices;
using OpenSynapse.Windows.Protocols;
using Xunit;

namespace OpenSynapse.Core.Tests;

public sealed class RazerMouseAuditRegressionTests
{
    private const string DockPath = "HID#VID_1532&PID_00A4#dock";

    [Theory]
    [InlineData(0xBF, 0xE0)]
    [InlineData(0x80, 0xFF)]
    [InlineData(0x84, 0xFF)]
    [InlineData(0x46, 0x00)]
    [InlineData(0x41, 0x00)]
    public void DockPreparationMatchesTheLocalLinkerTransactionContract(byte command, byte expected)
    {
        var sequence = -1;
        Assert.Equal(expected, RazerFeatureTransport.PrepareTransactionId(
            DockPath, 0x1F, 0x00, command, ref sequence));
    }

    [Fact]
    public void DockTransactionsWrapBeforeTheWildcardFf()
    {
        var sequence = -1;
        for (var i = 0; i < 96; i++)
        {
            var transaction = RazerFeatureTransport.PrepareTransactionId(
                DockPath, 0xFF, 0x00, 0xBF, ref sequence);
            Assert.Equal((byte)(0xE0 | (i % 31)), transaction);
            Assert.NotEqual(0xFF, transaction);
        }
    }

    [Theory]
    [InlineData("HID#VID_1532&PID_0088#receiver")]
    [InlineData("HID#VID_1532&PID_00AA#mouse")]
    [InlineData("HID#VID_0001&PID_00A4#not-razer")]
    public void NonLinkerPreparationKeepsPinnedFfAndExistingLowBitSequence(string path)
    {
        var sequence = -1;
        Assert.Equal(0xFF, RazerFeatureTransport.PrepareTransactionId(path, 0xFF, 0x00, 0x0A, ref sequence));
        Assert.Equal(-1, sequence);
        for (var i = 0; i < 34; i++)
            Assert.Equal((byte)(i & 0x1F), RazerFeatureTransport.PrepareTransactionId(path, 0xE0, 0x00, 0x80, ref sequence));
    }

    [Theory]
    [InlineData(1, 10)]
    [InlineData(2, 17)]
    [InlineData(5, 38)]
    public void ViperSetStageLengthIsThreePlusSevenPerStage(int count, byte expectedLength)
    {
        var stages = Enumerable.Range(1, count).Select(i => new ViperDpiStage((byte)i, 800 * i, 800 * i)).ToArray();
        var request = ViperProduct184Protocol.CreateSetDpiStagesRequest(new ViperDpiStagesState(1, stages));
        Assert.Equal(RazerFeatureReport.Length, request.Length);
        Assert.Equal(expectedLength, request[6]);
        Assert.Equal(RazerFeatureReport.CalculateCrc(request), request[89]);
        var capability = RazerDeviceRegistry.BuiltIn.Find(0x1532, 0x00B8)!.GetRequiredCapability(CapabilityIds.DpiStagesSet);
        var configured = capability.CreateRequest(request.AsSpan(RazerFeatureReport.ArgumentsOffset, request[6]), request[6]);
        Assert.Equal(expectedLength, configured[6]);
        for (var i = 0; i < count; i++)
            Assert.Equal((byte)i, request[RazerFeatureReport.ArgumentsOffset + 3 + (7 * i)]);
    }
}
