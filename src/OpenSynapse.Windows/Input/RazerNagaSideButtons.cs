using OpenSynapse.Core.Devices;

namespace OpenSynapse.Windows.Input;

internal static class RazerNagaSideButtons
{
    private const ushort RazerVendorId = 0x1532;

    // Naga PIDs from the middleware bundles. The product-name check below
    // keeps a new Naga model working when its PID is not listed yet.
    private static readonly HashSet<ushort> ProductIds =
    [
        0x0015, 0x001F, 0x0021, 0x002E, 0x0036, 0x003E, 0x003F, 0x0040, 0x0041,
        0x0050, 0x0053, 0x0067, 0x008D, 0x008F, 0x0090, 0x0092, 0x0093,
        0x0096, 0x00A7, 0x00A8, 0x00A9, 0x00B4, 0x00B5, 0x00E7, 0x00E8,
        0x00E9,
    ];

    // 00B4/00B5 mapping button list, in the order the firmware reports it. The
    // device normally answers the list request; this covers the reads that time
    // out or return no table, which used to leave the mapping page blank.
    private static readonly byte[] NagaV2HyperSpeedMappingButtonIds =
    [
        0x01, 0x02, 0x03, 0x34, 0x35, 0x0B, 0x0C,
        0x40, 0x43, 0x46, 0x49, 0x41, 0x44, 0x47, 0x4A, 0x42, 0x45, 0x48, 0x4B,
        0x09, 0x0A,
    ];

    // Firmware ids from each middleware bundle's normal-mode input table. Include
    // every supported plate so its saved mappings remain editable while detached.
    private static readonly byte[] TwelveButtonPlate =
        [0x40, 0x43, 0x46, 0x49, 0x41, 0x44, 0x47, 0x4A, 0x42, 0x45, 0x48, 0x4B];
    private static readonly byte[] SixButtonPlate = [0x50, 0x51, 0x52, 0x53, 0x54, 0x55];
    private static readonly byte[] TrinityMappingButtonIds =
        [0x01, 0x02, 0x03, 0x34, 0x35, 0x0B, 0x0C, 0x04, 0x05,
            .. TwelveButtonPlate, .. SixButtonPlate, 0x56, 0x09, 0x0A];
    private static readonly byte[] LeftHandedMappingButtonIds =
        [0x01, 0x02, 0x03, 0x34, 0x35, 0x0B, 0x0C, 0x0E,
            .. TwelveButtonPlate, 0x09, 0x0A];
    private static readonly byte[] ProMappingButtonIds =
        [0x01, 0x02, 0x03, 0x34, 0x35, 0x0B, 0x0C, 0x0E, 0x04, 0x05,
            .. TwelveButtonPlate, .. SixButtonPlate, 0x09, 0x0A];
    private static readonly byte[] XMappingButtonIds =
        [0x01, 0x02, 0x03, 0x60, .. TwelveButtonPlate, 0x09, 0x0A];
    private static readonly byte[] V2ProMappingButtonIds =
        [0x01, 0x02, 0x03, 0x34, 0x35, 0x0E, 0x60, 0x6D, 0x04, 0x05,
            .. TwelveButtonPlate, .. SixButtonPlate, 0x09, 0x0A];
    private static readonly byte[] V3ProMappingButtonIds =
        [0x01, 0x02, 0x03, 0x34, 0x35, 0x0E, 0x6A, 0x39, 0x80, 0x04, 0x05,
            .. TwelveButtonPlate, .. SixButtonPlate, 0x09, 0x0A];

    internal static bool TryGetMappingButtonIds(
        ushort productId,
        out IReadOnlyList<byte> ids)
    {
        ids = RazerProductAliases.Canonical(productId) switch
        {
            0x0067 or 0x0093 => TrinityMappingButtonIds,
            0x008D => LeftHandedMappingButtonIds,
            0x008F => ProMappingButtonIds,
            0x0096 => XMappingButtonIds,
            0x00A7 => V2ProMappingButtonIds,
            0x00B4 => NagaV2HyperSpeedMappingButtonIds,
            0x00E7 => V3ProMappingButtonIds,
            _ => [],
        };
        return ids.Count > 0;
    }

    internal static int SmallPlateButtonCount(ushort productId) =>
        RazerProductAliases.Canonical(productId) switch
        {
            0x0050 or 0x0067 or 0x0093 => 7,
            0x008F or 0x00A7 or 0x00E7 => 6,
            _ => 0,
        };

    internal static bool MatchesDevice(
        int boundProductId, string? boundInstanceId, ushort productId, string? instanceId) =>
        (boundProductId == 0 || boundProductId is > 0 and <= ushort.MaxValue &&
            RazerProductAliases.Canonical((ushort)boundProductId) == RazerProductAliases.Canonical(productId)) &&
        (string.IsNullOrWhiteSpace(boundInstanceId) ||
         StringComparer.OrdinalIgnoreCase.Equals(boundInstanceId.Trim(), instanceId?.Trim()));

    internal static bool IsNaga(ushort vendorId, ushort productId, string? productName) =>
        vendorId switch
        {
            RazerVendorId => ProductIds.Contains(productId) ||
                productName?.Contains("Naga", StringComparison.OrdinalIgnoreCase) == true,
            // Naga BLE identities documented in RazerProductAliases; names alone cannot admit this vendor.
            0x068E => productId is 0x0092 or 0x00A9 or 0x00B5 or 0x00E9,
            _ => false,
        };

    /// <summary>
    /// The Naga side plates report the keyboard number row (1-0), F12 and
    /// PrintScreen in their default firmware profile. Raw Input carries the
    /// device path, so these keys can be distinguished from the physical
    /// keyboard even though their HID usages are the same.
    /// </summary>
    internal static int FromKeyboard(int virtualKey, int scanCode)
    {
        if (virtualKey is >= 0x31 and <= 0x39)
        {
            return virtualKey - 0x30;
        }

        return virtualKey switch
        {
            0x30 => 10,
            0x7B => 11,
            0x2C => 12,
            0xBD => 11,
            0xBB => 12,
            0 or 0xFF => scanCode switch
            {
                >= 0x02 and <= 0x0B => scanCode - 1,
                0x0C => 11,
                0x0D => 12,
                0x58 => 11,
                0x37 => 12,
                _ => 0,
            },
            _ => 0,
        };
    }
}
