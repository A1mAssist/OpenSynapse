namespace OpenSynapse.Windows.Input;

/// <summary>
/// Replays Razer's official Sensor Rotation Tool measurement on Raw Input counts.
/// Every horizontal reversal that is longer than the tool's 200-unit stroke closes a
/// segment; the signed segment angles are averaged. Raw counts already carry the
/// device DPI, so the threshold is used unscaled exactly like the web tool.
/// </summary>
internal sealed class MouseRotationCalibrator
{
    private const int MinimumStrokeCounts = 200;

    private readonly object _gate = new();
    private readonly List<double> _segments = [];
    private int _x;
    private int _y;
    private int _startX;
    private int _startY;
    private int _lastX;
    private int _lastY;
    private int _direction;
    private bool _drawing;

    internal int SegmentCount
    {
        get
        {
            lock (_gate)
            {
                return _segments.Count;
            }
        }
    }

    internal double? AverageAngleDegrees
    {
        get
        {
            lock (_gate)
            {
                if (_segments.Count == 0)
                {
                    return null;
                }

                var sum = 0d;
                foreach (var segment in _segments)
                {
                    sum += segment;
                }

                return sum / _segments.Count * (180d / Math.PI);
            }
        }
    }

    internal void BeginStroke()
    {
        lock (_gate)
        {
            _drawing = true;
            _startX = _x;
            _startY = _y;
            _lastX = _x;
            _lastY = _y;
            _direction = 0;
        }
    }

    internal void EndStroke()
    {
        lock (_gate)
        {
            _drawing = false;
            _direction = 0;
        }
    }

    internal void AddDelta(int deltaX, int deltaY)
    {
        lock (_gate)
        {
            _x += deltaX;
            _y += deltaY;
            if (!_drawing)
            {
                return;
            }

            var direction = Math.Sign(deltaX);
            if (direction == 0)
            {
                _lastX = _x;
                _lastY = _y;
                return;
            }

            if (_direction != 0 && direction != _direction &&
                Math.Abs(_x - _startX) > MinimumStrokeCounts)
            {
                AddSegment(_startX, _startY, _lastX, _lastY);
                _startX = _lastX;
                _startY = _lastY;
            }

            _lastX = _x;
            _lastY = _y;
            _direction = direction;
        }
    }

    private void AddSegment(int startX, int startY, int endX, int endY)
    {
        var deltaX = Math.Abs(endX - startX);
        if (deltaX == 0)
        {
            return;
        }

        var angle = Math.Atan2(Math.Abs(endY - startY), deltaX);
        // Screen Y grows downward: right-down and left-up tilt the sensor the same
        // way, which the official tool reports as a negative angle.
        var negative = (endX > startX) == (endY > startY);
        _segments.Add(negative ? -angle : angle);
    }
}
