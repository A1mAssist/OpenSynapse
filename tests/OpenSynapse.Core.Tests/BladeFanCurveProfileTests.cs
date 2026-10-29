using OpenSynapse.Core.Devices;
using OpenSynapse.Core.Profiles;
using Xunit;

namespace OpenSynapse.Core.Tests;

public sealed class BladeFanCurveProfileTests
{
    [Fact]
    public async Task IndependentCurveRoundTripsThroughProfileStore()
    {
        var curve = new BladeFanCurve(
            BladeFanCurveTemperatureMode.Independent,
            [
                new(45, 2400, 2200),
                new(85, 4800, 2200),
            ],
            [
                new(55, 2200, 3200),
                new(90, 2200, 5200),
            ]);
        var document = ProfileDocument.CreateDefault();
        document.Global.Blade.FanCurve = BladeFanCurveProfile.FromCurve(curve);
        var path = Path.Combine(
            Path.GetTempPath(),
            $"opensynapse-fan-curve-{Guid.NewGuid():N}.json");

        try
        {
            await ProfileStore.ExportAsync(document, path);
            var loaded = await ProfileStore.ImportAsync(path);
            var saved = Assert.IsType<BladeFanCurveProfile>(
                loaded.Global.Blade.FanCurve);

            Assert.Equal(BladeFanCurveTemperatureMode.Independent, saved.TemperatureMode);
            Assert.Equal(curve.CpuPoints, saved.CpuPoints);
            Assert.Equal(curve.GpuPoints, saved.GpuPoints);

            var restored = saved.CreateCurve();
            Assert.Equal(curve.CpuPoints, restored.CpuPoints);
            Assert.Equal(curve.GpuPoints, restored.GpuPoints);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
