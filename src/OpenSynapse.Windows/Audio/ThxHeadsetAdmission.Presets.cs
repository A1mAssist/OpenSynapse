using System.Net;
using System.Text.Json;
using Microsoft.Win32;

namespace OpenSynapse.Windows.Audio;

internal static partial class ThxHeadsetAdmission
{
    private const string PresetProductName = "THX V3 APO Presets";

    /// <summary>
    /// The preset package ships inside the per-model Razer manifest; the local
    /// table drifted from it and had to be edited by hand for every release.
    /// </summary>
    internal static Uri PresetManifestUri(int productPage) => new(
        $"https://apps.razer.com/synapse/products/{productPage}/mw/manifest.json");

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
        foreach (var productPage in PresetProductPages(profile))
        {
            try
            {
                using var response = await ComponentClient.GetAsync(
                    PresetManifestUri(productPage),
                    cancellationToken).ConfigureAwait(false);
                if (response.StatusCode == HttpStatusCode.NotFound)
                {
                    continue;
                }
                response.EnsureSuccessStatusCode();
                await using var stream = await response.Content
                    .ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
                using var document = await JsonDocument.ParseAsync(
                    stream,
                    cancellationToken: cancellationToken).ConfigureAwait(false);
                var preset = ReadPresetInstaller(profile.ProductId, document.RootElement);
                if (preset is not null)
                {
                    return preset;
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception) when (
                exception is HttpRequestException or IOException or JsonException or TaskCanceledException)
            {
                // The APO installer is the required part; a missing preset degrades
                // spatial processing but must not block the install.
                return null;
            }
        }

        return null;
    }

    /// <summary>
    /// Razer files most models under the decimal PID, but 053C/0565/0568/056C only
    /// exist under the number in their THX installer URL. Try both.
    /// </summary>
    internal static IEnumerable<int> PresetProductPages(ThxHeadsetProfile profile)
    {
        var installerPage = InstallerProductPage(profile.InstallerUri);
        if (installerPage is { } page && page != profile.ProductId)
        {
            yield return page;
        }
        yield return profile.ProductId;
    }

    private static int? InstallerProductPage(string? installerUri)
    {
        if (installerUri is null)
        {
            return null;
        }

        var parts = installerUri.Split('/');
        for (var index = 0; index + 1 < parts.Length; index++)
        {
            if (parts[index].Equals("devices", StringComparison.OrdinalIgnoreCase) &&
                int.TryParse(parts[index + 1], out var page))
            {
                return page;
            }
        }

        return null;
    }

    internal static ThxInstallerPackage? ReadPresetInstaller(
        ushort productId,
        JsonElement manifest)
    {
        if (!manifest.TryGetProperty("resourceManifest", out var resourceManifest) ||
            !resourceManifest.TryGetProperty("resources", out var resources) ||
            resources.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        // Shared manifests (1383, 1387, 1390) carry one preset per PID, and the
        // file name is the only per-PID field: 052D even ships THX-052C-*.
        var expected = $"THX-{productId:X4}-";
        ThxInstallerPackage? candidate = null;
        var candidates = 0;
        foreach (var resource in resources.EnumerateArray())
        {
            var name = resource.TryGetProperty("resourceName", out var nameValue)
                ? nameValue.GetString()
                : null;
            var url = resource.TryGetProperty("url", out var urlValue)
                ? urlValue.GetString()
                : null;
            if (url is null ||
                (!url.Contains("Presets", StringComparison.OrdinalIgnoreCase) &&
                    name?.Contains("Preset", StringComparison.OrdinalIgnoreCase) != true))
            {
                continue;
            }

            candidates++;
            var package = ReadPackage(resource, url);
            if (package is null)
            {
                continue;
            }
            if (Path.GetFileName(url).Contains(expected, StringComparison.OrdinalIgnoreCase))
            {
                return package;
            }
            candidate = package;
        }

        return candidates == 1 ? candidate : null;
    }

    private static ThxInstallerPackage? ReadPackage(JsonElement resource, string url)
    {
        if (!resource.TryGetProperty("size", out var sizeValue) ||
            !sizeValue.TryGetInt64(out var size) || size <= 0 ||
            !resource.TryGetProperty("sha256", out var shaValue) ||
            shaValue.GetString() is not { Length: 64 } sha256 ||
            !sha256.All(Uri.IsHexDigit))
        {
            return null;
        }

        var version = resource.TryGetProperty("resourceVersion", out var versionValue) &&
            Version.TryParse(versionValue.GetString(), out var parsed)
                ? parsed
                : ParsePackageVersion(url);
        return version is null
            ? null
            : new ThxInstallerPackage(url, size, sha256.ToUpperInvariant(), "/S", version);
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
