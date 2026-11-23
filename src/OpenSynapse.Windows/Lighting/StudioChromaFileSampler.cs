using OpenSynapse.Core.Lighting;
using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.Windows.Lighting;

/// <summary>
/// Plays raw .chroma keyboard frames on a studio matrix. Frames are sampled by
/// LED centre so a 6x22 file also renders on an 8x24 or vendor-specific grid.
/// </summary>
public sealed class StudioChromaFileSampler : IStudioChromaSampler
{
    private readonly IReadOnlyList<ChromaFileFrame> _frames;
    private readonly double[] _ends;
    private readonly int _sourceRows;
    private readonly int _sourceColumns;

    public StudioChromaFileSampler(ChromaFileAnimation animation)
    {
        ArgumentNullException.ThrowIfNull(animation);
        _frames = animation.SourceFrames;
        _sourceRows = animation.SourceRows;
        _sourceColumns = animation.SourceColumns;
        _ends = new double[_frames.Count];
        var total = 0d;
        for (var index = 0; index < _frames.Count; index++)
        {
            total += _frames[index].Duration.TotalSeconds;
            _ends[index] = total;
        }

        DurationSeconds = total;
    }

    public double DurationSeconds { get; }

    public StudioRgb[] Sample(int rows, int columns, double seconds)
    {
        if (rows <= 0) throw new ArgumentOutOfRangeException(nameof(rows));
        if (columns <= 0) throw new ArgumentOutOfRangeException(nameof(columns));

        var colors = FrameAt(seconds).Colors;
        var frame = new StudioRgb[rows * columns];
        for (var row = 0; row < rows; row++)
        {
            var sourceRow = Math.Min(_sourceRows - 1, (int)((row + 0.5) / rows * _sourceRows));
            for (var column = 0; column < columns; column++)
            {
                var sourceColumn = Math.Min(
                    _sourceColumns - 1,
                    (int)((column + 0.5) / columns * _sourceColumns));
                var color = colors[(sourceRow * _sourceColumns) + sourceColumn];
                frame[(row * columns) + column] = new StudioRgb(color.Red, color.Green, color.Blue);
            }
        }

        return frame;
    }

    private ChromaFileFrame FrameAt(double seconds)
    {
        if (_frames.Count == 1 || DurationSeconds <= 0) return _frames[0];

        var position = seconds % DurationSeconds;
        if (position < 0) position += DurationSeconds;
        var index = Array.BinarySearch(_ends, position);
        if (index >= 0) index++;
        if (index < 0) index = ~index;
        return _frames[Math.Min(index, _frames.Count - 1)];
    }
}
