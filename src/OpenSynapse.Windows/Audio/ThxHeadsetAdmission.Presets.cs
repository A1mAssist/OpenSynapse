using Microsoft.Win32;

namespace OpenSynapse.Windows.Audio;

internal static partial class ThxHeadsetAdmission
{
    private const string PresetProductName = "THX V3 APO Presets";

    /// <summary>
    /// A newer preset package already on the machine must not be replaced by the
    /// older package this app pins; the older list is what the native client
    /// fails to parse.
    /// </summary>
    internal static bool ShouldInstallPresetPackage(string? installedVersion, string? packageUri)
    {
        if (!Version.TryParse(installedVersion, out var installed) ||
            ParsePackageVersion(packageUri) is not { } pinned)
        {
            return true;
        }

        return installed < pinned;
    }

    internal static Version? ParsePackageVersion(string? packageUri)
    {
        if (string.IsNullOrWhiteSpace(packageUri))
        {
            return null;
        }

        var name = Path.GetFileName(packageUri);
        var marker = name.LastIndexOf("-v", StringComparison.OrdinalIgnoreCase);
        if (marker < 0)
        {
            return null;
        }

        var start = marker + 2;
        var end = name.IndexOf(".exe", start, StringComparison.OrdinalIgnoreCase);
        return end > start && Version.TryParse(name[start..end], out var version)
            ? version
            : null;
    }

    private static Version? ReadInstalledPresetVersion()
    {
        Version? newest = null;
        foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
        {
            try
            {
                using var machine = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view);
                using var uninstall = machine.OpenSubKey(
                    @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall");
                if (uninstall is null)
                {
                    continue;
                }

                foreach (var name in uninstall.GetSubKeyNames())
                {
                    using var key = uninstall.OpenSubKey(name);
                    if (key?.GetValue("DisplayName") is not string displayName ||
                        !string.Equals(
                            displayName,
                            PresetProductName,
                            StringComparison.OrdinalIgnoreCase) ||
                        key.GetValue("DisplayVersion") is not string text ||
                        !Version.TryParse(text, out var version))
                    {
                        continue;
                    }

                    if (newest is null || version > newest)
                    {
                        newest = version;
                    }
                }
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException or
                    System.Security.SecurityException or PlatformNotSupportedException)
            {
            }
        }

        return newest;
    }
}
