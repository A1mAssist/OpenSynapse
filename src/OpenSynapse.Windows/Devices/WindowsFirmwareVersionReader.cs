using Microsoft.Win32;

namespace OpenSynapse.Windows.Devices;

public static class WindowsFirmwareVersionReader
{
    private const string BiosRegistryPath = @"HARDWARE\DESCRIPTION\System\BIOS";
    private const string VideoRegistryPath = @"SYSTEM\CurrentControlSet\Control\Video";

    public static (string Bios, string Ec, string Vbios) Read()
    {
        try
        {
            using var biosKey = Registry.LocalMachine.OpenSubKey(BiosRegistryPath, writable: false);
            var bios = FormatValue(biosKey?.GetValue("BIOSVersion"));
            var ec = FormatEc(
                biosKey?.GetValue("ECFirmwareMajorRelease"),
                biosKey?.GetValue("ECFirmwareMinorRelease"));
            return (bios, ec, ReadVbios());
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            return ("--", "--", "--");
        }
    }

    private static string ReadVbios()
    {
        using var videoRoot = Registry.LocalMachine.OpenSubKey(VideoRegistryPath, writable: false);
        if (videoRoot is null)
        {
            return "--";
        }

        foreach (var adapterKeyName in videoRoot.GetSubKeyNames())
        {
            using var adapterKey = videoRoot.OpenSubKey(adapterKeyName, writable: false);
            if (adapterKey is null)
            {
                continue;
            }

            foreach (var instanceKeyName in adapterKey.GetSubKeyNames())
            {
                using var instanceKey = adapterKey.OpenSubKey(instanceKeyName, writable: false);
                if (instanceKey?.GetValue("HardwareInformation.AdapterString") is not string adapter ||
                    !adapter.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase) ||
                    instanceKey.GetValue("HardwareInformation.BiosString") is not string bios ||
                    string.IsNullOrWhiteSpace(bios))
                {
                    continue;
                }

                var value = bios.Trim();
                return value.StartsWith("Version", StringComparison.OrdinalIgnoreCase)
                    ? value["Version".Length..].Trim()
                    : value;
            }
        }

        return "--";
    }

    private static string FormatEc(object? majorValue, object? minorValue)
    {
        if (!TryConvertByte(majorValue, out var major) || !TryConvertByte(minorValue, out var minor))
        {
            return "--";
        }

        return $"{major}.{minor:D2}";
    }

    private static bool TryConvertByte(object? value, out int result)
    {
        try
        {
            result = Convert.ToInt32(value, System.Globalization.CultureInfo.InvariantCulture);
            return result is >= byte.MinValue and <= byte.MaxValue;
        }
        catch (Exception exception) when (
            exception is FormatException or InvalidCastException or OverflowException)
        {
            result = 0;
            return false;
        }
    }

    private static string FormatValue(object? value) => value switch
    {
        string text when !string.IsNullOrWhiteSpace(text) => text.Trim(),
        string[] values when values.Any(item => !string.IsNullOrWhiteSpace(item)) =>
            string.Join(" ", values.Where(item => !string.IsNullOrWhiteSpace(item))),
        _ => "--",
    };
}
