using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using OpenSynapse.Core.Devices;

namespace OpenSynapse.App.ViewModels;

/// <summary>
/// Editing surface for the Blade fan curve. Holds the tiers, keeps temperatures ordered,
/// and produces a validated <see cref="BladeFanCurve"/> for apply.
/// </summary>
public sealed class BladeFanCurveEditorViewModel : INotifyPropertyChanged
{
    private const int MaximumPoints = 8;
    private BladeFanCurveTemperatureMode _temperatureMode = BladeFanCurveTemperatureMode.Both;
    private int _selectedIndex = -1;

    public BladeFanCurveEditorViewModel(BladeFanCurve? curve = null)
    {
        var source = curve ?? BladeFanCurve.CreateDefault();
        _temperatureMode = source.TemperatureMode;
        foreach (var point in source.CpuPoints)
        {
            Points.Add(new BladeFanCurvePointViewModel(point));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<BladeFanCurvePointViewModel> Points { get; } = [];

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

    public bool CanAddPoint => Points.Count < MaximumPoints;

    public void AddPoint()
    {
        if (!CanAddPoint)
        {
            return;
        }

        var temperature = Points.Count == 0
            ? BladeFanCurvePointViewModel.MinimumTemperatureCelsius + 10
            : Math.Min(Points[^1].TemperatureCelsius + 5, BladeFanCurvePointViewModel.MaximumTemperatureCelsius);
        Points.Add(new BladeFanCurvePointViewModel(new BladeFanCurvePoint(
            temperature,
            Points.Count == 0 ? BladeFanCurve.DefaultMinimumFanSpeedRpm : Points[^1].CpuRpm,
            Points.Count == 0 ? BladeFanCurve.DefaultMinimumFanSpeedRpm : Points[^1].GpuRpm)));
        Sort();
        SelectedIndex = Points.Count - 1;
        OnPropertyChanged(nameof(CanAddPoint));
    }

    /// <summary>Selects a tier so the plot highlights it, used by the table's delete button.</summary>
    public void Select(BladeFanCurvePointViewModel point)
    {
        var index = Points.IndexOf(point);
        if (index >= 0)
        {
            SelectedIndex = index;
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
    }

    public void ResetToDefault()
    {
        var source = BladeFanCurve.CreateDefault();
        _temperatureMode = source.TemperatureMode;
        Points.Clear();
        foreach (var point in source.CpuPoints)
        {
            Points.Add(new BladeFanCurvePointViewModel(point));
        }

        OnPropertyChanged(nameof(TemperatureMode));
        OnPropertyChanged(nameof(CanAddPoint));
    }

    /// <summary>Keeps tiers ordered so the model accepts them.</summary>
    public void Sort()
    {
        var ordered = Points.OrderBy(point => point.TemperatureCelsius).ToArray();
        for (var index = 0; index < ordered.Length; index++)
        {
            var currentIndex = Points.IndexOf(ordered[index]);
            if (currentIndex != index)
            {
                Points.Move(currentIndex, index);
            }
        }
    }

    /// <summary>Enforces a strictly increasing temperature without rejecting the edit.</summary>
    public void EnforceStrictlyIncreasing()
    {
        Sort();
        for (var index = 1; index < Points.Count; index++)
        {
            if (Points[index].TemperatureCelsius <= Points[index - 1].TemperatureCelsius)
            {
                Points[index].TemperatureCelsius = Points[index - 1].TemperatureCelsius + 1;
            }
        }
    }

    public BladeFanCurve BuildCurve()
    {
        EnforceStrictlyIncreasing();
        var points = Points.Select(point => point.ToPoint()).ToArray();
        return new BladeFanCurve(_temperatureMode, points, points);
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
