using OpenSynapse.Core.Devices;
using OpenSynapse.Core.Sensors;
using Xunit;

namespace OpenSynapse.Core.Tests;

public sealed class PowerSourceResolverTests
{
    [Fact]
    public void BatteryStatusWins()
    {
        Assert.Equal(
            PowerSourceKind.Battery,
            PowerSourceResolver.Resolve(false, new BladeAdapterPowerSnapshot(0x0C, 0x0C, 200, 200)));
    }

    [Fact]
    public void ZeroWattsOverridesAStaleAcReading()
    {
        Assert.Equal(
            PowerSourceKind.Battery,
            PowerSourceResolver.Resolve(true, new BladeAdapterPowerSnapshot(0x00, 0x0C, 0, 200)));
    }

    [Fact]
    public void KnownFullAdapterIsClassifiedFromWattage()
    {
        Assert.Equal(
            PowerSourceKind.FullPowerAdapter,
            PowerSourceResolver.Resolve(true, new BladeAdapterPowerSnapshot(0x0C, 0x0C, 200, 200)));
    }

    [Fact]
    public void KnownLowerWattageAdapterIsLimited()
    {
        Assert.Equal(
            PowerSourceKind.LowPowerAdapter,
            PowerSourceResolver.Resolve(true, new BladeAdapterPowerSnapshot(0x0C, 0x11, 200, 280)));
    }

    [Fact]
    public void UnmappedLevelsFallBackToTheSystemAcReading()
    {
        Assert.Equal(
            PowerSourceKind.FullPowerAdapter,
            PowerSourceResolver.Resolve(true, new BladeAdapterPowerSnapshot(0x10, 0x11, null, 280)));
    }

    [Fact]
    public void MissingEvidenceStaysUnknown()
    {
        Assert.Equal(PowerSourceKind.Unknown, PowerSourceResolver.Resolve(null, null));
    }
}
