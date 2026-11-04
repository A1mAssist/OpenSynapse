using OpenSynapse.Core.Lighting;
using Xunit;

namespace OpenSynapse.Core.Tests;

public sealed class StudioLegacyLayoutParserTests
{
    [Fact]
    public void ParsesTheCheckedInFallbackLayouts()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "synapse2-legacy.json");
        var layouts = StudioLegacyLayoutParser.Parse(File.ReadAllText(path));

        Assert.Equal(6, layouts.Count);
        Assert.Equal(117, layouts[532].Leds.Count);
        Assert.Equal(117, layouts[535].Leds.Count);
        Assert.Equal(117, layouts[538].Leds.Count);
        Assert.Equal(15, layouts[68].Leds.Count);
        Assert.Equal(15, layouts[69].Leds.Count);
        Assert.Equal(21, layouts[76].Leds.Count);
        Assert.All(layouts.Values, layout => Assert.All(layout.Leds, led =>
        {
            Assert.InRange(led.DeviceRow, 0, layout.DeviceRows - 1);
            Assert.InRange(led.DeviceCol, 0, layout.DeviceColumns - 1);
        }));
    }
}
