using System.Globalization;
using OpenSynapse.Core.Devices;
using OpenSynapse.Core.Storage;

namespace OpenSynapse.App;

/// <summary>
/// The only trusted factory wall. A report's Firmware* fields are whatever the register held
/// when the process opened, so they cannot undo a limit this process already applied.
/// </summary>
internal sealed class CpuPowerBaselineStore
{
    private static readonly string FilePath = AppDataPaths.Combine("cpu-power-baseline.txt");

    public static CpuPowerBaselineStore Instance { get; } = new();

    public CpuPowerLimit? Current { get; private set; }

    private CpuPowerBaselineStore() => Current = Read();

    /// <summary>Records the first available wall and never overwrites it (INV-07).</summary>
    public void Capture(CpuPowerLimitReport report)
    {
        if (Current is not null ||
            report.Availability != CpuPowerLimitAvailability.Available ||
            !double.IsFinite(report.Pl1Watts) ||
            !double.IsFinite(report.Pl2Watts) ||
            report.Pl1Watts <= 0 ||
            report.Pl2Watts <= 0)
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
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(
                FilePath,
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"{baseline.Pl1Watts} {baseline.Pl2Watts}"));
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or
                System.Security.SecurityException or NotSupportedException)
        {
            // The in-memory baseline still restores this session; a later launch retries.
        }
    }

    private static CpuPowerLimit? Read()
    {
        try
        {
            if (!File.Exists(FilePath))
            {
                return null;
            }

            var parts = File.ReadAllText(FilePath)
                .Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 2 ||
                !double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var pl1) ||
                !double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var pl2) ||
                !double.IsFinite(pl1) ||
                !double.IsFinite(pl2) ||
                pl1 <= 0 ||
                pl2 <= 0)
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
}
