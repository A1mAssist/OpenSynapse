namespace OpenSynapse.Core.Lighting;

/// <summary>One frame's copy of the key heat map. Cells are normalized 0..1.</summary>
public sealed class StudioKeyState
{
    public static StudioKeyState Empty { get; } = new(
        0, 0, [], -1, -1, double.PositiveInfinity, []);

    public StudioKeyState(
        int rows,
        int columns,
        float[] cells,
        int lastRow,
        int lastColumn,
        double secondsSincePress,
        IReadOnlyList<StudioKeyPress>? presses = null)
    {
        Rows = rows;
        Columns = columns;
        Cells = cells;
        LastRow = lastRow;
        LastColumn = lastColumn;
        SecondsSincePress = secondsSincePress;
        Presses = presses ?? Array.Empty<StudioKeyPress>();
    }

    public int Rows { get; }

    public int Columns { get; }

    public float[] Cells { get; }

    public int LastRow { get; }

    public int LastColumn { get; }

    public double SecondsSincePress { get; }

    /// <summary>Recent presses, newest first, with age and release state.</summary>
    public IReadOnlyList<StudioKeyPress> Presses { get; }

    public bool HasPress => LastRow >= 0;

    public float this[int row, int column] =>
        (uint)row < (uint)Rows && (uint)column < (uint)Columns
            ? Cells[row * Columns + column]
            : 0;
}

/// <summary>One key press in matrix coordinates.</summary>
public readonly record struct StudioKeyPress(
    int Row,
    int Column,
    double Age,
    bool Held,
    double Hue);

/// <summary>
/// Per-device key heat. Press sets a cell to 1; Decay fades every cell with a
/// 450 ms half-life. Snapshot is taken once per rendered frame.
/// </summary>
public sealed class StudioKeyHeat
{
    private const double HalfLifeSeconds = 0.45;
    private const int MaxPresses = 32;
    private readonly object _gate = new();
    private readonly float[] _cells;
    private readonly List<Entry> _presses = new();
    private int _lastRow = -1;
    private int _lastColumn = -1;
    private double _secondsSincePress = double.PositiveInfinity;
    private double _clock;
    private int _pressCount;

    public StudioKeyHeat(int rows, int columns)
    {
        if (rows < 0) throw new ArgumentOutOfRangeException(nameof(rows));
        if (columns < 0) throw new ArgumentOutOfRangeException(nameof(columns));
        Rows = rows;
        Columns = columns;
        _cells = new float[checked(rows * columns)];
    }

    public int Rows { get; }

    public int Columns { get; }

    public void Press(int row, int column)
    {
        if ((uint)row >= (uint)Rows || (uint)column >= (uint)Columns)
        {
            return;
        }

        lock (_gate)
        {
            _cells[row * Columns + column] = 1;
            _lastRow = row;
            _lastColumn = column;
            _secondsSincePress = 0;
            _presses.Insert(0, new Entry
            {
                Row = row,
                Column = column,
                Time = _clock,
                Hue = (_pressCount++ * 0.6180339887498949) % 1.0,
            });
            if (_presses.Count > MaxPresses) _presses.RemoveRange(MaxPresses, _presses.Count - MaxPresses);
        }
    }

    public void Release(int row, int column)
    {
        lock (_gate)
        {
            foreach (var entry in _presses)
            {
                if (entry.Row == row && entry.Column == column && double.IsNaN(entry.Release))
                {
                    entry.Release = _clock;
                    return;
                }
            }
        }
    }

    public void Decay(double seconds)
    {
        if (!(seconds > 0))
        {
            return;
        }

        lock (_gate)
        {
            var factor = (float)Math.Exp(-Math.Log(2) * seconds / HalfLifeSeconds);
            _clock += seconds;
            for (var index = 0; index < _cells.Length; index++)
            {
                var value = _cells[index] * factor;
                _cells[index] = value < 0.001f ? 0 : value;
            }

            _secondsSincePress = Math.Min(_secondsSincePress + seconds, 60);
            _presses.RemoveAll(entry => !double.IsNaN(entry.Release) && _clock - entry.Time > 4);
        }
    }

    public void Clear()
    {
        lock (_gate)
        {
            Array.Clear(_cells);
            _lastRow = -1;
            _lastColumn = -1;
            _secondsSincePress = double.PositiveInfinity;
            _presses.Clear();
            _clock = 0;
        }
    }

    public StudioKeyState Snapshot()
    {
        lock (_gate)
        {
            var presses = new StudioKeyPress[_presses.Count];
            for (var index = 0; index < _presses.Count; index++)
            {
                var entry = _presses[index];
                presses[index] = new StudioKeyPress(
                    entry.Row,
                    entry.Column,
                    _clock - entry.Time,
                    double.IsNaN(entry.Release),
                    entry.Hue);
            }

            return new StudioKeyState(
                Rows,
                Columns,
                (float[])_cells.Clone(),
                _lastRow,
                _lastColumn,
                _secondsSincePress,
                presses);
        }
    }

    private sealed class Entry
    {
        public int Row;
        public int Column;
        public double Time;
        public double Release = double.NaN;
        public double Hue;
    }
}
