namespace OpenSynapse.Core.Audio;

/// <summary>Keeps a delayed device read from replacing an EQ draft or a newer write.</summary>
public sealed class EqualizerReadback
{
    private string? _preset;
    private double[]? _bands;
    private long _revision;

    public void Accept(string? preset, IEnumerable<double> bands)
    {
        _preset = preset;
        _bands = bands.ToArray();
        Invalidate();
    }

    public void Invalidate() => _revision++;

    public void AcceptPreset(string? preset)
    {
        _preset = preset;
        Invalidate();
    }

    public long? Begin(string? preset, IEnumerable<double> bands) =>
        Matches(preset, bands) ? _revision : null;

    public bool CanApply(long revision, string? preset, IEnumerable<double> bands) =>
        revision == _revision && Matches(preset, bands);

    private bool Matches(string? preset, IEnumerable<double> bands) =>
        _bands is not null && StringComparer.Ordinal.Equals(_preset, preset) &&
        _bands.SequenceEqual(bands);
}
