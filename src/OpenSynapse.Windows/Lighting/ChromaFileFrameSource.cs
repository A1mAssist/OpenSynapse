using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.Windows.Lighting;

/// <summary>
/// Plays a parsed .chroma animation on the software frame runtime.
/// </summary>
public sealed class ChromaFileFrameSource : ISoftwareLightingFrameSource
{
    private readonly IReadOnlyList<ChromaFileFrame> _frames;
    private readonly long[] _frameEnds;
    private readonly long _durationTicks;

    /// <param name="useSourceFrames">
    /// True for a keyboard that writes its own matrix; the default keeps the
    /// Blade-mapped frames the lighting controller has always used.
    /// </param>
    public ChromaFileFrameSource(ChromaFileAnimation animation, bool useSourceFrames = false)
    {
        ArgumentNullException.ThrowIfNull(animation);
        _frames = useSourceFrames ? animation.SourceFrames : animation.Frames;
        if (_frames.Count == 0 || animation.Duration <= TimeSpan.Zero)
        {
            throw new ArgumentException("The Chroma animation has no frames.", nameof(animation));
        }

        Rows = animation.SourceRows;
        Columns = animation.SourceColumns;
        _durationTicks = animation.Duration.Ticks;
        _frameEnds = new long[_frames.Count];
        var total = 0L;
        for (var index = 0; index < _frames.Count; index++)
        {
            total += _frames[index].Duration.Ticks;
            _frameEnds[index] = total;
        }
    }

    public int Rows { get; }
    public int Columns { get; }

    public ValueTask<IReadOnlyList<RazerRgb>> RenderAsync(
        TimeSpan elapsed,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var ticks = elapsed.Ticks % _durationTicks;
        if (ticks < 0)
        {
            ticks += _durationTicks;
        }

        var index = 0;
        while (index < _frameEnds.Length - 1 && ticks >= _frameEnds[index])
        {
            index++;
        }

        return ValueTask.FromResult(_frames[index].Colors);
    }
}
