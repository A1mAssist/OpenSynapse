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

    private static MacroInput Side(int sideButton) => new()
    {
        Kind = MacroInputKind.NagaSideButton,
        SideButton = sideButton,
    };
}
