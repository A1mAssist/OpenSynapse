using Microsoft.Win32;

namespace OpenSynapse.Windows.Devices;

public static class WindowsFirmwareVersionReader
{
    private const string BiosRegistryPath = @"HARDWARE\DESCRIPTION\System\BIOS";
    private const string VideoRegistryPath = @"SYSTEM\CurrentControlSet\Control\Video";

    public static (string Bios, string Ec, string Vbios) Read()
    {
        var bios = "--";
        var ec = "--";
        try
        {
            using var biosKey = Registry.LocalMachine.OpenSubKey(BiosRegistryPath, writable: false);
            bios = FormatValue(biosKey?.GetValue("BIOSVersion"));
            ec = FormatEc(
                biosKey?.GetValue("ECFirmwareMajorRelease"),
                biosKey?.GetValue("ECFirmwareMinorRelease"));
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            // Keep vBIOS readable even when the SMBIOS key is unavailable.
        }

        return (bios, ec, ReadVbios());
    }

    private static string ReadVbios()
    {
        try
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
                    var adapter = FormatValue(instanceKey?.GetValue("HardwareInformation.AdapterString"), keepMissing: true);
                    var bios = FormatValue(instanceKey?.GetValue("HardwareInformation.BiosString"), keepMissing: true);
                    if (!adapter.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase) ||
                        string.IsNullOrWhiteSpace(bios) || bios == "--")
                    {
                        continue;
                    }

                    return bios.StartsWith("Version", StringComparison.OrdinalIgnoreCase)
                        ? bios["Version".Length..].Trim()
                        : bios;
                }
            }
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            // vBIOS is supplementary; leave it blank rather than failing all firmware fields.
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

    private static string FormatValue(object? value, bool keepMissing = false) => value switch
    {
        string text when !string.IsNullOrWhiteSpace(text) => text.TrimEnd('\0').Trim(),
        string[] values when values.Any(item => !string.IsNullOrWhiteSpace(item)) =>
            string.Join(" ", values.Where(item => !string.IsNullOrWhiteSpace(item))),
        byte[] bytes => DecodeUnicode(bytes),
        _ => keepMissing ? string.Empty : "--",
    };

    private static string DecodeUnicode(byte[] bytes)
    {
        var text = System.Text.Encoding.Unicode.GetString(bytes).TrimEnd('\0').Trim();
        return string.IsNullOrWhiteSpace(text) ? "--" : text;
    }
}
