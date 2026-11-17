using System.Text.Json;
using Microsoft.Win32;
using OpenSynapse.Windows.Devices;

namespace OpenSynapse.Windows.Audio;

internal static partial class ThxHeadsetAdmission
{
    private const string PresetProductName = "THX V3 APO Presets";

    /// <summary>
    /// A newer preset package already on the machine must not be replaced by the
    /// one the manifest offers; the older list is what the native client fails
    /// to parse.
    /// </summary>
    internal static bool ShouldInstallPreset(Version? installed, Version? available) =>
        available is not null && (installed is null || installed < available);

    internal static async Task<ThxInstallerPackage?> ResolvePresetInstallerAsync(
        ThxHeadsetProfile profile,
        CancellationToken cancellationToken)
    {
        // A missing preset degrades spatial processing but must not block the
        // install, so a failed fetch simply skips it.
        using var manifest = await RazerManifestResources
            .FetchAsync(profile.InstallerPage, cancellationToken)
            .ConfigureAwait(false);
        return manifest is null
            ? null
            : ReadPresetInstaller(profile.ProductId, manifest.RootElement);
    }

    internal static ThxInstallerPackage? ReadPresetInstaller(
        ushort productId,
        JsonElement manifest)
    {
        // Shared manifests (1383, 1387, 1390) carry one preset per PID, and the
        // file name is the only per-PID field: 052D even ships THX-052C-*.
        var expected = $"THX-{productId:X4}-";
        ThxInstallerPackage? candidate = null;
        var candidates = 0;
        foreach (var resource in RazerManifestResources.Read(manifest))
        {
            if (!resource.Url.Contains("Presets", StringComparison.OrdinalIgnoreCase) &&
                !resource.Name.Contains("Preset", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            candidates++;
            if (resource.Package is not { } package)
            {
                continue;
            }
            var installer = ToInstaller(package, ThxNativeVersion.V3);
            if (Path.GetFileName(resource.Url).Contains(expected, StringComparison.OrdinalIgnoreCase))
            {
                return installer;
            }
            candidate = installer;
        }

        return candidates == 1 ? candidate : null;
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
