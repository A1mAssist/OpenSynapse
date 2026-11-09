using System.Globalization;
using System.Text.RegularExpressions;
using OpenSynapse.Core.Devices;

namespace OpenSynapse.Windows.Devices;

public sealed partial class RazerFirmwareUpdateClient
{
    private static FirmwareUpdateCheckResult CompareManifestComponents(
        FirmwareVersions installed, RazerFirmwareRelease release, string? deviceName)
    {
        var components = release.Additional ?? [];
        var bios = SingleVersion(components, "bios");
        var ec = SingleVersion(components, "ec");
        var mcu = SingleVersion(components, "mcu");
        var newer = new List<string>();
        var uncheckedComponents = new List<string>();
        CompareComponent("BIOS", installed.Bios, bios, newer, uncheckedComponents);
        CompareComponent("EC", installed.Ec, ec, newer, uncheckedComponents);
        CompareComponent("MCU", installed.Mcu, mcu, newer, uncheckedComponents);
        var vbios = components.Where(item => item.Type.Equals("vbios", StringComparison.OrdinalIgnoreCase)).ToArray();
        CompareVbios(installed, vbios, newer, uncheckedComponents);
        foreach (var component in components.Where(item =>
            !new[] { "bios", "ec", "mcu", "vbios" }.Contains(item.Type, StringComparer.OrdinalIgnoreCase)))
            uncheckedComponents.Add(component.Type);
        var downloadUrl = newer.Count == 0 ? release.Url : components.FirstOrDefault(item =>
            item.Type.Equals(newer[0], StringComparison.OrdinalIgnoreCase) &&
            (newer[0] != "vBIOS" || GpuModel(item.Model) == GpuModel(installed.VbiosModel)))?.Url;
        return new(bios, ec, mcu,
            vbios.Length == 0 ? null : string.Join(" · ", vbios.Select(item => $"{item.Model} {item.Version}")),
            newer, downloadUrl, deviceName, uncheckedComponents.Distinct(StringComparer.OrdinalIgnoreCase).ToArray());
    }

    private static string? SingleVersion(IEnumerable<RazerFirmwareComponent> components, string type)
    {
        var versions = components.Where(item => item.Type.Equals(type, StringComparison.OrdinalIgnoreCase))
            .Select(item => item.Version).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        return versions.Length == 1 ? versions[0] : null;
    }

    private static void CompareComponent(string name, string installed, string? published,
        List<string> newer, List<string> uncheckedComponents, bool hexadecimal = false)
    {
        var comparison = CompareVersions(installed, published, hexadecimal);
        if (comparison is null) uncheckedComponents.Add(name);
        else if (comparison > 0) newer.Add(name);
    }

    private static void CompareVbios(FirmwareVersions installed, IEnumerable<RazerFirmwareComponent> candidates,
        List<string> newer, List<string> uncheckedComponents)
    {
        var model = GpuModel(installed.VbiosModel);
        var matches = candidates.Where(item => model is not null && GpuModel(item.Model) == model)
            .Select(item => item.Version).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        CompareComponent("vBIOS", installed.Vbios, matches.Length == 1 ? matches[0] : null,
            newer, uncheckedComponents, hexadecimal: true);
    }

    internal static int? CompareVersions(string? installed, string? published, bool hexadecimal = false)
    {
        static uint[]? Parse(string? text, bool hex)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;
            var tokens = text.Trim().TrimStart('v', 'V').Split('.');
            if (tokens.Length < 2) return null;
            var parts = new uint[tokens.Length];
            for (var index = 0; index < tokens.Length; index++)
                if (!uint.TryParse(tokens[index], hex ? NumberStyles.AllowHexSpecifier : NumberStyles.None,
                    CultureInfo.InvariantCulture, out parts[index])) return null;
            return parts;
        }
        var current = Parse(installed, hexadecimal);
        var offered = Parse(published, hexadecimal);
        if (current is null || offered is null || (hexadecimal && current.Length != offered.Length)) return null;
        for (var index = 0; index < Math.Max(current.Length, offered.Length); index++)
        {
            var comparison = (index < offered.Length ? offered[index] : 0)
                .CompareTo(index < current.Length ? current[index] : 0);
            if (comparison != 0) return comparison;
        }
        return 0;
    }

    private static string? GpuModel(string? model)
    {
        var match = GpuModelRegex().Match(model ?? string.Empty);
        return match.Success ? $"RTX{match.Groups["number"].Value}{match.Groups["ti"].Value.ToUpperInvariant()}" : null;
    }

    [GeneratedRegex(@"\bRTX\s*(?<number>\d{4})(?:\s*(?<ti>Ti))?\b", RegexOptions.IgnoreCase)]
    private static partial Regex GpuModelRegex();
}
