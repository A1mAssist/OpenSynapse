using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.Windows.Lighting;

/// <summary>
/// Direction used by the software-rendered color wheel.
/// </summary>
public enum QuickLightingDirection
{
    Clockwise,
    CounterClockwise,
}

/// <summary>
/// A keyboard input event expressed in Blade matrix coordinates and effect time.
/// Input adapters are responsible for translating Windows key events to these coordinates.
/// </summary>
public readonly record struct QuickLightingKeyEvent(int Row, int Column, TimeSpan At);

/// <summary>
/// Deterministic software effects for the Blade 6 x 17 matrix.
/// This class has no device, clock, capture, audio, or input dependencies.
/// </summary>
public static partial class QuickLightingEngine
{
    public const int Rows = BladeLightingProtocol.Rows;
    public const int Columns = BladeLightingProtocol.Columns;
    public const int PixelCount = Rows * Columns;
    public const int LogicalRows = BladeLightingLayout.LogicalRows;
    public const int LogicalColumns = BladeLightingLayout.LogicalColumns;

    private const int FireKeyFrameCount = 100;
    private const int FireInterpolationFrames = 5;
    private const int FireWorkRows = 7;
    private const int FireWorkColumns = 23;
    private const double WheelPeriodSeconds = 4;
    private const double BreathingPeriodMilliseconds = 7000;
    private const double SpectrumPeriodMilliseconds = 37740;
    private const int LightingFramesPerSecond = 25;
    private const double AudioAttackSeconds = 0.045;
    private const double AudioReleaseSeconds = 0.12;
    private const double AudioSilenceThreshold = 0.02;
    public static LightingPalette DefaultSpectrumPalette { get; } = LightingPalette.Create(
        new RazerRgb(255, 0, 0),
        new RazerRgb(0, 255, 0),
        new RazerRgb(0, 0, 255),
        new RazerRgb(255, 0, 0));
    private static readonly byte[] FireSourceMask = Convert.FromHexString(
        "0806040606040202020406060406080604020204060810");

    private static readonly byte[][] FirePropagationMasks =
    [
        Convert.FromHexString("1008060808060402040608080608100806040406081020"),
        Convert.FromHexString("2010081010080604060810100810201008060608102040"),
        Convert.FromHexString("4020102020100806081020201020402010080810204060"),
        Convert.FromHexString("6040204040201008102040402040604020101020406080"),
        Convert.FromHexString("8060406060402010204060604060806040202040608080"),
        Convert.FromHexString("8080608080604020406080806080808060404060808080"),
    ];

    private static readonly byte[][] FireColorLookup =
    [
        Convert.FromHexString("E0E0C0E0E0C0A080A0C0E0E0C0E0E0E0C0A0A0C0E0E0E0"),
        Convert.FromHexString("E0C0A0C0C0A0806080A0C0C0A0C0E0C0A08080A0C0E0E0"),
        Convert.FromHexString("C0A080A0A08060406080A0A080A0C0A080606080A0C0E0"),
        Convert.FromHexString("A080608080604020406080806080A0806040406080A0C0"),
        Convert.FromHexString("80604060604020102040606040608060402020406080A0"),
        Convert.FromHexString("6040204040201008102040402040604020101020406080"),
        Convert.FromHexString("4020102020100808081020201020402010080810204060"),
    ];
}

internal sealed class StarlightLightingRenderer
{
    private const int MaximumStars = 20;
    private const int EffectFrames = 42;
    private const int RegenerationFrames = 5;
    private readonly RazerRgb _color;
    private readonly List<Star> _stars = [];
    private uint _randomState;
    private long _nextFrame;
    private RazerRgb[] _lastFrame = new RazerRgb[QuickLightingEngine.PixelCount];

    public StarlightLightingRenderer(RazerRgb color, int seed)
    {
        _color = color;
        _randomState = unchecked((uint)seed);
    }

    public IReadOnlyList<RazerRgb> Render(TimeSpan elapsed)
    {
        if (elapsed < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(elapsed));
        }

        var targetFrame = (long)(elapsed.TotalMilliseconds * 25 / 1000);
        if (targetFrame < _nextFrame - 1)
        {
            throw new InvalidOperationException("Starlight timestamps must increase monotonically.");
        }
        while (_nextFrame <= targetFrame)
        {
            RenderNextFrame();
            _nextFrame++;
        }
        return _lastFrame;
    }

    private void RenderNextFrame()
    {
        _stars.RemoveAll(star => star.Age >= star.FrameCount);
        if (_nextFrame % RegenerationFrames == 0)
        {
            SpawnStars();
        }

        var frame = new RazerRgb[QuickLightingEngine.PixelCount];
        foreach (var star in _stars)
        {
            var position = star.FrameCount == 1 ? 1 : star.Age / (double)(star.FrameCount - 1);
            var factor = position switch
            {
                <= 0.33 => position / 0.33,
                <= 0.67 => (0.67 - position) / 0.34,
                _ => 0,
            };
            frame[star.Position] = new RazerRgb(
                Scale(_color.Red, factor * star.Intensity / 100),
                Scale(_color.Green, factor * star.Intensity / 100),
                Scale(_color.Blue, factor * star.Intensity / 100));
            star.Age++;
        }
        _lastFrame = frame;
    }

    private void SpawnStars()
    {
        var free = MaximumStars - _stars.Count;
        var spawnCount = NextRandom(free + 1);
        for (var index = 0; index < spawnCount; index++)
        {
            var position = NextRandom(QuickLightingEngine.PixelCount);
            if (_stars.Any(star => star.Position == position))
            {
                break;
            }
            _stars.Add(new Star(
                position,
                NextRandom(EffectFrames) + 5,
                NextRandom(80) + 20));
        }
    }

    private int NextRandom(int exclusiveMaximum)
    {
        _randomState = unchecked(_randomState * 0x343FD + 0x269EC3);
        return (int)((_randomState >> 16) & 0x7FFF) % exclusiveMaximum;
    }

    private static byte Scale(byte value, double factor) =>
        checked((byte)Math.Clamp((int)(value * factor), 0, 255));

    private sealed class Star(int position, int frameCount, int intensity)
    {
        public int Position { get; } = position;
        public int FrameCount { get; } = frameCount;
        public int Intensity { get; } = intensity;
        public int Age { get; set; }
    }
}
