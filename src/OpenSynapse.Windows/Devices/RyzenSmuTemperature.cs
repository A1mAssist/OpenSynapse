using OpenSynapse.Core.Devices;

namespace OpenSynapse.Windows.Devices;

/// <summary>
/// AMD APUs report the package temperature in SMN 0x00059800 (THM_TCON_CUR_TMP).
/// The 11-bit field is Tctl at 1/8 C; Tdie sits 49 C below it. Measured on a Blade 16
/// (Strix Point): idle 64-68 C and 73 C under six busy threads, matching the ACPI
/// thermal zone within one degree.
/// </summary>
public static class RyzenSmuTemperature
{
    private const ulong ThermalControlRegister = 0x00059800;
    private const double TctlOffsetCelsius = 49;

    public static bool TryRead(out double celsius, out string? error)
    {
        celsius = 0;
        var availability = RyzenSmuSession.TryOpen(out var session, out error);
        if (availability != CpuPowerLimitAvailability.Available || session is null)
        {
            return false;
        }

        using (session)
        {
            if (!session.TryReadRegister(ThermalControlRegister, out var raw, out error))
            {
                return false;
            }

            var tdie = (((raw >> 21) & 0x7FF) / 8.0) - TctlOffsetCelsius;
            if (tdie is < -10 or > 125)
            {
                error = $"The SMU reported an implausible temperature ({tdie:F1} C).";
                return false;
            }

            celsius = tdie;
            error = null;
            return true;
        }
    }
}
