using OpenSynapse.Core.Devices;

namespace OpenSynapse.Windows.Devices;

/// <summary>
/// The 0F1F per-LED path needs Razer's RZCONTROL filter driver before the
/// 320-byte SET_FEATURE frame can be sent. When the endpoint is missing, the
/// lighting page offers Razer's signed driver package instead of hiding silently.
/// </summary>
internal static class RazerFilterDriverAdmission
{
    internal const ushort ArgbProductId = 0x0F1F;
    internal const string ArgbInstallerUri =
        "https://app-assets.razer.com/files/synapse/devices/3871/1.0.0.0/PID0f1fDrv_v0.1.0.0.exe";

    internal static bool NeedsDriver(ushort productId, bool endpointReady) =>
        productId == ArgbProductId && !endpointReady;

    /// <summary>
    /// Only mice and keyboards reach the RZCONTROL endpoint the filter driver publishes.
    /// Headsets, mats, lamps, monitors and stands light themselves over feature reports or
    /// output reports, so offering their driver would install a filter for nothing.
    /// </summary>
    internal static bool NeedsFilterDriver(ushort productId)
    {
        var category = ResolveCategory(productId);
        return category is DeviceCategory.Mouse or DeviceCategory.Keyboard;
    }

    private static DeviceCategory? ResolveCategory(ushort productId)
    {
        // Dongles enumerate their own PID; the wired sibling owns the catalogue entry.
        var canonical = RazerProductAliases.Canonical(productId);
        if (RazerHidDeviceCatalog.BuiltIn.Find(0x1532, canonical) is { } hid)
        {
            return hid.Category;
        }

        if (RazerDeviceRegistry.BuiltIn.Find(0x1532, canonical) is { } registry)
        {
            return registry.Category;
        }

        if (RazerKeyboardService.Products.ContainsKey(canonical))
        {
            return DeviceCategory.Keyboard;
        }

        return RazerChromaStandardService.Products.TryGetValue(canonical, out var chroma)
            ? chroma.Category
            : null;
    }
}
