using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace OpenSynapse.Windows.Devices;

internal static partial class RazerHostModel
{
    private const string BiosKey = @"HARDWARE\DESCRIPTION\System\BIOS";
    private const string SystemInformationKey =
        @"SYSTEM\CurrentControlSet\Control\SystemInformation";

    public static string? Read()
    {
        using var localMachine = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
        using var bios = localMachine.OpenSubKey(BiosKey);
        using var system = localMachine.OpenSubKey(SystemInformationKey);
        var biosManufacturer = bios?.GetValue("SystemManufacturer") as string;
        var biosProduct = bios?.GetValue("SystemProductName") as string;
        var systemManufacturer = system?.GetValue("SystemManufacturer") as string;
        var systemProduct = system?.GetValue("SystemProductName") as string;
        return biosManufacturer == "Razer" &&
               systemManufacturer == "Razer" &&
               string.Equals(biosProduct, systemProduct, StringComparison.Ordinal)
            ? biosProduct
            : null;
    }

    public static string? ReadModelCode()
    {
        var model = Read();
        if (string.IsNullOrWhiteSpace(model))
        {
            return null;
        }

        var match = ModelCodeRegex().Match(model);
        return match.Success ? match.Value : null;
    }

    [GeneratedRegex(@"RZ09-\d{4}[A-Z0-9]*", RegexOptions.IgnoreCase)]
    private static partial Regex ModelCodeRegex();
}
