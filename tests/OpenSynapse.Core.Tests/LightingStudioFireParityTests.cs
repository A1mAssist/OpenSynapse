using System.Text.Json;
using OpenSynapse.Core.Lighting;
using Xunit;

namespace OpenSynapse.Core.Tests;

/// <summary>
/// Locks CFireEffect to the native 4.0.55.0 engine. The expected frames were captured
/// from Action 52 after the official effect 8 factory accepted these exact parameters.
/// </summary>
public sealed class LightingStudioFireParityTests
{
    [Fact]
    public void DefaultOracleFramesMatchEveryPixel()
    {
        var fixture = ReadFixture("fire_3x3_default.json");
        var parameters = FireParams();
        var input = new StudioEffectInput(FireSeed: fixture.Seed, FireRate: fixture.Rate);

        foreach (var sample in fixture.Frames)
        {
            var frame = StudioEffectRenderer.Render(
                parameters,
                fixture.Rows,
                fixture.Columns,
                sample.Index / 25.0,
                input);
            Assert.Equal(sample.Expected, frame);
        }
    }

    [Theory]
    [InlineData("fire_6x17_s40_r80.json")]
    [InlineData("fire_9x26_s25_r120.json")]
    public void SecondSeedRateAndGeometryMatchEveryPixel(string fixtureName)
    {
        var fixture = ReadFixture(fixtureName);
        var parameters = FireParams();
        var input = new StudioEffectInput(FireSeed: fixture.Seed, FireRate: fixture.Rate);

        foreach (var sample in fixture.Frames)
        {
            var frame = StudioEffectRenderer.Render(
                parameters,
                fixture.Rows,
                fixture.Columns,
                sample.Index / 25.0,
                input);
            Assert.Equal(sample.Expected, frame);
        }
    }

    [Fact]
    public void DefaultFirePaletteMatchesTheNativeRedOrangeBlackRamp()
    {
        var defaults = new StudioEffectParams
        {
            Effect = StudioEffectKind.Fire,
            Primary = new StudioRgb(255, 0, 0),
            Secondary = new StudioRgb(255, 128, 0),
        };
        var explicitPalette = FireParams();

        for (var frame = 0; frame < 3; frame++)
        {
            Assert.Equal(
                StudioEffectRenderer.Render(explicitPalette, 3, 3, frame / 25.0),
                StudioEffectRenderer.Render(defaults, 3, 3, frame / 25.0));
        }
    }

    private static StudioEffectParams FireParams()
    {
        var parameters = new StudioEffectParams
        {
            Effect = StudioEffectKind.Fire,
            Primary = new StudioRgb(255, 0, 0),
            Secondary = new StudioRgb(255, 128, 0),
        };
        parameters.ColorStops.Add(new StudioColorStop(0, new StudioRgb(255, 0, 0)));
        parameters.ColorStops.Add(new StudioColorStop(50, new StudioRgb(255, 128, 0)));
        parameters.ColorStops.Add(new StudioColorStop(100, StudioRgb.Black));
        return parameters;
    }

    private static Fixture ReadFixture(string name)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "Fire", name);
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var root = document.RootElement;
        var rows = root.GetProperty("rows").GetInt32();
        var columns = root.GetProperty("cols").GetInt32();
        var seed = root.GetProperty("seed").GetInt32();
        var rate = root.GetProperty("rate").GetInt32();
        var frames = root.GetProperty("frames").EnumerateArray()
            .Select(item => new ExpectedFrame(
                item.GetProperty("t_ms").GetInt32() / 40 - 1,
                item.GetProperty("frame").EnumerateArray()
                    .Select(value => new StudioRgb(
                        (byte)((value.GetInt32() >> 16) & 0xFF),
                        (byte)((value.GetInt32() >> 8) & 0xFF),
                        (byte)(value.GetInt32() & 0xFF)))
                    .ToArray()))
            .ToArray();
        return new Fixture(rows, columns, seed, rate, frames);
    }

    private sealed record Fixture(
        int Rows,
        int Columns,
        int Seed,
        int Rate,
        IReadOnlyList<ExpectedFrame> Frames);

    private sealed record ExpectedFrame(int Index, StudioRgb[] Expected);
}
