using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace OpenSynapse.Core.Devices;

/// <summary>One editable tier of the fan curve: a temperature and the two fan targets.</summary>
public sealed class BladeFanCurvePointViewModel : INotifyPropertyChanged
{
    private readonly bool _isGpu;
    private int _temperatureCelsius;
    private int _cpuRpm;
    private int _gpuRpm;

    public BladeFanCurvePointViewModel(
        BladeFanCurvePoint point,
        bool isGpu = false,
        int minimumTemperatureCelsius = MinimumTemperatureCelsius,
        int minimumFanSpeedRpm = BladeFanCurve.MinimumPointFanSpeedRpm,
        int maximumFanSpeedRpm = BladeFanCurve.MaximumPointFanSpeedRpm)
    {
        _isGpu = isGpu;
        MinimumEditableTemperatureCelsius = minimumTemperatureCelsius;
        MinimumFanSpeedRpm = minimumFanSpeedRpm;
        MaximumFanSpeedRpm = maximumFanSpeedRpm;
        _temperatureCelsius = Math.Clamp(point.TemperatureCelsius, minimumTemperatureCelsius, MaximumTemperatureCelsius);
        _cpuRpm = Math.Clamp(point.CpuFanSpeedRpm, minimumFanSpeedRpm, maximumFanSpeedRpm);
        _gpuRpm = Math.Clamp(point.GpuFanSpeedRpm, minimumFanSpeedRpm, maximumFanSpeedRpm);
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public int MinimumEditableTemperatureCelsius { get; }
    public int MinimumFanSpeedRpm { get; }
    public int MaximumFanSpeedRpm { get; }

    public int TemperatureCelsius
    {
        get => _temperatureCelsius;
        set => Set(ref _temperatureCelsius, Clamp(value, MinimumEditableTemperatureCelsius, MaximumTemperatureCelsius));
    }

    public int CpuRpm
    {
        get => _cpuRpm;
        set => Set(ref _cpuRpm, Clamp(value, MinimumFanSpeedRpm, MaximumFanSpeedRpm));
    }

    public int GpuRpm
    {
        get => _gpuRpm;
        set => Set(ref _gpuRpm, Clamp(value, MinimumFanSpeedRpm, MaximumFanSpeedRpm));
    }

    /// <summary>The speed column this point owns in the selected CPU or GPU curve.</summary>
    public int Rpm
    {
        get => _isGpu ? _gpuRpm : _cpuRpm;
        set
        {
            if (_isGpu)
            {
                GpuRpm = value;
            }
            else
            {
                CpuRpm = value;
            }
        }
    }

    // NumberBox is double-typed and can briefly report NaN while the field is empty or
    // holds text, so the editable surface stays double and clamps on write.
    public double TemperatureInput
    {
        get => _temperatureCelsius;
        set => TemperatureCelsius = (int)Math.Round(Math.Clamp(double.IsFinite(value) ? value : _temperatureCelsius,
            MinimumEditableTemperatureCelsius, MaximumTemperatureCelsius));
    }

    public double CpuRpmInput
    {
        get => _cpuRpm;
        set => CpuRpm = ToRpm(value, _cpuRpm);
    }

    public double GpuRpmInput
    {
        get => _gpuRpm;
        set => GpuRpm = ToRpm(value, _gpuRpm);
    }

    public double RpmInput
    {
        get => Rpm;
        set => Rpm = ToRpm(value, Rpm);
    }

    public string TemperatureText => $"{_temperatureCelsius} °C";
    public string CpuText => $"{_cpuRpm} RPM";
    public string GpuText => $"{_gpuRpm} RPM";

    public const int MinimumTemperatureCelsius = 35;
    public const int MaximumTemperatureCelsius = 120;

    internal BladeFanCurvePoint ToPoint() => new(_temperatureCelsius, _cpuRpm, _gpuRpm);

    internal void NotifySpeedChanged()
    {
        OnPropertyChanged(nameof(CpuRpm));
        OnPropertyChanged(nameof(GpuRpm));
        OnPropertyChanged(nameof(CpuText));
        OnPropertyChanged(nameof(GpuText));
    }

    private static int Clamp(int value, int minimum, int maximum) =>
        Math.Clamp(value, minimum, maximum);

    private int ToRpm(double value, int previous) => (int)Math.Round(Math.Clamp(
        double.IsFinite(value) ? value : previous,
        MinimumFanSpeedRpm, MaximumFanSpeedRpm));

    private void Set(ref int field, int value, [CallerMemberName] string? propertyName = null)
    {
        if (field == value)
        {
            return;
        }

        field = value;
        OnPropertyChanged(propertyName);
        switch (propertyName)
        {
            case nameof(TemperatureCelsius):
                OnPropertyChanged(nameof(TemperatureText));
                break;
            case nameof(CpuRpm):
                OnPropertyChanged(nameof(CpuText));
                OnPropertyChanged(nameof(Rpm));
                OnPropertyChanged(nameof(RpmInput));
                break;
            case nameof(GpuRpm):
                OnPropertyChanged(nameof(GpuText));
                OnPropertyChanged(nameof(Rpm));
                OnPropertyChanged(nameof(RpmInput));
                break;
        }
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
