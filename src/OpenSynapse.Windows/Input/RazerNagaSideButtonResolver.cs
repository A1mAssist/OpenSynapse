using OpenSynapse.Core.Devices;
using OpenSynapse.Core.Macros;

namespace OpenSynapse.Windows.Input;

/// <summary>
/// Naga side plates expose their default layout as the keyboard number row
/// (1-0) plus '-' and '='. The mapping page reports those HID usages, while
/// the macro runtime names the same physical inputs 1-12.
/// </summary>
public static class RazerNagaSideButtonResolver
{
    // Display callers (button names) have no product id; identity stays unset.
    public static MacroInput? Resolve(byte hidUsage) => Resolve(0, hidUsage);

    public static MacroInput? Resolve(ushort productId, byte hidUsage) => hidUsage switch
    {
        >= 0x1E and <= 0x27 => Side(productId, hidUsage - 0x1D),
        0x2D => Side(productId, 11),
        0x2E => Side(productId, 12),
        _ => null,
    };

    /// <summary>
    /// The mapping page reports the firmware ids the side plates declare in the
    /// product bundle: SPAD_NUM_1..12 are 64..75 and HPAD_NUM_1..7 are 80..86.
    /// HID usages share the same numeric range, so firmware ids get their own reader.
    /// </summary>
    public static MacroInput? ResolveFirmware(byte buttonId) => ResolveFirmware(0, buttonId);

    public static MacroInput? ResolveFirmware(ushort productId, byte buttonId) => buttonId switch
    {
        >= 64 and <= 75 => Side(productId, buttonId - 63),
        >= 80 and <= 86 => Side(productId, buttonId - 79),
        _ => null,
    };

    public static bool IsNagaProduct(ushort productId, string? productName) =>
        RazerNagaSideButtons.IsNaga(0x1532, productId, productName);

    private static MacroInput Side(ushort productId, int sideButton) => new()
    {
        Kind = MacroInputKind.NagaSideButton,
        // Wireless identities (00B5) must match the wired id the page binds (00B4).
        DeviceId = RazerProductAliases.Canonical(productId),
        SideButton = sideButton,
    };
}
