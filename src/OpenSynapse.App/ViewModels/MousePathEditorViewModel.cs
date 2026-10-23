using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Microsoft.UI.Xaml;
using OpenSynapse.Core.Macros;

namespace OpenSynapse.App.ViewModels;

public sealed class MousePathPointViewModel : INotifyPropertyChanged
{
    private readonly int _minimumCoordinate;
    private readonly int _maximumCoordinate;
    private readonly Action<MousePathPointViewModel> _changed;
    private int _x;
    private int _y;
    private int _timeMilliseconds;

    internal MousePathPointViewModel(
        MacroMousePoint point,
        int minimumCoordinate,
        int maximumCoordinate,
        Action<MousePathPointViewModel> changed)
    {
        _minimumCoordinate = minimumCoordinate;
        _maximumCoordinate = maximumCoordinate;
        _changed = changed;
        _x = Math.Clamp(point.X, minimumCoordinate, maximumCoordinate);
        _y = Math.Clamp(point.Y, minimumCoordinate, maximumCoordinate);
        _timeMilliseconds = Math.Clamp(point.OffsetMilliseconds, 0, MacroDefinition.MaxDelayMilliseconds);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public int MinimumCoordinate => _minimumCoordinate;

    public int MaximumCoordinate => _maximumCoordinate;

    public int MaximumTime => MacroDefinition.MaxDelayMilliseconds;

    public int X
    {
        get => _x;
        set
        {
            var clamped = Math.Clamp(value, _minimumCoordinate, _maximumCoordinate);
            if (_x == clamped)
            {
                return;
            }

            _x = clamped;
            OnPropertyChanged();
            _changed(this);
        }
    }

    public int Y
    {
        get => _y;
        set
        {
            var clamped = Math.Clamp(value, _minimumCoordinate, _maximumCoordinate);
            if (_y == clamped)
            {
                return;
            }

            _y = clamped;
            OnPropertyChanged();
            _changed(this);
        }
    }

    public int TimeMilliseconds
    {
        get => _timeMilliseconds;
        set
        {
            var clamped = Math.Clamp(value, 0, MacroDefinition.MaxDelayMilliseconds);
            if (_timeMilliseconds == clamped)
            {
                return;
            }

            _timeMilliseconds = clamped;
            OnPropertyChanged();
            _changed(this);
        }
    }

    internal void Move(int x, int y)
    {
        var clampedX = Math.Clamp(x, _minimumCoordinate, _maximumCoordinate);
        var clampedY = Math.Clamp(y, _minimumCoordinate, _maximumCoordinate);
        if (_x == clampedX && _y == clampedY)
        {
            return;
        }

        _x = clampedX;
        _y = clampedY;
        OnPropertyChanged(nameof(X));
        OnPropertyChanged(nameof(Y));
        _changed(this);
    }

    internal MacroMousePoint ToModel() => new()
    {
        X = X,
        Y = Y,
        OffsetMilliseconds = TimeMilliseconds,
    };

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public sealed class MousePathEditorViewModel : INotifyPropertyChanged
{
    public const int MaxPoints = MacroStep.MaxMousePathPoints;

    private readonly int _minimumCoordinate;
    private readonly int _maximumCoordinate;
    private readonly ObservableCollection<MousePathPointViewModel> _points = [];
    private MousePathPointViewModel? _selectedPoint;
    private string? _validationError;
    private double _plotMinX = -100;
    private double _plotMaxX = 100;
    private double _plotMinY = -100;
    private double _plotMaxY = 100;

    public MousePathEditorViewModel(MacroMouseMoveMode mode, IReadOnlyList<MacroMousePoint>? points)
    {
        Mode = mode;
        (_minimumCoordinate, _maximumCoordinate) = mode == MacroMouseMoveMode.ForegroundRelative
            ? (0, 10_000)
            : (-100_000, 100_000);

        _points.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(Points));
            OnPropertyChanged(nameof(PointCountText));
            OnPropertyChanged(nameof(CanAddPoint));
            OnPropertyChanged(nameof(CanRemovePoint));
            ResetPlotBounds();
            Validate();
        };

        foreach (var point in points ?? [])
        {
            _points.Add(CreatePoint(point));
        }

        if (_points.Count == 0)
        {
            _points.Add(CreatePoint(new MacroMousePoint()));
        }

        _selectedPoint = _points[0];
        ResetPlotBounds();
        Validate();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public event EventHandler? PlotChanged;

    public MacroMouseMoveMode Mode { get; }

    public string ModeText => AppStrings.Text(Mode switch
    {
        MacroMouseMoveMode.Absolute => "MacroRecordMouseAbsolute/Content",
        MacroMouseMoveMode.Relative => "MacroRecordMouseRelative/Content",
        MacroMouseMoveMode.RelativeToStart => "MacroRecordMouseStart/Content",
        MacroMouseMoveMode.ForegroundRelative => "MacroRecordMouseForeground/Content",
        _ => "MacroRecordMouseNone/Content",
    });

    public string ModeHint => AppStrings.Text(Mode switch
    {
        MacroMouseMoveMode.Absolute => "MacroMousePathHintAbsolute",
        MacroMouseMoveMode.Relative => "MacroMousePathHintRelative",
        MacroMouseMoveMode.RelativeToStart => "MacroMousePathHintStart",
        MacroMouseMoveMode.ForegroundRelative => "MacroMousePathHintForeground",
        _ => "MacroMousePathHintAbsolute",
    });

    public ObservableCollection<MousePathPointViewModel> Points => _points;

    public MousePathPointViewModel? SelectedPoint
    {
        get => _selectedPoint;
        set
        {
            if (ReferenceEquals(_selectedPoint, value))
            {
                return;
            }

            _selectedPoint = value;
            OnPropertyChanged();
            PlotChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public bool CanAddPoint => _points.Count < MaxPoints;

    public bool CanRemovePoint => _points.Count > 1;

    public bool CanApply => _validationError is null;

    public string ValidationText => _validationError ?? string.Empty;

    public Visibility ValidationVisibility =>
        _validationError is null ? Visibility.Collapsed : Visibility.Visible;

    public string PointCountText =>
        AppStrings.FormatText("MacroMousePathPointCount", _points.Count, MaxPoints);

    public double PlotMinX => _plotMinX;

    public double PlotMaxX => _plotMaxX;

    public double PlotMinY => _plotMinY;

    public double PlotMaxY => _plotMaxY;

    public void AddPoint()
    {
        if (!CanAddPoint)
        {
            return;
        }

        var anchorIndex = SelectedPoint is null ? _points.Count - 1 : _points.IndexOf(SelectedPoint);
        var insertIndex = Math.Clamp(anchorIndex + 1, 0, _points.Count);
        var previous = anchorIndex >= 0 ? _points[anchorIndex] : null;
        var next = insertIndex < _points.Count ? _points[insertIndex] : null;
        var time = previous is null
            ? 0
            : next is null
                ? Math.Min(previous.TimeMilliseconds + 100, MacroDefinition.MaxDelayMilliseconds)
                : previous.TimeMilliseconds + ((next.TimeMilliseconds - previous.TimeMilliseconds) / 2);
        var point = CreatePoint(new MacroMousePoint
        {
            X = previous?.X ?? 0,
            Y = previous?.Y ?? 0,
            OffsetMilliseconds = time,
        });
        _points.Insert(insertIndex, point);
        SelectedPoint = point;
    }

    public void RemoveSelectedPoint()
    {
        if (_points.Count <= 1)
        {
            return;
        }

        var index = SelectedPoint is null ? _points.Count - 1 : _points.IndexOf(SelectedPoint);
        if (index < 0)
        {
            index = _points.Count - 1;
        }

        _points.RemoveAt(index);
        SelectedPoint = _points[Math.Min(index, _points.Count - 1)];
    }

    public void MovePoint(int index, int x, int y)
    {
        if (index < 0 || index >= _points.Count)
        {
            return;
        }

        SelectedPoint = _points[index];
        _points[index].Move(x, y);
    }

    public IReadOnlyList<MacroMousePoint> CreateResult() =>
        _points.Select(point => point.ToModel()).ToList();

    public void RefreshLocalization()
    {
        OnPropertyChanged(nameof(ModeText));
        OnPropertyChanged(nameof(ModeHint));
        OnPropertyChanged(nameof(PointCountText));
        OnPropertyChanged(nameof(ValidationText));
        OnPropertyChanged(nameof(ValidationVisibility));
    }

    public bool Validate()
    {
        string? error = null;
        if (_points.Count == 0)
        {
            error = AppStrings.Text("MacroMousePathNeedPoint");
        }
        else if (_points.Count > MaxPoints)
        {
            error = AppStrings.FormatText("MacroMousePathPointLimit", MaxPoints);
        }
        else
        {
            var previousTime = 0;
            for (var index = 0; index < _points.Count; index++)
            {
                var point = _points[index];
                if (index > 0 && point.TimeMilliseconds < previousTime)
                {
                    error = AppStrings.Text("MacroMousePathTimeOrder");
                    break;
                }

                previousTime = point.TimeMilliseconds;
            }
        }

        if (StringComparer.Ordinal.Equals(error, _validationError))
        {
            return error is null;
        }

        _validationError = error;
        OnPropertyChanged(nameof(ValidationText));
        OnPropertyChanged(nameof(ValidationVisibility));
        OnPropertyChanged(nameof(CanApply));
        return error is null;
    }

    private MousePathPointViewModel CreatePoint(MacroMousePoint point) =>
        new(point, _minimumCoordinate, _maximumCoordinate, PointChanged);

    private void PointChanged(MousePathPointViewModel point)
    {
        EnsureBoundsContain(point.X, point.Y);
        Validate();
        PlotChanged?.Invoke(this, EventArgs.Empty);
    }

    private void ResetPlotBounds()
    {
        if (Mode == MacroMouseMoveMode.ForegroundRelative)
        {
            SetPlotBounds(0, 10_000, 0, 10_000);
            return;
        }

        if (_points.Count == 0)
        {
            SetPlotBounds(-100, 100, -100, 100);
            return;
        }

        var minX = _points.Min(point => (double)point.X);
        var maxX = _points.Max(point => (double)point.X);
        var minY = _points.Min(point => (double)point.Y);
        var maxY = _points.Max(point => (double)point.Y);
        var spanX = Math.Max(1, maxX - minX);
        var spanY = Math.Max(1, maxY - minY);
        var padX = Math.Max(20, spanX * 0.08);
        var padY = Math.Max(20, spanY * 0.08);
        SetPlotBounds(minX - padX, maxX + padX, minY - padY, maxY + padY);
    }

    private void EnsureBoundsContain(int x, int y)
    {
        if (Mode == MacroMouseMoveMode.ForegroundRelative)
        {
            return;
        }

        if (x >= _plotMinX && x <= _plotMaxX && y >= _plotMinY && y <= _plotMaxY)
        {
            return;
        }

        var spanX = Math.Max(1, _plotMaxX - _plotMinX);
        var spanY = Math.Max(1, _plotMaxY - _plotMinY);
        SetPlotBounds(
            Math.Min(_plotMinX, x - (spanX * 0.08)),
            Math.Max(_plotMaxX, x + (spanX * 0.08)),
            Math.Min(_plotMinY, y - (spanY * 0.08)),
            Math.Max(_plotMaxY, y + (spanY * 0.08)));
    }

    private void SetPlotBounds(double minX, double maxX, double minY, double maxY)
    {
        if (Math.Abs(_plotMinX - minX) < 0.01 &&
            Math.Abs(_plotMaxX - maxX) < 0.01 &&
            Math.Abs(_plotMinY - minY) < 0.01 &&
            Math.Abs(_plotMaxY - maxY) < 0.01)
        {
            return;
        }

        _plotMinX = minX;
        _plotMaxX = maxX;
        _plotMinY = minY;
        _plotMaxY = maxY;
        OnPropertyChanged(nameof(PlotMinX));
        OnPropertyChanged(nameof(PlotMaxX));
        OnPropertyChanged(nameof(PlotMinY));
        OnPropertyChanged(nameof(PlotMaxY));
        PlotChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
