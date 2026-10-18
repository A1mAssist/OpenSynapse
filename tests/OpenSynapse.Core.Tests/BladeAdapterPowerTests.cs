using OpenSynapse.Core.Devices;
using OpenSynapse.Windows.Protocols;
using Xunit;

namespace OpenSynapse.Core.Tests;

public sealed class BladeAdapterPowerTests
{
    [Fact]
    public void ParseReadsCurrentAndRecommendedWattage()
    {
        var request = BladeAdapterPowerProtocol.CreateGetRequest();
        var response = request.ToArray();
        response[1] = 0x02;
        response[3] = 0x02;
        response[RazerFeatureReport.ArgumentsOffset] = 0x0C;
        response[RazerFeatureReport.ArgumentsOffset + 1] = 0x0C;
        response[89] = RazerFeatureReport.CalculateCrc(response);

        var snapshot = BladeAdapterPowerProtocol.Parse(response, request);

        Assert.Equal(new BladeAdapterPowerSnapshot(0x0C, 0x0C, 200, 200), snapshot);
        Assert.False(snapshot.IsLimitedAdapter);
    }

    [Fact]
    public void ParseDoesNotCompareUnmappedRawLevels()
    {
        var request = BladeAdapterPowerProtocol.CreateGetRequest();
        var response = request.ToArray();
        response[1] = 0x02;
        response[RazerFeatureReport.ArgumentsOffset] = 0x0F;
        response[RazerFeatureReport.ArgumentsOffset + 1] = 0x11;
        response[89] = RazerFeatureReport.CalculateCrc(response);

        var snapshot = BladeAdapterPowerProtocol.Parse(response, request);

        Assert.Equal((310, 280), (snapshot.ActualWatts, snapshot.RecommendedWatts));
        Assert.False(snapshot.IsLimitedAdapter);
    }

    [Theory]
    [InlineData(0x00, 0)]
    [InlineData(0x0C, 200)]
    [InlineData(0x0F, 310)]
    [InlineData(0x11, 280)]
    [InlineData(0x13, 400)]
    [InlineData(0x10, null)]
    [InlineData(0xFF, null)]
    public void WattageLevelsMatchTheVendorTable(byte level, int? expected)
    {
        Assert.Equal(expected, BladeAdapterPowerProtocol.GetWatts(level));
    }
}
