using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using OpenSynapse.Core.Devices;

namespace OpenSynapse.App.ViewModels;

/// <summary>One editable tier of the fan curve: a temperature and the two fan targets.</summary>
public sealed class BladeFanCurvePointViewModel : INotifyPropertyChanged
{
    private readonly bool _isGpu;
    private int _temperatureCelsius;
    private int _cpuRpm;
    private int _gpuRpm;

    public BladeFanCurvePointViewModel(BladeFanCurvePoint point, bool isGpu = false)
    {
        _isGpu = isGpu;
        _temperatureCelsius = point.TemperatureCelsius;
        _cpuRpm = point.CpuFanSpeedRpm;
        _gpuRpm = point.GpuFanSpeedRpm;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public int TemperatureCelsius
    {
        get => _temperatureCelsius;
        set => Set(ref _temperatureCelsius, Clamp(value, MinimumTemperatureCelsius, MaximumTemperatureCelsius));
    }

    public int CpuRpm
    {
        get => _cpuRpm;
        set => Set(ref _cpuRpm, Clamp(value, BladeFanCurve.MinimumPointFanSpeedRpm, BladeFanCurve.MaximumPointFanSpeedRpm));
    }

    public int GpuRpm
    {
        get => _gpuRpm;
        set => Set(ref _gpuRpm, Clamp(value, BladeFanCurve.MinimumPointFanSpeedRpm, BladeFanCurve.MaximumPointFanSpeedRpm));
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
        set => TemperatureCelsius = (int)Math.Round(double.IsFinite(value) ? value : _temperatureCelsius);
    }

    public double CpuRpmInput
    {
        get => _cpuRpm;
        set => CpuRpm = (int)Math.Round(double.IsFinite(value) ? value : _cpuRpm);
    }

    public double GpuRpmInput
    {
        get => _gpuRpm;
        set => GpuRpm = (int)Math.Round(double.IsFinite(value) ? value : _gpuRpm);
    }

    public double RpmInput
    {
        get => Rpm;
        set => Rpm = (int)Math.Round(double.IsFinite(value) ? value : Rpm);
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
