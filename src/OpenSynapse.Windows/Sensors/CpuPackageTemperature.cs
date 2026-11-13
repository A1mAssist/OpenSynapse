using OpenSynapse.Windows.Devices;

namespace OpenSynapse.Windows.Sensors;

/// <summary>
/// Package temperature for either vendor, read from the elevated service so the
/// PawnIO-backed readers can open their driver and kernel module.
/// </summary>
public static class CpuPackageTemperature
{
    public static bool TryRead(out double celsius, out string? error)
    {
        if (CpuVendor.IsAmd(out _))
        {
            return RyzenSmuTemperature.TryRead(out celsius, out error);
        }

        using var reader = new IntelMsrTemperatureReader();
        if (reader.ReadPackageTemperatureCelsius() is double packageTemperature)
        {
            celsius = packageTemperature;
            error = null;
            return true;
        }

        celsius = 0;
        error = "PawnIO Intel package temperature is unavailable.";
        return false;
    }
}
