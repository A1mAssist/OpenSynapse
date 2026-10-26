using OpenSynapse.Core.Displays;
using OpenSynapse.Windows.Lifecycle;
using Xunit;
using Xunit.Abstractions;

namespace OpenSynapse.Core.Tests;

public sealed class GpuDeviceStateProbe
{
    private readonly ITestOutputHelper _output;

    public GpuDeviceStateProbe(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public void TheRealAdapterStateMatchesTheWindowsProblemCode()
    {
        var status = WindowsDedicatedGpuMode.GetStatus();
        _output.WriteLine(
            $"state={status.DeviceState} problem={status.ProblemCode} " +
            $"supported={status.IsSupported} active={status.IsActive} " +
            $"canChange={status.CanChange} reason={status.BlockReason}");

        if (status.DeviceState.IsFaulted())
        {
            Assert.NotEqual(0, status.ProblemCode);
            Assert.Equal(DedicatedGpuModeBlockReason.DiscreteGpuFaulted, status.BlockReason);
            Assert.False(status.CanChange);
        }
        else if (status.DeviceState.IsDisabled())
        {
            Assert.True(status.IsActive);
        }
    }
}
