namespace OpenSynapse.Core.Sensors;

public sealed record PerformanceSnapshot(
    string CpuName,
    double? CpuUsagePercent,
    double? CpuTemperatureCelsius,
    double? CpuPowerWatts,
    int? CpuClockMegahertz,
    string GpuName,
    double? GpuUsagePercent,
    double? GpuTemperatureCelsius,
    double? GpuPowerWatts,
    int? GpuClockMegahertz,
    long? GpuMemoryUsedMebibytes,
    long? GpuMemoryTotalMebibytes,
    ulong? MemoryUsedBytes,
    ulong? MemoryTotalBytes,
    long? StorageUsedBytes,
    long? StorageTotalBytes,
    DateTimeOffset CapturedAt,
    string? ErrorMessage = null);
