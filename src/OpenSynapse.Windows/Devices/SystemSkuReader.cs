using Microsoft.Win32;

namespace OpenSynapse.Windows.Devices;

/// <summary>
/// Reads the BIOS SystemSKU string the middleware uses for per-SKU configuration.
/// This build only records it; it does not gate any capability on its value.
/// </summary>
public static class SystemSkuReader
{
    public static string? TryRead()
    {
        try
        {
            using var bios = Registry.LocalMachine.OpenSubKey(
                @"HARDWARE\DESCRIPTION\System\BIOS");
            return bios?.GetValue("SystemSKU") as string;
        }
        catch
        {
            return null;
        }
    }
}
