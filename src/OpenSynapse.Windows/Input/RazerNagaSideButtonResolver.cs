using OpenSynapse.Core.Devices;
using OpenSynapse.Core.Macros;

namespace OpenSynapse.Windows.Input;

/// <summary>
/// Keeps keyboard output usages separate from the physical mapping ids of Naga
/// side plates. Only the firmware-id reader belongs on the mapping page.
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

    public static MacroInput? ResolveFirmware(ushort productId, byte buttonId)
    {
        var panelSize = GetPanelButtonCount(productId, buttonId);
        return panelSize switch
        {
            12 => Side(productId, buttonId - 63),
            > 0 => Side(productId, buttonId - 79),
            _ => null,
        };
    }

    public static int GetPanelButtonCount(ushort productId, byte buttonId)
    {
        if (buttonId is >= 64 and <= 75)
            return RazerProductAliases.Canonical(productId) == 0x0050 ? 0 : 12;
        var smallPlateSize = productId == 0 ? 7 : RazerNagaSideButtons.SmallPlateButtonCount(productId);
        return buttonId >= 80 && buttonId < 80 + smallPlateSize ? smallPlateSize : 0;
    }

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
