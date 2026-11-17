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
            // Razer Viper V3 Pro
            [0x00DF] = 0x00DE,
            // Razer Basilisk V4 Pro
            [0x00E3] = 0x00E2,
            [0x00E4] = 0x00E2,
            // Razer Naga V3 Pro
            [0x00E8] = 0x00E7,
            [0x00E9] = 0x00E7,
            // Razer Basilisk V4 HyperSpeed
            [0x00EA] = 0x00EB,
            [0x00EC] = 0x00EB,
            // Razer Basilisk V3 Pro 35K Xbox 25th Anniversary Edition
            [0xB00B] = 0xB00A,
            [0xB00C] = 0xB00A,
            // Wireless receivers and Bluetooth identities share the wired model's
            // product page and driver package.
            [0x005A] = 0x0059,
            [0x006F] = 0x0070,
            [0x0072] = 0x0073,
            [0x0077] = 0x0080,
            [0x007B] = 0x007A,
            [0x007D] = 0x007C,
            [0x0090] = 0x008F,
            [0x0095] = 0x0094,
            [0x009F] = 0x009E,
            [0x00A6] = 0x00A5,
            [0x00A8] = 0x00A7,
            [0x00AB] = 0x00AA,
            [0x00B0] = 0x00AF,
            [0x00B7] = 0x00B6,
            [0x00BF] = 0x00BE,
            [0x00C1] = 0x00C0,
            [0x00C3] = 0x00C2,
            [0x00C5] = 0x00C4,
            [0x00C8] = 0x00C7,
            [0x00CD] = 0x00CC,
            [0x00D1] = 0x00D0,
            [0x00D4] = 0x00D3,
            [0x00D7] = 0x00D6,
            [0x00DB] = 0x00DA,
            [0x00E6] = 0x00E5,
        };

    // These siblings expose the protocol-30 BLE page, not the 91-byte protocol-25 keyboard path.
    private static readonly IReadOnlySet<ushort> BleProductIds = new HashSet<ushort>
    {
        0x02CA, 0x02D3, 0x02B5, 0x02C3, 0x02F6,
    };

    /// <summary>The wired PID that owns the model's capabilities, or the PID itself.</summary>
    public static ushort Canonical(ushort productId) =>
        CanonicalProductIds.TryGetValue(productId, out var canonical) ? canonical : productId;

    internal static IReadOnlyDictionary<ushort, ushort> Aliases => CanonicalProductIds;

    /// <summary>True when the PID is a wireless sibling of a wired model.</summary>
    public static bool IsWirelessAlias(ushort productId) => CanonicalProductIds.ContainsKey(productId);

    /// <summary>True when the PID is the Bluetooth LE sibling handled by the BLE page.</summary>
    public static bool IsBleAlias(ushort productId) => BleProductIds.Contains(productId);
}
