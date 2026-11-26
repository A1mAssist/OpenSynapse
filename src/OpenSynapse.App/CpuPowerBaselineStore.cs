using System.Globalization;
using OpenSynapse.Core.Devices;
using OpenSynapse.Core.Storage;
using OpenSynapse.Windows.Devices;

namespace OpenSynapse.App;

/// <summary>
/// The only trusted factory wall. A report's Firmware* fields are whatever the register held
/// when the process opened, so they cannot undo a limit this process already applied.
/// </summary>
internal sealed class CpuPowerBaselineStore
{
    private readonly string _filePath;
    private const string FileVersion = "v2";

    public static CpuPowerBaselineStore Instance { get; } = new(AppDataPaths.Combine("cpu-power-baseline.txt"));
    private static CpuPowerBaselineStore HyperBoost { get; } = new(AppDataPaths.Combine("cpu-power-baseline-hyperboost.txt"));

    public static CpuPowerBaselineStore ForMode(BladePerformanceMode? mode) =>
        mode == BladePerformanceMode.Hyperboost ? HyperBoost : Instance;

    public CpuPowerLimit? Current { get; private set; }

    internal CpuPowerBaselineStore(string filePath)
    {
        _filePath = filePath;
        Current = Read();
    }

    // Capture HyperBoost's own wall before its first override, never restore another mode's wall.
    internal static async Task CaptureHyperBoostAsync(BladePerformanceMode? mode, CancellationToken cancellationToken)
    {
        if (mode != BladePerformanceMode.Hyperboost || HyperBoost.Current is not null) return;
        var report = await CpuPowerLimitBroker.ReadAsync(cancellationToken);
        HyperBoost.Capture(report, allowCapture: true);
        if (HyperBoost.Current is null)
            throw new InvalidOperationException(report.Detail ?? "The HyperBoost CPU power baseline could not be read.");
    }

    /// <summary>Records the first trusted wall for this store and never overwrites it (INV-07).</summary>
    public void Capture(CpuPowerLimitReport report, bool allowCapture)
    {
        if (!allowCapture ||
            Current is not null ||
            report.Availability != CpuPowerLimitAvailability.Available ||
            !IsPlausible(report.Pl1Watts, report.Pl2Watts))
        {
            return;
        }

        var baseline = new CpuPowerLimit(
            report.Pl1Watts,
            report.Pl2Watts,
            CpuPowerLimit.MinimumTimeSeconds,
            CpuPowerLimit.MaximumTimeSeconds);
        Current = baseline;

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);
            File.WriteAllText(
                _filePath,
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"{FileVersion} {baseline.Pl1Watts} {baseline.Pl2Watts}"));
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or
                System.Security.SecurityException or NotSupportedException)
        {
            // The in-memory baseline still restores this session; a later launch retries.
        }
    }

    private CpuPowerLimit? Read()
    {
        try
        {
            if (!File.Exists(_filePath))
            {
                return null;
            }

            var parts = File.ReadAllText(_filePath)
                .Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 3 ||
                !StringComparer.Ordinal.Equals(parts[0], FileVersion) ||
                !double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var pl1) ||
                !double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var pl2) ||
                !IsPlausible(pl1, pl2))
            {
                return null;
            }

            return new CpuPowerLimit(
                pl1,
                pl2,
                CpuPowerLimit.MinimumTimeSeconds,
                CpuPowerLimit.MaximumTimeSeconds);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or
                System.Security.SecurityException or NotSupportedException)
        {
            return null;
        }
    }

    // 0.06 W was the old Ryzen decode bug; it must not become an 8 W Auto restore.
    private static bool IsPlausible(double pl1, double pl2) =>
        double.IsFinite(pl1) &&
        double.IsFinite(pl2) &&
        pl1 >= CpuPowerLimit.MinimumWatts &&
        pl2 >= CpuPowerLimit.MinimumWatts &&
        pl1 <= CpuPowerLimit.MaximumRestoreWatts &&
        pl2 <= CpuPowerLimit.MaximumRestoreWatts;
}
