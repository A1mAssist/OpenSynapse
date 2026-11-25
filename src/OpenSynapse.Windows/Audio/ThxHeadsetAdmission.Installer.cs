using OpenSynapse.Windows.Devices;

namespace OpenSynapse.Windows.Audio;

internal static partial class ThxHeadsetAdmission
{
    internal static async Task<ThxInstallerPackage?> ResolveInstallerAsync(
        ThxHeadsetProfile profile,
        CancellationToken cancellationToken,
        Action<string>? diagnostic = null)
    {
        using var manifest = await RazerManifestResources
            .FetchAsync(profile.InstallerPage, cancellationToken, diagnostic)
            .ConfigureAwait(false);
        if (manifest is null)
        {
            return null;
        }

        return SelectInstaller(profile, RazerManifestResources.Read(manifest.RootElement));
    }

    internal static ThxInstallerPackage? SelectInstaller(
        ThxHeadsetProfile profile,
        IReadOnlyList<RazerManifestResource> resources)
    {
        var productId = profile.ProductId.ToString("X4");
        ThxInstallerPackage? candidate = null;
        var candidates = 0;
        foreach (var resource in resources)
        {
            if (!IsApoInstaller(resource) || resource.Package is not { } package)
            {
                continue;
            }

            candidates++;
            var installer = ToInstaller(package, profile.NativeVersion);
            if (resource.Name.Contains(productId, StringComparison.OrdinalIgnoreCase) ||
                Path.GetFileName(resource.Url).Contains(
                    $"-{productId}-",
                    StringComparison.OrdinalIgnoreCase))
            {
                return installer;
            }
            candidate = installer;
        }

        // 052D ships THX-052C-*, and 057A shares 0579's package: a lone candidate
        // without its PID in the name is still the right one.
        return candidates == 1 ? candidate : null;
    }

    private static bool IsApoInstaller(RazerManifestResource resource)
    {
        var name = resource.Name;
        return resource.Package is not null &&
            (name.Contains("APO", StringComparison.OrdinalIgnoreCase) ||
                name.StartsWith("THX-", StringComparison.OrdinalIgnoreCase)) &&
            !name.Contains("Preset", StringComparison.OrdinalIgnoreCase) &&
            !name.Contains("NativeDLL", StringComparison.OrdinalIgnoreCase) &&
            !name.Contains("GameProfiles", StringComparison.OrdinalIgnoreCase) &&
            !name.Contains("Patcher", StringComparison.OrdinalIgnoreCase);
    }

    internal static ThxInstallerPackage ToInstaller(
        RazerManifestPackage package,
        ThxNativeVersion version) => new(
        package.Uri.ToString(),
        package.Length,
        package.Sha256,
        version == ThxNativeVersion.V3 ? "/S" : "/s /norestart",
        package.Version);
}
