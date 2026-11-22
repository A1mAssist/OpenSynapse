using OpenSynapse.Core.Macros;

namespace OpenSynapse.Windows.Input;

/// <summary>
/// Naga side plates expose their default layout as the keyboard number row
/// (1-0) plus '-' and '='. The mapping page reports those HID usages, while
/// the macro runtime names the same physical inputs 1-12.
/// </summary>
public static class RazerNagaSideButtonResolver
{
    public static MacroInput? Resolve(byte hidUsage) => hidUsage switch
    {
        >= 0x1E and <= 0x27 => Side(hidUsage - 0x1D),
        0x2D => Side(11),
        0x2E => Side(12),
        _ => null,
    };

    /// <summary>
    /// The mapping page reports the firmware ids the side plates declare in the
    /// product bundle: SPAD_NUM_1..12 are 64..75 and HPAD_NUM_1..7 are 80..86.
    /// HID usages share the same numeric range, so firmware ids get their own reader.
    /// </summary>
    public static MacroInput? ResolveFirmware(byte buttonId) => buttonId switch
    {
        >= 64 and <= 75 => Side(buttonId - 63),
        >= 80 and <= 86 => Side(buttonId - 79),
        _ => null,
    };

    public static bool IsNagaProduct(ushort productId, string? productName) =>
        RazerNagaSideButtons.IsNaga(0x1532, productId, productName);

    private static MacroInput Side(int sideButton) => new()
    {
        Kind = MacroInputKind.NagaSideButton,
        SideButton = sideButton,
    };
}
