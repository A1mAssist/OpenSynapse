namespace OpenSynapse.Windows.Input;

internal static class RazerNagaSideButtons
{
    private const ushort RazerVendorId = 0x1532;

    // Naga PIDs from the middleware bundles. The product-name check below
    // keeps a new Naga model working when its PID is not listed yet.
    private static readonly HashSet<ushort> ProductIds =
    [
        0x0015, 0x001F, 0x002E, 0x0036, 0x003E, 0x003F, 0x0040, 0x0041,
        0x0050, 0x0053, 0x0067, 0x008D, 0x008F, 0x0090, 0x0092, 0x0093,
        0x0096, 0x00A7, 0x00A8, 0x00B4, 0x00E7, 0x00E8,
    ];

    internal static bool IsNaga(ushort vendorId, ushort productId, string? productName) =>
        vendorId == RazerVendorId &&
        (ProductIds.Contains(productId) ||
         productName?.Contains("Naga", StringComparison.OrdinalIgnoreCase) == true);

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
            0 or 0xFF => scanCode switch
            {
                >= 0x02 and <= 0x0B => scanCode - 1,
                0x58 => 11,
                0x37 => 12,
                _ => 0,
            },
            _ => 0,
        };
    }
}
