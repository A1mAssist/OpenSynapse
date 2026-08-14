using OpenSynapse.Core.Profiles;
using OpenSynapse.Windows.Lighting;
using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.Core.Tests;

public sealed class BladeLightingProfileCodecTests
{
    [Theory]
    [InlineData("off", BladeLightingMode.Off)]
    [InlineData("spectrum", BladeLightingMode.Spectrum)]
    [InlineData("fire", BladeLightingMode.Fire)]
    public void ParsesParameterlessModes(string name, BladeLightingMode expected)
    {
        Assert.Equal(expected, BladeLightingProfileCodec.Parse(new LightingProfile { Effect = name }).Mode);
    }

    [Theory]
    [InlineData("reactive", BladeLightingMode.Reactive)]
    [InlineData("ripple", BladeLightingMode.Ripple)]
    public void ParsesKeyboardInputModes(string name, BladeLightingMode expected)
    {
        var effect = BladeLightingProfileCodec.Parse(new LightingProfile
        {
            Effect = name,
            Parameters = new(StringComparer.OrdinalIgnoreCase) { ["color"] = "99DD72" },
        });

        Assert.Equal(expected, effect.Mode);
        Assert.Equal("99DD72", BladeLightingProfileCodec.Create(effect).Parameters["color"]);
    }

    [Fact]
    public void ParsesAndCreatesCanonicalColor()
    {
        var effect = BladeLightingProfileCodec.Parse(new LightingProfile
        {
            Effect = "STATIC",
            Parameters = new(StringComparer.OrdinalIgnoreCase) { ["color"] = "99dd72" },
        });

        Assert.Equal(new RazerRgb(0x99, 0xDD, 0x72), effect.Color);
        var profile = BladeLightingProfileCodec.Create(effect);
        Assert.Equal("static", profile.Effect);
        Assert.Equal("99DD72", profile.Parameters["color"]);
    }

    [Theory]
    [InlineData("left", BladeWaveDirection.Left)]
    [InlineData("right", BladeWaveDirection.Right)]
    public void ParsesWaveDirection(string name, BladeWaveDirection expected)
    {
        var effect = BladeLightingProfileCodec.Parse(new LightingProfile
        {
            Effect = "wave",
            Parameters = new(StringComparer.OrdinalIgnoreCase) { ["direction"] = name },
        });

        Assert.Equal(expected, effect.Direction);
        Assert.Equal(name, BladeLightingProfileCodec.Create(effect).Parameters["direction"]);
    }

    [Theory]
    [InlineData("unknown", "")]
    [InlineData("static", "12345")]
    [InlineData("wave", "up")]
    public void RejectsInvalidEffectParameters(string mode, string value)
    {
        var profile = new LightingProfile { Effect = mode };
        if (mode == "static")
        {
            profile.Parameters["color"] = value;
        }
        else if (mode == "wave")
        {
            profile.Parameters["direction"] = value;
        }

        Assert.Throws<InvalidOperationException>(() => BladeLightingProfileCodec.Parse(profile));
    }

    [Fact]
    public void RejectsIrrelevantParameters()
    {
        var profile = new LightingProfile
        {
            Effect = "spectrum",
            Parameters = new(StringComparer.OrdinalIgnoreCase) { ["color"] = "99DD72" },
        };

        Assert.Throws<InvalidOperationException>(() => BladeLightingProfileCodec.Parse(profile));
    }
}
