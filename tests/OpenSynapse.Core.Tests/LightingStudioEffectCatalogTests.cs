using OpenSynapse.Core.Lighting;
using Xunit;
using static OpenSynapse.Core.Lighting.StudioParameterField;

namespace OpenSynapse.Core.Tests;

public sealed class LightingStudioEffectCatalogTests
{
    private static readonly StudioColorStop[] Rainbow7 =
    [
        new(0, new StudioRgb(0xFF, 0x00, 0x00)),
        new(26, new StudioRgb(0xFF, 0xCC, 0x00)),
        new(42, new StudioRgb(0x36, 0xFF, 0x48)),
        new(55, new StudioRgb(0x62, 0xF1, 0xFF)),
        new(68, new StudioRgb(0x00, 0x0C, 0xFF)),
        new(81, new StudioRgb(0x80, 0x00, 0xFF)),
        new(100, new StudioRgb(0xFF, 0x00, 0x00)),
    ];

    private static readonly StudioColorStop[] Audio3 =
    [
        new(0, new StudioRgb(0x00, 0xFF, 0x00)),
        new(51, new StudioRgb(0xFF, 0xFF, 0x00)),
        new(100, new StudioRgb(0xFF, 0x00, 0x00)),
    ];

    [Fact]
    public void CatalogCoversEveryEffectWithMappedUiValue()
    {
        var kinds = Enum.GetValues<StudioEffectKind>();

        Assert.Equal(14, kinds.Length);
        Assert.Equal(14, StudioEffectCatalog.All.Count);
        Assert.Equal(
            kinds.OrderBy(kind => kind).ToArray(),
            StudioEffectCatalog.All.Select(descriptor => descriptor.Effect).OrderBy(effect => effect).ToArray());

        foreach (var descriptor in StudioEffectCatalog.All)
        {
            Assert.Equal(StudioEffectMap.ToUiValue(descriptor.Effect), descriptor.UiValue);
            Assert.Same(descriptor, StudioEffectCatalog.Get(descriptor.Effect));
        }

        var uniqueValues = StudioEffectCatalog.All
            .Where(descriptor => descriptor.Effect != StudioEffectKind.Gradient)
            .Select(descriptor => descriptor.UiValue)
            .Distinct()
            .Count();
        Assert.Equal(13, uniqueValues);
        Assert.Equal(
            StudioEffectCatalog.Get(StudioEffectKind.Spectrum).UiValue,
            StudioEffectCatalog.Get(StudioEffectKind.Gradient).UiValue);
    }

    [Fact]
    public void WaveDefaultsMatchChroma()
    {
        var parameters = StudioEffectCatalog.CreateDefault(StudioEffectKind.Wave);

        Assert.Equal(StudioEffectKind.Wave, parameters.Effect);
        Assert.Equal(new StudioRgb(0xFF, 0x00, 0x00), parameters.Primary);
        Assert.Equal(new StudioRgb(0xFF, 0x00, 0x00), parameters.Secondary);
        Assert.Equal(Rainbow7, parameters.ColorStops.ToArray());
        Assert.Equal(100d, parameters.Width);
        Assert.Equal(15d, parameters.Speed);
        Assert.Equal(0, parameters.PauseMs);
        Assert.Equal(90, parameters.Angle);
        Assert.Equal(0d, parameters.Split);
        Assert.Equal(StudioPlaybackStart.Random, parameters.PlaybackStart);
        Assert.Equal(StudioPlaybackEnd.Never, parameters.PlaybackEnd);
        Assert.Equal(-1, parameters.Cycles);
    }

    [Fact]
    public void RippleDefaultsMatchChroma()
    {
        var parameters = StudioEffectCatalog.CreateDefault(StudioEffectKind.Ripple);

        Assert.Equal(Rainbow7, parameters.ColorStops.ToArray());
        Assert.Equal(300d, parameters.Width);
        Assert.Equal(10d, parameters.Speed);
        Assert.Equal(StudioPlaybackStart.OnPress, parameters.PlaybackStart);
        Assert.Equal(StudioPlaybackEnd.After, parameters.PlaybackEnd);
        Assert.Equal(1, parameters.Cycles);
    }

    [Fact]
    public void StarlightDefaultsMatchChroma()
    {
        var parameters = StudioEffectCatalog.CreateDefault(StudioEffectKind.Starlight);

        Assert.Equal(Rainbow7, parameters.ColorStops.ToArray());
        Assert.False(parameters.RandomColor);
        Assert.Equal(1d, parameters.Density);
        Assert.Equal(1_700, parameters.DurationMs);
    }

    [Fact]
    public void AudioDefaultsMatchChroma()
    {
        var parameters = StudioEffectCatalog.CreateDefault(StudioEffectKind.Audio);

        Assert.Equal(new StudioRgb(0x00, 0xFF, 0x00), parameters.Primary);
        Assert.Equal(new StudioRgb(0xFF, 0x00, 0x00), parameters.Secondary);
        Assert.Equal(Audio3, parameters.ColorStops.ToArray());
        Assert.Equal(1d, parameters.Boost);
        Assert.False(parameters.AutoBoost);
        Assert.Equal(1d, parameters.Decay);
    }

    [Fact]
    public void TidalDefaultsMatchChroma()
    {
        var parameters = StudioEffectCatalog.CreateDefault(StudioEffectKind.Tidal);

        Assert.Equal(new StudioRgb(0x00, 0xFF, 0x00), parameters.Primary);
        Assert.Equal(new StudioRgb(0x00, 0x00, 0xFF), parameters.Secondary);
        Assert.False(parameters.RandomColor);
        Assert.Equal(10d, parameters.Speed);
        Assert.Equal(160, parameters.Angle);
        Assert.Equal(100d, parameters.Split);
        Assert.Equal(StudioPlaybackStart.Random, parameters.PlaybackStart);
        Assert.Equal(StudioPlaybackEnd.Never, parameters.PlaybackEnd);
        Assert.Equal(-1, parameters.Cycles);
    }

    [Fact]
    public void ApplyDefaultsReplacesStaleValuesAndKeepsMedia()
    {
        var parameters = new StudioEffectParams
        {
            Effect = StudioEffectKind.Wave,
            Primary = new StudioRgb(0x12, 0x34, 0x56),
            Secondary = StudioRgb.White,
            RandomColor = true,
            DurationMs = 1_234,
            PauseMs = 4_321,
            Cycles = 7,
            PlaybackStart = StudioPlaybackStart.OnSelectedKeys,
            PlaybackEnd = StudioPlaybackEnd.Toggle,
            Speed = 999,
            Width = 1,
            Angle = 200,
            Split = 75,
            Density = 5,
            Boost = 3,
            AutoBoost = true,
            Decay = 1.5,
            Blur = 9,
            Screen = "left",
            ScreenRect = new StudioScreenRect(0.1, 0.2, 0.3, 0.4),
            MediaSrc = "clip.mp4",
            CurrentConfigJson = "{\"pattern\":\"1\"}",
        };
        parameters.ColorStops.Add(new StudioColorStop(50, StudioRgb.White));

        StudioEffectCatalog.ApplyDefaults(parameters);

        Assert.Equal(StudioEffectKind.Wave, parameters.Effect);
        Assert.Equal(Rainbow7, parameters.ColorStops.ToArray());
        Assert.Equal(100d, parameters.Width);
        Assert.Equal(15d, parameters.Speed);
        Assert.Equal(0, parameters.PauseMs);
        Assert.Equal(90, parameters.Angle);
        Assert.Equal(0d, parameters.Split);
        Assert.Equal(0, parameters.DurationMs);
        Assert.Equal(StudioPlaybackStart.Random, parameters.PlaybackStart);
        Assert.Equal(StudioPlaybackEnd.Never, parameters.PlaybackEnd);
        Assert.Equal(-1, parameters.Cycles);
        Assert.Equal("clip.mp4", parameters.MediaSrc);
        Assert.Equal("{\"pattern\":\"1\"}", parameters.CurrentConfigJson);
    }

    [Fact]
    public void FieldsMatchTheInspectorControls()
    {
        Assert.Equal(
            new[] { ColorStops, Width, Speed, Pause, Angle, Split, PlaybackStart, PlaybackEnd, Cycles },
            StudioEffectCatalog.Get(StudioEffectKind.Wave).Fields.ToArray());
        Assert.Equal(
            new[] { Screen, ScreenRect, Blur },
            StudioEffectCatalog.Get(StudioEffectKind.Ambient).Fields.ToArray());
        Assert.Equal(
            new[] { MediaSrc, GenerateConfig },
            StudioEffectCatalog.Get(StudioEffectKind.ChromaGenerate).Fields.ToArray());
    }

    [Fact]
    public void WaveWidthRangeMatchesChroma()
    {
        var range = StudioEffectCatalog.Get(StudioEffectKind.Wave).Ranges[Width];

        Assert.Equal(new StudioParameterRange(10, 400, 1, 100), range);
    }

    [Fact]
    public void EveryScalarFieldHasARange()
    {
        foreach (var descriptor in StudioEffectCatalog.All)
        {
            foreach (var field in descriptor.Fields)
            {
                if (field is ColorStops or ScreenRect or MediaSrc or GenerateConfig)
                {
                    continue;
                }

                Assert.True(
                    descriptor.Ranges.ContainsKey(field),
                    $"{descriptor.Effect} is missing a range for {field}.");
            }
        }
    }

    [Fact]
    public void DefaultsDoNotSharePaletteState()
    {
        var first = StudioEffectCatalog.CreateDefault(StudioEffectKind.Wave);
        first.ColorStops.Clear();

        var second = StudioEffectCatalog.CreateDefault(StudioEffectKind.Wave);

        Assert.Equal(Rainbow7, second.ColorStops.ToArray());
    }
}
