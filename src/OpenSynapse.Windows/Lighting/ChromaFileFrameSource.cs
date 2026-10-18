using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.Windows.Lighting;

/// <summary>
/// Plays a parsed .chroma animation on the software frame runtime.
/// </summary>
public sealed class ChromaFileFrameSource : ISoftwareLightingFrameSource
{
    private readonly ChromaFileAnimation _animation;
    private readonly long[] _frameEnds;
    private readonly long _durationTicks;

    public ChromaFileFrameSource(ChromaFileAnimation animation)
    {
        _animation = animation ?? throw new ArgumentNullException(nameof(animation));
        if (animation.Frames.Count == 0 || animation.Duration <= TimeSpan.Zero)
        {
            throw new ArgumentException("The Chroma animation has no frames.", nameof(animation));
        }

        _durationTicks = animation.Duration.Ticks;
        _frameEnds = new long[animation.Frames.Count];
        var total = 0L;
        for (var index = 0; index < animation.Frames.Count; index++)
        {
            total += animation.Frames[index].Duration.Ticks;
            _frameEnds[index] = total;
        }
    }

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

        return ValueTask.FromResult(_animation.Frames[index].Colors);
    }
}
