namespace OpenSynapse.Windows.Devices;

/// <summary>
/// The three product identities used by the driver chain: the connected device,
/// the catalog package key and the PID in the official installer file name.
/// </summary>
internal static class RazerDriverIdentity
{
    internal static bool IsPackageInstalled(string root, ushort productId)
    {
        var id = productId.ToString("X4");
        return File.Exists(Path.Combine(
            root,
            "Razer",
            "Synapse3",
            "Uninstall",
            $"PID{id}Drv",
            $"PID{id}DrvUninstaller.exe"));
    }

    internal static bool IsPackageInstalled(
        string root,
        string fallbackRoot,
        IReadOnlyList<ushort> candidates) =>
        candidates.Any(productId =>
            IsPackageInstalled(root, productId) ||
            IsPackageInstalled(fallbackRoot, productId));

    internal static ushort? TryParseEndpointProductId(string path)
    {
        const string marker = "PID_";
        var index = path.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (index < 0 || index + marker.Length + 4 > path.Length)
        {
            return null;
        }

        return ushort.TryParse(
            path.AsSpan(index + marker.Length, 4),
            System.Globalization.NumberStyles.HexNumber,
            null,
            out var productId)
            ? productId
            : null;
    }

    internal static HashSet<ushort> ExpandReady(IEnumerable<ushort> endpointProductIds)
    {
        var ready = new HashSet<ushort>();
        foreach (var productId in endpointProductIds)
        {
            ready.Add(productId);
            if (RazerDriverPackageCatalog.TryResolve(productId, out var package))
            {
                ready.UnionWith(
                    RazerDriverPackageCatalog.GetCandidateProductIds(productId, package));
            }
        }

        return ready;
    }
}
