using OpenSynapse.Windows.Devices;
using Xunit;

namespace OpenSynapse.Core.Tests;

public sealed class RazerFilterDriverAdmissionTests
{
    [Theory]
    [InlineData(0x0F1F, false, true)]
    [InlineData(0x0F1F, true, false)]
    [InlineData(0x0F1B, false, false)]
    [InlineData(0x056B, false, false)]
    public void OnlyTheArgbControllerWithoutAnEndpointNeedsTheDriver(
        ushort productId,
        bool endpointReady,
        bool expected)
    {
        Assert.Equal(expected, RazerFilterDriverAdmission.NeedsDriver(productId, endpointReady));
    }
}
