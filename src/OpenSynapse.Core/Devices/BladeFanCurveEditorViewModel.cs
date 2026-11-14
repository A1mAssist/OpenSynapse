using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace OpenSynapse.Core.Devices;

public enum BladeFanCurveSelection
{
    Cpu,
    Gpu,
}

/// <summary>
/// Editing surface for the Blade fan curve. Holds the tiers, keeps temperatures ordered,
/// and produces a validated <see cref="BladeFanCurve"/> for apply.
/// </summary>
public sealed class BladeFanCurveEditorViewModel : INotifyPropertyChanged
{
    private const int MaximumPoints = 8;
    private BladeFanCurveTemperatureMode _temperatureMode = BladeFanCurveTemperatureMode.Both;
    private BladeFanCurveSelection _selectedCurve = BladeFanCurveSelection.Cpu;
    private int _selectedIndex = -1;
    private readonly int _minimumCpuTemperature;
    private readonly int _minimumGpuTemperature;
    private readonly int _minimumFanSpeed;

    public BladeFanCurveEditorViewModel(
        BladeFanCurve? curve = null,
        int minimumFanSpeedRpm = BladeFanCurve.MinimumPointFanSpeedRpm,
        int maximumFanSpeedRpm = BladeFanCurve.MaximumPointFanSpeedRpm)
    {
        MinimumFanSpeedRpm = minimumFanSpeedRpm;
        MaximumFanSpeedRpm = maximumFanSpeedRpm;
        var source = curve ?? BladeFanCurve.CreateDefault();
        _temperatureMode = source.TemperatureMode;
        _minimumCpuTemperature = source.MinimumCpuTemperatureCelsius;
        _minimumGpuTemperature = source.MinimumGpuTemperatureCelsius;
        _minimumFanSpeed = source.MinimumFanSpeedRpm;
        foreach (var point in source.CpuPoints)
        {
            CpuPoints.Add(new BladeFanCurvePointViewModel(
                point,
                minimumTemperatureCelsius: _minimumCpuTemperature + 1,
                minimumFanSpeedRpm: MinimumFanSpeedRpm,
                maximumFanSpeedRpm: MaximumFanSpeedRpm));
        }

        foreach (var point in source.GpuPoints)
        {
            GpuPoints.Add(new BladeFanCurvePointViewModel(
                point,
                isGpu: true,
                minimumTemperatureCelsius: _minimumGpuTemperature + 1,
                minimumFanSpeedRpm: MinimumFanSpeedRpm,
                maximumFanSpeedRpm: MaximumFanSpeedRpm));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public int MinimumFanSpeedRpm { get; }
    public int MaximumFanSpeedRpm { get; }

    public ObservableCollection<BladeFanCurvePointViewModel> CpuPoints { get; } = [];

    public ObservableCollection<BladeFanCurvePointViewModel> GpuPoints { get; } = [];

    /// <summary>The collection the table, add/remove and plot selection act on.</summary>
    public ObservableCollection<BladeFanCurvePointViewModel> Points =>
        _selectedCurve == BladeFanCurveSelection.Cpu ? CpuPoints : GpuPoints;

    public BladeFanCurveSelection SelectedCurve
    {
        get => _selectedCurve;
        set
        {
            if (!Enum.IsDefined(value) || _selectedCurve == value)
            {
                return;
            }

            _selectedCurve = value;
            _selectedIndex = -1;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsCpuCurveSelected));
            OnPropertyChanged(nameof(IsGpuCurveSelected));
            OnPropertyChanged(nameof(Points));
            OnPropertyChanged(nameof(SelectedIndex));
            OnPropertyChanged(nameof(SelectedPoint));
            OnPropertyChanged(nameof(CanAddPoint));
        }
    }

    public bool IsCpuCurveSelected
    {
        get => _selectedCurve == BladeFanCurveSelection.Cpu;
        set
        {
            if (value)
            {
                SelectedCurve = BladeFanCurveSelection.Cpu;
            }
        }
    }

    public bool IsGpuCurveSelected
    {
        get => _selectedCurve == BladeFanCurveSelection.Gpu;
        set
        {
            if (value)
            {
                SelectedCurve = BladeFanCurveSelection.Gpu;
            }
        }
    }

    public void SelectCurve(BladeFanCurveSelection curve) => SelectedCurve = curve;

    public BladeFanCurveTemperatureMode TemperatureMode
    {
        get => _temperatureMode;
        set
        {
            if (!Enum.IsDefined(value) || _temperatureMode == value)
            {
                return;
            }

            _temperatureMode = value;
            OnPropertyChanged();
        }
    }

    public int SelectedIndex
    {
        get => _selectedIndex;
        set => Set(ref _selectedIndex, value);
    }

    public BladeFanCurvePointViewModel? SelectedPoint =>
        _selectedIndex >= 0 && _selectedIndex < Points.Count ? Points[_selectedIndex] : null;

    private int SelectedMinimumTemperature =>
        (_selectedCurve == BladeFanCurveSelection.Cpu ? _minimumCpuTemperature : _minimumGpuTemperature) + 1;

    public bool CanAddPoint => Points.Count < Math.Min(MaximumPoints,
        BladeFanCurvePointViewModel.MaximumTemperatureCelsius - SelectedMinimumTemperature + 1);

    public void AddPoint()
    {
        if (!CanAddPoint)
        {
            return;
        }

        var points = Points;
        var temperature = points.Count == 0
            ? BladeFanCurvePointViewModel.MinimumTemperatureCelsius + 10
            : Math.Min(points[^1].TemperatureCelsius + 5, BladeFanCurvePointViewModel.MaximumTemperatureCelsius);
        points.Add(new BladeFanCurvePointViewModel(new BladeFanCurvePoint(
            temperature,
            points.Count == 0 ? BladeFanCurve.DefaultMinimumFanSpeedRpm : points[^1].CpuRpm,
            points.Count == 0 ? BladeFanCurve.DefaultMinimumFanSpeedRpm : points[^1].GpuRpm),
            isGpu: _selectedCurve == BladeFanCurveSelection.Gpu,
            minimumTemperatureCelsius: (_selectedCurve == BladeFanCurveSelection.Cpu ? _minimumCpuTemperature : _minimumGpuTemperature) + 1,
            minimumFanSpeedRpm: MinimumFanSpeedRpm,
            maximumFanSpeedRpm: MaximumFanSpeedRpm));
        EnforceStrictlyIncreasing();
        SelectedIndex = points.Count - 1;
        OnPropertyChanged(nameof(CanAddPoint));
    }

    /// <summary>Selects a tier so the plot highlights it, used by the table's delete button.</summary>
    public void Select(BladeFanCurvePointViewModel point)
    {
        var cpuIndex = CpuPoints.IndexOf(point);
        if (cpuIndex >= 0)
        {
            SelectedCurve = BladeFanCurveSelection.Cpu;
            SelectedIndex = cpuIndex;
            return;
        }

        var gpuIndex = GpuPoints.IndexOf(point);
        if (gpuIndex >= 0)
        {
            SelectedCurve = BladeFanCurveSelection.Gpu;
            SelectedIndex = gpuIndex;
        }
    }

    public void RemoveSelected()
    {
        if (SelectedPoint is not { } point || Points.Count <= 1)
        {
            return;
        }

        var index = Points.IndexOf(point);
        Points.Remove(point);
        Sort();
        SelectedIndex = Math.Clamp(index, 0, Points.Count - 1);
        OnPropertyChanged(nameof(CanAddPoint));
    }

    public void ResetToDefault()
    {
        var source = BladeFanCurve.CreateDefault();
        var points = Points;
        var sourcePoints = _selectedCurve == BladeFanCurveSelection.Cpu
            ? source.CpuPoints
            : source.GpuPoints;
        points.Clear();
        foreach (var point in sourcePoints.Take(Math.Min(MaximumPoints,
            BladeFanCurvePointViewModel.MaximumTemperatureCelsius - SelectedMinimumTemperature + 1)))
        {
            points.Add(new BladeFanCurvePointViewModel(
                point,
                isGpu: _selectedCurve == BladeFanCurveSelection.Gpu,
                minimumTemperatureCelsius: (_selectedCurve == BladeFanCurveSelection.Cpu ? _minimumCpuTemperature : _minimumGpuTemperature) + 1,
                minimumFanSpeedRpm: MinimumFanSpeedRpm,
                maximumFanSpeedRpm: MaximumFanSpeedRpm));
        }

        EnforceStrictlyIncreasing();
        SelectedIndex = -1;
        OnPropertyChanged(nameof(CanAddPoint));
    }

    /// <summary>Keeps tiers ordered so the model accepts them.</summary>
    public void Sort() => Sort(Points);

    private static void Sort(ObservableCollection<BladeFanCurvePointViewModel> points)
    {
        var ordered = points.OrderBy(point => point.TemperatureCelsius).ToArray();
        for (var index = 0; index < ordered.Length; index++)
        {
            var currentIndex = points.IndexOf(ordered[index]);
            if (currentIndex != index)
            {
                points.Move(currentIndex, index);
            }
        }
    }

    /// <summary>Enforces a strictly increasing temperature without rejecting the edit.</summary>
    public void EnforceStrictlyIncreasing() => EnforceStrictlyIncreasing(Points);

    private static void EnforceStrictlyIncreasing(
        ObservableCollection<BladeFanCurvePointViewModel> points)
    {
        Sort(points);
        for (var index = 0; index < points.Count; index++)
        {
            var minimum = Math.Max(points[index].MinimumEditableTemperatureCelsius,
                index == 0 ? 0 : points[index - 1].TemperatureCelsius + 1);
            var maximum = BladeFanCurvePointViewModel.MaximumTemperatureCelsius - (points.Count - index - 1);
            if (minimum > maximum)
                throw new ArgumentException("There is not enough temperature range for all curve points.");
            points[index].TemperatureCelsius = Math.Clamp(points[index].TemperatureCelsius, minimum, maximum);
        }
    }

    public BladeFanCurve BuildCurve()
    {
        EnforceStrictlyIncreasing(CpuPoints);
        EnforceStrictlyIncreasing(GpuPoints);
        return new BladeFanCurve(
            _temperatureMode,
            CpuPoints.Select(point => point.ToPoint()).ToArray(),
            GpuPoints.Select(point => point.ToPoint()).ToArray(),
            _minimumCpuTemperature, _minimumGpuTemperature, _minimumFanSpeed);
    }

    private void Set(ref int field, int value, [CallerMemberName] string? propertyName = null)
    {
        if (field == value)
        {
            return;
        }

        field = value;
        OnPropertyChanged(propertyName);
        OnPropertyChanged(nameof(SelectedPoint));
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
