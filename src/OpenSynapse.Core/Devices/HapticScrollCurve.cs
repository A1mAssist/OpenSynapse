using OpenSynapse.Core.Profiles;

namespace OpenSynapse.Core.Devices;

public readonly record struct HapticScrollControlPoint(byte X, byte Y);

public sealed class HapticScrollDirection
{
    public byte Steps { get; set; } = 15;
    public byte Tension { get; set; } = 11;
    public List<HapticScrollControlPoint> Points { get; set; } = [new(0, 0), new(1, 99), new(50, 100), new(99, 99), new(100, 0)];

    public HapticScrollDirection Clone() => new() { Steps = Steps, Tension = Tension, Points = [.. Points] };

    public void Validate()
    {
        if (Steps == 0 || Tension > 100 || Points is not { Count: 5 } ||
            Points[0] != new HapticScrollControlPoint(0, 0) || Points[4] != new HapticScrollControlPoint(100, 0) ||
            Points[2].X is 0 or >= 100 || Points.Any(p => p.X > 100 || p.Y > 100) ||
            Points.Zip(Points.Skip(1)).Any(pair => pair.First.X > pair.Second.X))
            throw new InvalidDataException("Use 1–255 steps, 0–100 tension and five ordered control points within 0–100, with endpoints (0,0) and (100,0).");
    }
}

public sealed class HapticScrollCurve
{
    public bool Linked { get; set; } = true;
    public HapticScrollDirection Up { get; set; } = new();
    public HapticScrollDirection Down { get; set; } = new();

    public static HapticScrollCurve FromMetadata(byte[] metadata)
    {
        if (metadata is not { Length: 40 } || metadata[1] != 40 || metadata[2] != 6)
            throw new InvalidDataException("The device has no editable custom curve metadata. Read it again or create a default curve.");
        HapticScrollDirection Read(int offset, int parameters) => new()
        {
            Steps = metadata[parameters], Tension = metadata[parameters + 1],
            Points = Enumerable.Range(0, 5).Select(i => new HapticScrollControlPoint(metadata[offset + 2 * i], metadata[offset + 2 * i + 1])).ToList(),
        };
        var curve = new HapticScrollCurve { Linked = metadata[4] != 0, Up = Read(9, 5), Down = Read(21, 7) };
        curve.Up.Validate();
        curve.Down.Validate();
        return curve;
    }

    public HapticScrollCustomProfile Generate(byte profileId)
    {
        if (profileId is < 1 or > 5) throw new ArgumentOutOfRangeException(nameof(profileId));
        Up.Validate();
        Down.Validate();
        var down = Linked ? Up : Down;
        var metadata = new byte[40];
        metadata[0] = profileId; metadata[1] = 40; metadata[2] = 6; metadata[3] = 1;
        metadata[4] = Linked ? (byte)1 : (byte)0;
        metadata[5] = Up.Steps; metadata[6] = Up.Tension; metadata[7] = down.Steps; metadata[8] = down.Tension;
        foreach (var (direction, offset) in new[] { (Up, 9), (down, 21) })
        {
            for (var i = 0; i < 5; i++)
            {
                metadata[offset + i * 2] = direction.Points[i].X;
                metadata[offset + i * 2 + 1] = direction.Points[i].Y;
            }
            metadata[offset + 10] = metadata[offset + 11] = 255;
        }
        var cw = Discretize(Up);
        var ccw = Discretize(down);
        return new HapticScrollCustomProfile
        {
            Metadata = metadata, Enabled = true, AlternateCurrent = 1,
            IndexCountAngleCw = PeakAngle(cw, Up.Steps), IndexCountAngleCcw = PeakAngle(ccw, down.Steps),
            Cw = Scale(cw, Up), Ccw = Scale(ccw, down), Velocity = [new(-720, 1), new(0, 1), new(720, 1)],
        };
    }

    // 00A7/763 O→M→C→b→N: two 50-sample quadratics, forward error .2, then float32 at the wire boundary.
    public static List<(double X, double Y)> Sample(HapticScrollDirection direction)
    {
        direction.Validate();
        var points = direction.Points;
        static (double X, double Y) At(HapticScrollControlPoint a, HapticScrollControlPoint b, HapticScrollControlPoint c, double t) =>
            ((1 - t) * (1 - t) * a.X + 2 * (1 - t) * t * b.X + t * t * c.X,
             (1 - t) * (1 - t) * a.Y + 2 * (1 - t) * t * b.Y + t * t * c.Y);
        var left = new List<(double X, double Y)> { (points[0].X, points[0].Y) };
        var right = new List<(double X, double Y)> { (points[4].X, points[4].Y) };
        for (var i = 1; i <= 50; i++)
        {
            left.Add(At(points[0], points[1], points[2], i / 50d));
            right.Add(At(points[4], points[3], points[2], i / 50d));
        }
        right.Reverse();
        left.AddRange(right.Skip(1));
        return left;
    }

    private static List<(double X, double Y)> Discretize(HapticScrollDirection direction)
    {
        var samples = Sample(direction);
        var result = new List<(double X, double Y)> { samples[0] };
        var anchor = 0;
        var candidate = 1;
        while (candidate < samples.Count - 1)
        {
            var a = samples[anchor];
            var b = samples[candidate];
            var slope = (b.Y - a.Y) / (b.X - a.X);
            var intercept = a.Y - slope * a.X;
            var fits = true;
            for (var i = anchor + 1; i <= candidate; i++)
                if (!(Math.Abs(samples[i].X * slope + intercept - samples[i].Y) <= .2)) { fits = false; break; }
            if (fits) { candidate++; continue; }
            anchor = candidate - 1;
            result.Add(samples[anchor]);
            candidate = anchor + 2;
        }
        result.Add(samples[^1]);
        return result;
    }

    private static List<HapticScrollPoint> Scale(List<(double X, double Y)> points, HapticScrollDirection direction) =>
        points.Select(p => new HapticScrollPoint((float)(360d / direction.Steps * (p.X / 100)),
            (float)(.2 * direction.Tension / 100 * (p.Y / 100)))).ToList();

    private static float PeakAngle(List<(double X, double Y)> points, byte steps) =>
        (float)(360d / steps * (points.First(p => p.Y == points.Max(p => p.Y)).X / 100));
}
