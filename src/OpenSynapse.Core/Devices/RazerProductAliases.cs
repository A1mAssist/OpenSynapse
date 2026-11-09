namespace OpenSynapse.Core.Devices;

/// <summary>
/// Wireless identities that Windows enumerates for a model whose wired PID owns the
/// manifest, the device page and the lighting data. Razer's own
/// <c>DeviceManifest_&lt;pid&gt;_0.json</c> declares both sides through
/// <c>wiredProductId</c> and <c>dongleProductId</c> / <c>bleProductId</c>; the pairs
/// below are copied from those manifests for the products this app already drives.
/// </summary>
public static class RazerProductAliases
{
    private static readonly IReadOnlyDictionary<ushort, ushort> CanonicalProductIds =
        new Dictionary<ushort, ushort>
        {
            // Razer Basilisk Ultimate: 134/0086 owns the product UI and both driver packages;
            // 0088 is the wireless receiver identity.
            [0x0088] = 0x0086,
            // Razer BlackWidow V4 Low-profile HyperSpeed
            [0x02C9] = 0x02CC,
            [0x02CA] = 0x02CC,
            // Razer BlackWidow V4 Low-profile Tenkeyless HyperSpeed
            [0x02D2] = 0x02D4,
            [0x02D3] = 0x02D4,
            // Razer BlackWidow V4 Pro 75%
            [0x02B4] = 0x02B3,
            [0x02B5] = 0x02B3,
            // Razer Pro Type Ergo
            [0x02C2] = 0x02C4,
            [0x02C3] = 0x02C4,
            // Razer Reclusa Mini 65%
            [0x02F5] = 0x02F4,
            [0x02F6] = 0x02F4,
        };

    // These siblings expose the protocol-30 BLE page, not the 91-byte protocol-25 keyboard path.
    private static readonly IReadOnlySet<ushort> BleProductIds = new HashSet<ushort>
    {
        0x02CA, 0x02D3, 0x02B5, 0x02C3, 0x02F6,
    };

    /// <summary>The wired PID that owns the model's capabilities, or the PID itself.</summary>
    public static ushort Canonical(ushort productId) =>
        CanonicalProductIds.TryGetValue(productId, out var canonical) ? canonical : productId;

    /// <summary>True when the PID is a wireless sibling of a wired model.</summary>
    public static bool IsWirelessAlias(ushort productId) => CanonicalProductIds.ContainsKey(productId);

    /// <summary>True when the PID is the Bluetooth LE sibling handled by the BLE page.</summary>
    public static bool IsBleAlias(ushort productId) => BleProductIds.Contains(productId);
}
