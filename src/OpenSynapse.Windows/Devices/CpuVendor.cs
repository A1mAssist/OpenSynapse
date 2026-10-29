using Microsoft.Win32;

namespace OpenSynapse.Windows.Devices;

/// <summary>Intel and AMD expose the package wall through different channels.</summary>
internal static class CpuVendor
{
    public static bool IsAmd(out string? processorName)
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(
                @"HARDWARE\DESCRIPTION\System\CentralProcessor\0");
            processorName = (key?.GetValue("ProcessorNameString") as string)?.Trim();
            return processorName is not null &&
                (processorName.Contains("AMD", StringComparison.OrdinalIgnoreCase) ||
                 processorName.Contains("Ryzen", StringComparison.OrdinalIgnoreCase) ||
                 processorName.Contains("EPYC", StringComparison.OrdinalIgnoreCase) ||
                 processorName.Contains("Threadripper", StringComparison.OrdinalIgnoreCase));
        }
        catch (Exception exception) when (
            exception is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            processorName = null;
            return false;
        }
    }
}
