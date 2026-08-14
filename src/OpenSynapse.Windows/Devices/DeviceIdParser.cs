using System.Globalization;
using System.Text.RegularExpressions;

namespace OpenSynapse.Windows.Devices;

public static partial class DeviceIdParser
{
    [GeneratedRegex("VID_([0-9A-F]{4}).*PID_([0-9A-F]{4})", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex VidPidPattern();

    public static bool TryParse(string? deviceId, out ushort vendorId, out ushort productId)
    {
        vendorId = 0;
        productId = 0;

        var match = VidPidPattern().Match(deviceId ?? string.Empty);
        if (!match.Success ||
            !ushort.TryParse(match.Groups[1].Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out vendorId) ||
            !ushort.TryParse(match.Groups[2].Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out productId))
        {
            vendorId = 0;
            productId = 0;
            return false;
        }

        return true;
    }
}
