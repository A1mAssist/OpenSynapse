namespace OpenSynapse.Core.Sensors;

/// <summary>
/// Accumulates the lowest and highest readings seen during the current session.
/// A sample the monitor could not read is ignored rather than remembered as a
/// reading, and GPU peaks are kept per adapter so a hybrid laptop switching
/// between its integrated and discrete GPU does not erase them.
/// </summary>
public sealed class TelemetryPeakTracker
{
    private readonly Dictionary<string, RangePeaks> _gpuPeaks = new(StringComparer.Ordinal);
    private RangePeaks _activeGpu = new();
    private string _activeGpuName = string.Empty;
    private readonly RangePeaks _cpu = new();

    public double? CpuTemperatureLowCelsius => _cpu.TemperatureLow;

    public double? CpuTemperaturePeakCelsius => _cpu.TemperaturePeak;

    public double? CpuPowerLowWatts => _cpu.PowerLow;

    public double? CpuPowerPeakWatts => _cpu.PowerPeak;

    public int? CpuClockLowMegahertz => _cpu.ClockLow;

    public int? CpuClockPeakMegahertz => _cpu.ClockPeak;

    public double? GpuTemperatureLowCelsius => _activeGpu.TemperatureLow;

    public double? GpuTemperaturePeakCelsius => _activeGpu.TemperaturePeak;

    public double? GpuPowerLowWatts => _activeGpu.PowerLow;

    public double? GpuPowerPeakWatts => _activeGpu.PowerPeak;

    public int? GpuClockLowMegahertz => _activeGpu.ClockLow;

    public int? GpuClockPeakMegahertz => _activeGpu.ClockPeak;

    public void Apply(PerformanceSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        _cpu.TemperatureLow = Lower(_cpu.TemperatureLow, snapshot.CpuTemperatureCelsius);
        _cpu.TemperaturePeak = Higher(_cpu.TemperaturePeak, snapshot.CpuTemperatureCelsius);
        _cpu.PowerLow = Lower(_cpu.PowerLow, snapshot.CpuPowerWatts);
        _cpu.PowerPeak = Higher(_cpu.PowerPeak, snapshot.CpuPowerWatts);
        _cpu.ClockLow = Lower(_cpu.ClockLow, snapshot.CpuClockMegahertz);
        _cpu.ClockPeak = Higher(_cpu.ClockPeak, snapshot.CpuClockMegahertz);

        if (!string.IsNullOrWhiteSpace(snapshot.GpuName) &&
            !StringComparer.Ordinal.Equals(_activeGpuName, snapshot.GpuName))
        {
            _activeGpuName = snapshot.GpuName;
            _activeGpu = _gpuPeaks.GetValueOrDefault(snapshot.GpuName) ?? new RangePeaks();
            _gpuPeaks[snapshot.GpuName] = _activeGpu;
        }

        _activeGpu.TemperatureLow = Lower(_activeGpu.TemperatureLow, snapshot.GpuTemperatureCelsius);
        _activeGpu.TemperaturePeak = Higher(_activeGpu.TemperaturePeak, snapshot.GpuTemperatureCelsius);
        _activeGpu.PowerLow = Lower(_activeGpu.PowerLow, snapshot.GpuPowerWatts);
        _activeGpu.PowerPeak = Higher(_activeGpu.PowerPeak, snapshot.GpuPowerWatts);
        _activeGpu.ClockLow = Lower(_activeGpu.ClockLow, snapshot.GpuClockMegahertz);
        _activeGpu.ClockPeak = Higher(_activeGpu.ClockPeak, snapshot.GpuClockMegahertz);
    }

    private static double? Higher(double? peak, double? sample) =>
        sample is null ? peak : peak is null ? sample : Math.Max(peak.Value, sample.Value);

    private static double? Lower(double? low, double? sample) =>
        sample is null ? low : low is null ? sample : Math.Min(low.Value, sample.Value);

    private static int? Higher(int? peak, int? sample) =>
        // A reported 0 MHz means the core is parked and the reading is not meaningful.
        sample is not > 0 ? peak : peak is null ? sample : Math.Max(peak.Value, sample.Value);

    private static int? Lower(int? low, int? sample) =>
        sample is not > 0 ? low : low is null ? sample : Math.Min(low.Value, sample.Value);

    private sealed class RangePeaks
    {
        public double? TemperatureLow { get; set; }

        public double? TemperaturePeak { get; set; }

        public double? PowerLow { get; set; }

        public double? PowerPeak { get; set; }

        public int? ClockLow { get; set; }

        public int? ClockPeak { get; set; }
    }
}
