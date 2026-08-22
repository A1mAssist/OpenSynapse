using System.ComponentModel;
using System.Runtime.CompilerServices;
using OpenSynapse.Core.Sensors;

namespace OpenSynapse.App.ViewModels;

public sealed class SystemTelemetryViewModel : INotifyPropertyChanged
{
    private string _telemetryTimeText = "等待采样";
    private string _cpuName = "CPU";
    private string _cpuValue = "--";
    private double _cpuPercent;
    private string _cpuTemperatureText = "--";
    private string _cpuPowerText = "--";
    private string _cpuClockText = "--";
    private string _gpuName = "GPU";
    private string _gpuValue = "--";
    private double _gpuPercent;
    private string _gpuTemperatureText = "--";
    private string _gpuPowerText = "--";
    private string _gpuClockText = "--";
    private string _gpuMemoryLabel = "GPU 内存";
    private string _gpuMemoryText = "--";
    private string _memoryValue = "--";
    private string _memoryDetail = "-- / -- GB";
    private double _memoryPercent;
    private string _storageValue = "--";
    private string _storageDetail = "-- / -- GB";
    private double _storagePercent;

    public event PropertyChangedEventHandler? PropertyChanged;

    public string TelemetryTimeText { get => AppStrings.Get(_telemetryTimeText); private set => SetField(ref _telemetryTimeText, value); }
    public string CpuName { get => _cpuName; private set => SetField(ref _cpuName, value); }
    public string CpuValue { get => _cpuValue; private set => SetField(ref _cpuValue, value); }
    public double CpuPercent { get => _cpuPercent; private set => SetField(ref _cpuPercent, value); }
    public string CpuTemperatureText { get => _cpuTemperatureText; private set => SetField(ref _cpuTemperatureText, value); }
    public string CpuPowerText { get => _cpuPowerText; private set => SetField(ref _cpuPowerText, value); }
    public string CpuClockText { get => _cpuClockText; private set => SetField(ref _cpuClockText, value); }
    public string GpuName { get => _gpuName; private set => SetField(ref _gpuName, value); }
    public string GpuValue { get => _gpuValue; private set => SetField(ref _gpuValue, value); }
    public double GpuPercent { get => _gpuPercent; private set => SetField(ref _gpuPercent, value); }
    public string GpuTemperatureText { get => _gpuTemperatureText; private set => SetField(ref _gpuTemperatureText, value); }
    public string GpuPowerText { get => _gpuPowerText; private set => SetField(ref _gpuPowerText, value); }
    public string GpuClockText { get => _gpuClockText; private set => SetField(ref _gpuClockText, value); }
    public string GpuMemoryLabel { get => AppStrings.Get(_gpuMemoryLabel); private set => SetField(ref _gpuMemoryLabel, value); }
    public string GpuMemoryText { get => _gpuMemoryText; private set => SetField(ref _gpuMemoryText, value); }
    public string MemoryValue { get => _memoryValue; private set => SetField(ref _memoryValue, value); }
    public string MemoryDetail { get => _memoryDetail; private set => SetField(ref _memoryDetail, value); }
    public double MemoryPercent { get => _memoryPercent; private set => SetField(ref _memoryPercent, value); }
    public string StorageValue { get => _storageValue; private set => SetField(ref _storageValue, value); }
    public string StorageDetail { get => _storageDetail; private set => SetField(ref _storageDetail, value); }
    public double StoragePercent { get => _storagePercent; private set => SetField(ref _storagePercent, value); }

    public void Apply(PerformanceSnapshot snapshot)
    {
        if (!string.IsNullOrWhiteSpace(snapshot.CpuName)) CpuName = snapshot.CpuName;
        CpuValue = FormatPercent(snapshot.CpuUsagePercent);
        CpuPercent = snapshot.CpuUsagePercent ?? 0;
        CpuTemperatureText = FormatNumber(snapshot.CpuTemperatureCelsius, "0", "°C");
        CpuPowerText = FormatNumber(snapshot.CpuPowerWatts, "0.0", " W");
        CpuClockText = FormatNumber(snapshot.CpuClockMegahertz, "0", " MHz");

        if (!string.IsNullOrWhiteSpace(snapshot.GpuName) &&
            !StringComparer.Ordinal.Equals(GpuName, snapshot.GpuName))
        {
            GpuName = snapshot.GpuName;
            GpuTemperatureText = "--";
            GpuPowerText = "--";
            GpuClockText = "--";
            GpuMemoryText = "--";
        }

        GpuValue = FormatPercent(snapshot.GpuUsagePercent);
        GpuPercent = snapshot.GpuUsagePercent ?? 0;
        GpuTemperatureText = FormatNumber(snapshot.GpuTemperatureCelsius, "0", "°C");
        GpuPowerText = FormatNumber(snapshot.GpuPowerWatts, "0.0", " W");
        GpuClockText = FormatNumber(snapshot.GpuClockMegahertz, "0", " MHz");
        GpuMemoryLabel = snapshot.GpuMemoryLabel;
        GpuMemoryText = snapshot.GpuMemoryUsedMebibytes is long used &&
                        snapshot.GpuMemoryTotalMebibytes is long total
            ? $"{used:N0} / {total:N0} MiB"
            : "--";

        if (CalculatePercent(snapshot.MemoryUsedBytes, snapshot.MemoryTotalBytes) is double memoryPercent)
        {
            MemoryPercent = memoryPercent;
            MemoryValue = FormatPercent(memoryPercent);
            MemoryDetail = FormatBytePair(snapshot.MemoryUsedBytes, snapshot.MemoryTotalBytes);
        }
        else
        {
            MemoryPercent = 0;
            MemoryValue = "--";
            MemoryDetail = "-- / -- GB";
        }

        if (CalculatePercent(snapshot.StorageUsedBytes, snapshot.StorageTotalBytes) is double storagePercent)
        {
            StoragePercent = storagePercent;
            StorageValue = FormatPercent(storagePercent);
            StorageDetail = FormatBytePair(snapshot.StorageUsedBytes, snapshot.StorageTotalBytes);
        }
        else
        {
            StoragePercent = 0;
            StorageValue = "--";
            StorageDetail = "-- / -- GB";
        }

        TelemetryTimeText = AppStrings.FormatText("LiveSampleTime", snapshot.CapturedAt.ToLocalTime());
    }

    public void MarkUnavailable() => TelemetryTimeText = AppStrings.Text("PerformanceSamplingUnavailable");

    public void RefreshLocalization() => PropertyChanged?.Invoke(this, new(string.Empty));

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        PropertyChanged?.Invoke(this, new(propertyName));
        return true;
    }

    private static double? CalculatePercent<T>(T? used, T? total) where T : struct, IConvertible
    {
        if (used is null || total is null) return null;
        var totalValue = total.Value.ToDouble(null);
        return totalValue <= 0 ? null : Math.Clamp(used.Value.ToDouble(null) * 100 / totalValue, 0, 100);
    }

    private static string FormatPercent(double? value) => value is null ? "--" : $"{value:0}%";

    private static string FormatNumber<T>(T? value, string format, string suffix) where T : struct, IFormattable =>
        value is null ? "--" : value.Value.ToString(format, null) + suffix;

    private static string FormatBytePair<T>(T? used, T? total) where T : struct, IConvertible
    {
        if (used is null || total is null) return "-- / -- GB";
        var usedGibibytes = used.Value.ToDouble(null) / 1024 / 1024 / 1024;
        var totalGibibytes = total.Value.ToDouble(null) / 1024 / 1024 / 1024;
        return totalGibibytes >= 1024
            ? $"{usedGibibytes / 1024:0.00} / {totalGibibytes / 1024:0.00} TB"
            : $"{usedGibibytes:0.0} / {totalGibibytes:0.0} GB";
    }
}
