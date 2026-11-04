using static OpenSynapse.Core.Lighting.StudioParameterField;

namespace OpenSynapse.Core.Lighting;

public enum StudioParameterField
{
    Color,
    Color2,
    ColorStops,
    RandomColor,
    Duration,
    Pause,
    Cycles,
    PlaybackStart,
    PlaybackEnd,
    Speed,
    Width,
    Angle,
    Split,
    CenterPoint,
    Counterclockwise,
    Density,
    Boost,
    AutoBoost,
    Decay,
    Screen,
    ScreenRect,
    Blur,
    MediaSrc,
    GenerateConfig,
}

public sealed record StudioParameterRange(double Minimum, double Maximum, double Step, double Default);

public sealed record StudioEffectDescriptor(
    StudioEffectKind Effect,
    int UiValue,
    IReadOnlyList<StudioParameterField> Fields,
    IReadOnlyDictionary<StudioParameterField, StudioParameterRange> Ranges);

/// <summary>
/// Inspector metadata for the studio effects. Values mirror Razer Chroma
/// Studio layer params; Gradient is an OpenSynapse extension that shares
/// Spectrum's UI id, and Radiate is the engine-only effect 17.
/// </summary>
public static class StudioEffectCatalog
{
    private const int ColorMaximum = 16_777_215;
    private const int ScreenMaximum = 5;

    private static readonly StudioRgb Green = new(0x00, 0xFF, 0x00);
    private static readonly StudioRgb Red = new(0xFF, 0x00, 0x00);
    private static readonly StudioRgb Orange = new(0xFF, 0x80, 0x00);
    private static readonly StudioRgb Blue = new(0x00, 0x00, 0xFF);
    private static readonly StudioRgb GradientPrimary = new(0x44, 0xD6, 0x2C);
    private static readonly StudioRgb GradientSecondary = new(0x00, 0xBF, 0xFF);

    private static readonly StudioColorStop[] Rainbow7 =
    [
        new(0, Red),
        new(26, new StudioRgb(0xFF, 0xCC, 0x00)),
        new(42, new StudioRgb(0x36, 0xFF, 0x48)),
        new(55, new StudioRgb(0x62, 0xF1, 0xFF)),
        new(68, new StudioRgb(0x00, 0x0C, 0xFF)),
        new(81, new StudioRgb(0x80, 0x00, 0xFF)),
        new(100, Red),
    ];

    private static readonly StudioColorStop[] Audio3 =
    [
        new(0, Green),
        new(51, new StudioRgb(0xFF, 0xFF, 0x00)),
        new(100, Red),
    ];

    public static IReadOnlyList<StudioEffectDescriptor> All { get; } = BuildAll();

    private static readonly IReadOnlyDictionary<StudioEffectKind, StudioEffectDescriptor> ByEffect =
        All.ToDictionary(descriptor => descriptor.Effect);

    public static StudioEffectDescriptor Get(StudioEffectKind effect) =>
        ByEffect.TryGetValue(effect, out var descriptor)
            ? descriptor
            : throw new ArgumentOutOfRangeException(nameof(effect), effect, "Unknown studio effect.");

    public static StudioEffectParams CreateDefault(StudioEffectKind effect)
    {
        var parameters = CreateDefaults(effect);
        parameters.Normalize();
        return parameters;
    }

    public static void ApplyDefaults(StudioEffectParams parameters)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        var defaults = CreateDefaults(parameters.Effect);

        parameters.Primary = defaults.Primary;
        parameters.Secondary = defaults.Secondary;
        parameters.RandomColor = defaults.RandomColor;
        parameters.DurationMs = defaults.DurationMs;
        parameters.PauseMs = defaults.PauseMs;
        parameters.Cycles = defaults.Cycles;
        parameters.PlaybackStart = defaults.PlaybackStart;
        parameters.PlaybackEnd = defaults.PlaybackEnd;
        parameters.Speed = defaults.Speed;
        parameters.Width = defaults.Width;
        parameters.Angle = defaults.Angle;
        parameters.Split = defaults.Split;
        parameters.Density = defaults.Density;
        parameters.Boost = defaults.Boost;
        parameters.AutoBoost = defaults.AutoBoost;
        parameters.Decay = defaults.Decay;
        parameters.Blur = defaults.Blur;
        parameters.CenterPointX = defaults.CenterPointX;
        parameters.CenterPointY = defaults.CenterPointY;
        parameters.CenterRow = defaults.CenterRow;
        parameters.CenterCol = defaults.CenterCol;
        parameters.Counterclockwise = defaults.Counterclockwise;
        parameters.DeviceWithCenterPointJson = defaults.DeviceWithCenterPointJson;
        parameters.Screen = defaults.Screen;
        parameters.ScreenRect = defaults.ScreenRect;
        parameters.ColorStopsCustom = defaults.ColorStopsCustom;

        parameters.ColorStops.Clear();
        parameters.ColorStops.AddRange(defaults.ColorStops);
    }

    private static StudioEffectParams CreateDefaults(StudioEffectKind effect)
    {
        var parameters = new StudioEffectParams { Effect = effect };
        switch (effect)
        {
            case StudioEffectKind.Solid:
                parameters.Primary = Green;
                parameters.Secondary = Green;
                break;
            case StudioEffectKind.Breathing:
                parameters.Primary = Green;
                parameters.Secondary = Green;
                parameters.RandomColor = false;
                parameters.DurationMs = 14_000;
                parameters.PlaybackStart = StudioPlaybackStart.Random;
                parameters.PlaybackEnd = StudioPlaybackEnd.Never;
                parameters.Cycles = -1;
                break;
            case StudioEffectKind.Spectrum:
                UsePalette(parameters, Rainbow7);
                parameters.DurationMs = 37_740;
                break;
            case StudioEffectKind.Wave:
                UsePalette(parameters, Rainbow7);
                parameters.Width = 100;
                parameters.Speed = 15;
                parameters.PauseMs = 0;
                parameters.Angle = 90;
                parameters.Split = 0;
                parameters.PlaybackStart = StudioPlaybackStart.Random;
                parameters.PlaybackEnd = StudioPlaybackEnd.Never;
                parameters.Cycles = -1;
                break;
            case StudioEffectKind.Reactive:
                parameters.Primary = Green;
                parameters.Secondary = Green;
                parameters.RandomColor = false;
                parameters.DurationMs = 1_500;
                parameters.Decay = 0.1;
                parameters.PlaybackStart = StudioPlaybackStart.OnPress;
                parameters.PlaybackEnd = StudioPlaybackEnd.After;
                parameters.Cycles = 1;
                break;
            case StudioEffectKind.Ripple:
                UsePalette(parameters, Rainbow7);
                parameters.Width = 300;
                parameters.Speed = 10;
                parameters.PlaybackStart = StudioPlaybackStart.OnPress;
                parameters.PlaybackEnd = StudioPlaybackEnd.After;
                parameters.Cycles = 1;
                break;
            case StudioEffectKind.Starlight:
                UsePalette(parameters, Rainbow7);
                parameters.RandomColor = false;
                parameters.Density = 1;
                parameters.DurationMs = 1_700;
                break;
            case StudioEffectKind.Fire:
                parameters.Primary = Red;
                parameters.Secondary = Orange;
                break;
            case StudioEffectKind.Ambient:
                parameters.Screen = "full";
                parameters.ScreenRect = new StudioScreenRect(0, 0, 1, 1);
                parameters.Blur = 5;
                break;
            case StudioEffectKind.Audio:
                UsePalette(parameters, Audio3);
                parameters.Boost = 1;
                parameters.AutoBoost = false;
                parameters.Decay = 1;
                break;
            case StudioEffectKind.Wheel:
                UsePalette(parameters, Rainbow7);
                parameters.Speed = 60;
                parameters.CenterRow = StudioEffectParams.WheelCenterMaximum / 2;
                parameters.CenterCol = StudioEffectParams.WheelCenterMaximum / 2;
                parameters.PlaybackStart = StudioPlaybackStart.Random;
                parameters.PlaybackEnd = StudioPlaybackEnd.Never;
                parameters.Cycles = -1;
                break;
            case StudioEffectKind.ChromaGenerate:
                break;
            case StudioEffectKind.Tidal:
                parameters.Primary = Green;
                parameters.Secondary = Blue;
                parameters.RandomColor = false;
                parameters.Width = 100;
                parameters.Speed = 10;
                parameters.Angle = 160;
                parameters.Split = 100;
                parameters.PlaybackStart = StudioPlaybackStart.Random;
                parameters.PlaybackEnd = StudioPlaybackEnd.Never;
                parameters.Cycles = -1;
                break;
            case StudioEffectKind.Gradient:
                parameters.Primary = GradientPrimary;
                parameters.Secondary = GradientSecondary;
                break;
            case StudioEffectKind.Radiate:
                UsePalette(parameters, Rainbow7);
                parameters.PlaybackStart = StudioPlaybackStart.OnPress;
                parameters.PlaybackEnd = StudioPlaybackEnd.After;
                parameters.Cycles = 1;
                break;
        }

        return parameters;
    }

    private static void UsePalette(StudioEffectParams parameters, StudioColorStop[] palette)
    {
        parameters.Primary = palette[0].Color;
        parameters.Secondary = palette[^1].Color;
        parameters.ColorStops.AddRange(palette);
    }

    private static StudioEffectDescriptor[] BuildAll() =>
    [
        Describe(StudioEffectKind.Solid,
            Fields(Color),
            Ranges((Color, ColorRange(Green)))),

        Describe(StudioEffectKind.Breathing,
            Fields(Color, Color2, RandomColor, Duration, PlaybackStart, PlaybackEnd, Cycles),
            Ranges(
                (Color, ColorRange(Green)),
                (Color2, ColorRange(Green)),
                (RandomColor, Toggle()),
                (Duration, new StudioParameterRange(6_000, 28_000, 1, 14_000)),
                (PlaybackStart, Choice(0)),
                (PlaybackEnd, Choice(0)),
                (Cycles, new StudioParameterRange(-1, 100, 1, -1)))),

        Describe(StudioEffectKind.Spectrum,
            Fields(ColorStops, Duration),
            Ranges((Duration, new StudioParameterRange(1_000, 120_000, 1, 37_740)))),

        Describe(StudioEffectKind.Wave,
            Fields(ColorStops, Width, Speed, Pause, Angle, Split, PlaybackStart, PlaybackEnd, Cycles),
            Ranges(
                (Width, new StudioParameterRange(10, 400, 1, 100)),
                (Speed, new StudioParameterRange(0, 50, 1, 15)),
                (Pause, new StudioParameterRange(0, 60_000, 1, 0)),
                (Angle, new StudioParameterRange(0, 359, 1, 90)),
                (Split, new StudioParameterRange(0, 100, 100, 0)),
                (PlaybackStart, Choice(0)),
                (PlaybackEnd, Choice(0)),
                (Cycles, new StudioParameterRange(-1, 100, 1, -1)))),

        Describe(StudioEffectKind.Reactive,
            Fields(Color, RandomColor, Duration, PlaybackStart, PlaybackEnd, Cycles),
            Ranges(
                (Color, ColorRange(Green)),
                (RandomColor, Toggle()),
                (Duration, new StudioParameterRange(500, 2_000, 1, 1_500)),
                (PlaybackStart, Choice(1)),
                (PlaybackEnd, Choice(1)),
                (Cycles, new StudioParameterRange(-1, 100, 1, 1)))),

        Describe(StudioEffectKind.Ripple,
            Fields(ColorStops, Width, Speed, PlaybackStart, PlaybackEnd, Cycles),
            Ranges(
                (Width, new StudioParameterRange(100, 400, 100, 300)),
                (Speed, new StudioParameterRange(1, 50, 1, 10)),
                (PlaybackStart, Choice(1)),
                (PlaybackEnd, Choice(1)),
                (Cycles, new StudioParameterRange(-1, 100, 1, 1)))),

        Describe(StudioEffectKind.Starlight,
            Fields(ColorStops, RandomColor, Density, Duration),
            Ranges(
                (RandomColor, Toggle()),
                (Density, new StudioParameterRange(1, 10, 1, 1)),
                (Duration, new StudioParameterRange(500, 3_000, 1, 1_700)))),

        Describe(StudioEffectKind.Fire,
            Fields(Color, Color2),
            Ranges(
                (Color, ColorRange(Red)),
                (Color2, ColorRange(Orange)))),

        Describe(StudioEffectKind.Ambient,
            Fields(Screen, ScreenRect, Blur),
            Ranges(
                (Screen, new StudioParameterRange(0, ScreenMaximum, 1, 0)),
                (Blur, new StudioParameterRange(1, 9, 1, 5)))),

        Describe(StudioEffectKind.Audio,
            Fields(ColorStops, Boost, AutoBoost, Decay),
            Ranges(
                (Boost, new StudioParameterRange(0.25, 4, 0.25, 1)),
                (AutoBoost, Toggle()),
                (Decay, new StudioParameterRange(0.1, 2, 0.1, 1)))),

        Describe(StudioEffectKind.Wheel,
            Fields(ColorStops, Speed, CenterPoint, PlaybackStart, PlaybackEnd, Cycles),
            Ranges(
                (Speed, new StudioParameterRange(0, 360, 1, 60)),
                (CenterPoint, new StudioParameterRange(
                    0,
                    StudioEffectParams.WheelCenterMaximum,
                    1,
                    StudioEffectParams.WheelCenterMaximum / 2)),
                (PlaybackStart, Choice(0)),
                (PlaybackEnd, Choice(0)),
                (Cycles, new StudioParameterRange(-1, 100, 1, -1)))),

        Describe(StudioEffectKind.ChromaGenerate,
            Fields(MediaSrc, GenerateConfig),
            Ranges()),

        Describe(StudioEffectKind.Tidal,
            Fields(Color, Color2, RandomColor, Speed, Angle, Split, CenterPoint, Counterclockwise, PlaybackStart, PlaybackEnd, Cycles),
            Ranges(
                (Color, ColorRange(Green)),
                (Color2, ColorRange(Blue)),
                (RandomColor, Toggle()),
                (Speed, new StudioParameterRange(0, 50, 1, 10)),
                (Angle, new StudioParameterRange(0, 359, 1, 160)),
                (Split, new StudioParameterRange(0, 100, 100, 100)),
                (CenterPoint, new StudioParameterRange(0, 10_000, 40, 5_000)),
                (Counterclockwise, Toggle()),
                (PlaybackStart, Choice(0)),
                (PlaybackEnd, Choice(0)),
                (Cycles, new StudioParameterRange(-1, 100, 1, -1)))),

        Describe(StudioEffectKind.Gradient,
            Fields(Color, Color2),
            Ranges(
                (Color, ColorRange(GradientPrimary)),
                (Color2, ColorRange(GradientSecondary)))),

        Describe(StudioEffectKind.Radiate,
            Fields(ColorStops),
            Ranges()),
    ];

    private static StudioEffectDescriptor Describe(
        StudioEffectKind effect,
        IReadOnlyList<StudioParameterField> fields,
        IReadOnlyDictionary<StudioParameterField, StudioParameterRange> ranges) =>
        new(effect, StudioEffectMap.ToUiValue(effect), fields, ranges);

    private static IReadOnlyList<StudioParameterField> Fields(params StudioParameterField[] fields) => fields;

    private static IReadOnlyDictionary<StudioParameterField, StudioParameterRange> Ranges(
        params (StudioParameterField Field, StudioParameterRange Range)[] entries)
    {
        var ranges = new Dictionary<StudioParameterField, StudioParameterRange>(entries.Length);
        foreach (var entry in entries)
        {
            ranges[entry.Field] = entry.Range;
        }

        return ranges;
    }

    private static StudioParameterRange ColorRange(StudioRgb color) =>
        new(0, ColorMaximum, 1, Pack(color));

    private static StudioParameterRange Toggle(bool value = false) =>
        new(0, 1, 1, value ? 1 : 0);

    private static StudioParameterRange Choice(int defaultValue) =>
        new(0, 2, 1, defaultValue);

    private static int Pack(StudioRgb color) => (color.R << 16) | (color.G << 8) | color.B;
}
