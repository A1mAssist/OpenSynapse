namespace OpenSynapse.Core.Profiles;

public sealed class HapticScrollProfile
{
    public byte ActiveStage { get; set; } = 1;
    public List<byte> StageTypes { get; set; } = [];
    public HapticScrollCustomProfile? Custom { get; set; }

    public HapticScrollProfile Clone() => new()
    {
        ActiveStage = ActiveStage, StageTypes = [.. StageTypes], Custom = Custom?.Clone(),
    };

    public void Validate()
    {
        if (ActiveStage is < 1 or > 6 || StageTypes is null || StageTypes.Count is < 1 or > 6 || ActiveStage > StageTypes.Count ||
            StageTypes.Any(type => type is < 129 or > 134) || StageTypes.Distinct().Count() != StageTypes.Count)
            throw new InvalidDataException("Hapticore requires an active stage from 1 to 6 and a list of distinct stage types from 129 to 134.");
        Custom?.Validate();
    }

    public bool ContentEquals(HapticScrollProfile other) => ActiveStage == other.ActiveStage &&
        StageTypes.SequenceEqual(other.StageTypes) &&
        (Custom is null ? other.Custom is null : other.Custom is not null && Custom.ContentEquals(other.Custom));
}

public readonly record struct HapticScrollPoint(float X, float Y);

/// <summary>Native metadata and float32 tables, retained verbatim for profile replay and rollback.</summary>
public sealed class HapticScrollCustomProfile
{
    public byte[] Metadata { get; set; } = new byte[40];
    public bool Enabled { get; set; }
    public byte AlternateCurrent { get; set; }
    public float ReductionAngleCw { get; set; }
    public float IndexCountAngleCw { get; set; }
    public float ReductionAngleCcw { get; set; }
    public float IndexCountAngleCcw { get; set; }
    public List<HapticScrollPoint> Cw { get; set; } = [];
    public List<HapticScrollPoint> Ccw { get; set; } = [];
    public List<HapticScrollPoint> Velocity { get; set; } = [];

    public HapticScrollCustomProfile Clone() => new()
    {
        Metadata = [.. Metadata], Enabled = Enabled, AlternateCurrent = AlternateCurrent,
        ReductionAngleCw = ReductionAngleCw, IndexCountAngleCw = IndexCountAngleCw,
        ReductionAngleCcw = ReductionAngleCcw, IndexCountAngleCcw = IndexCountAngleCcw,
        Cw = [.. Cw], Ccw = [.. Ccw], Velocity = [.. Velocity],
    };

    public void Validate()
    {
        if (Metadata is not { Length: 40 } ||
            !float.IsFinite(ReductionAngleCw) || !float.IsFinite(IndexCountAngleCw) ||
            !float.IsFinite(ReductionAngleCcw) || !float.IsFinite(IndexCountAngleCcw))
            throw new InvalidDataException("Hapticore custom data requires 40 metadata bytes and finite generator values.");
        foreach (var table in new[] { Cw, Ccw, Velocity })
            if (table is null || table.Count > ushort.MaxValue ||
                table.Any(point => !float.IsFinite(point.X) || !float.IsFinite(point.Y)))
                throw new InvalidDataException("Hapticore tables must contain finite float32 coordinates and fit a 16-bit count.");
    }

    public bool ContentEquals(HapticScrollCustomProfile other) =>
        Metadata.SequenceEqual(other.Metadata) && Enabled == other.Enabled && AlternateCurrent == other.AlternateCurrent &&
        ReductionAngleCw == other.ReductionAngleCw && IndexCountAngleCw == other.IndexCountAngleCw &&
        ReductionAngleCcw == other.ReductionAngleCcw && IndexCountAngleCcw == other.IndexCountAngleCcw &&
        Cw.SequenceEqual(other.Cw) && Ccw.SequenceEqual(other.Ccw) && Velocity.SequenceEqual(other.Velocity);
}
