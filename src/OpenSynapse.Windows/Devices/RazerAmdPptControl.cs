using OpenSynapse.Core.Devices;

namespace OpenSynapse.Windows.Devices;

/// <summary>One PPT wall: normal user writes and trusted-baseline restore have different ceilings.</summary>
internal sealed class RazerAmdPptControl
{
    private readonly CpuPowerLimit _firmware;
    private readonly double _minimum, _maximum, _restoreMaximum;
    private readonly Func<(double? Watts, string? Error)> _read;
    private readonly Func<uint, byte, int> _write;
    private readonly Action<int> _delay;

    internal RazerAmdPptControl(CpuPowerLimit firmware, double minimum, double capabilityMaximum,
        Func<(double? Watts, string? Error)> read, Func<uint, byte, int> write, Action<int>? delay = null)
    {
        if (!double.IsFinite(minimum) || !double.IsFinite(capabilityMaximum) ||
            minimum < CpuPowerLimit.MinimumWatts || capabilityMaximum < minimum || minimum > CpuPowerLimit.MaximumWatts)
            throw new ArgumentOutOfRangeException(nameof(capabilityMaximum));
        _firmware = firmware;
        _minimum = minimum;
        _maximum = Math.Min(CpuPowerLimit.MaximumWatts, Math.Floor(capabilityMaximum));
        _restoreMaximum = Math.Min(CpuPowerLimit.MaximumRestoreWatts, Math.Floor(capabilityMaximum));
        _read = read; _write = write; _delay = delay ?? Thread.Sleep;
    }

    internal CpuPowerLimitReport Read()
    {
        var (watts, error) = _read();
        return watts is { } value && double.IsFinite(value) && value >= _minimum && value <= _restoreMaximum
            ? Report(value) : Failed(error ?? "AMD PPT readback is outside the verified capability range.");
    }

    internal CpuPowerLimitReport Apply(CpuPowerLimit limit) => SetAndVerify(
        Math.Clamp(limit.Clamp(_maximum).Pl1Watts, _minimum, _maximum));

    internal CpuPowerLimitReport RestoreTo(double pl1Watts, double pl2Watts)
    {
        if (!double.IsFinite(pl1Watts) || !double.IsFinite(pl2Watts) ||
            pl1Watts < _minimum || pl2Watts < _minimum || pl1Watts > _restoreMaximum || pl2Watts > _restoreMaximum)
            return Failed("The saved AMD PPT baseline is outside the verified capability/restore range; nothing was written.");
        return SetAndVerify(pl1Watts);
    }

    internal CpuPowerLimitReport RestoreFirmware() => RestoreTo(_firmware.Pl1Watts, _firmware.Pl2Watts);

    private CpuPowerLimitReport SetAndVerify(double requested)
    {
        var status = _write(checked((uint)Math.Round(requested)), 1);
        if (status != 0) return Failed($"The AMD Ryzen Master driver rejected the PPT write ({status}).");
        _delay(250);
        var report = Read();
        if (report.Availability != CpuPowerLimitAvailability.Available) return report;
        return Math.Abs(report.Pl1Watts - requested) <= 1 ? report
            : Failed($"The AMD driver accepted the PPT write but reports {report.Pl1Watts:0.#} W.");
    }

    private CpuPowerLimitReport Report(double watts) => new(CpuPowerLimitAvailability.Available, null,
        watts, watts, _firmware.Pl1Watts, _firmware.Pl2Watts, _maximum, false);
    private static CpuPowerLimitReport Failed(string? error) => CpuPowerLimitReport.Unavailable(CpuPowerLimitAvailability.Failed, error);
}
