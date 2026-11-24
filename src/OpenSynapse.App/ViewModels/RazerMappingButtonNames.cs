using System.Globalization;

namespace OpenSynapse.App.ViewModels;

internal static class RazerMappingButtonNames
{
    internal static string Localize(string name) => name switch
    {
        "Left" => AppStrings.Text("RazerMappingButtonLeft"),
        "Right" => AppStrings.Text("RazerMappingButtonRight"),
        "Middle" => AppStrings.Text("RazerMappingButtonMiddle"),
        "Back" => AppStrings.Text("RazerMappingButtonBack"),
        "Forward" => AppStrings.Text("RazerMappingButtonForward"),
        "Scroll up" => AppStrings.Text("RazerMappingButtonScrollUp"),
        "Scroll down" => AppStrings.Text("RazerMappingButtonScrollDown"),
        "Scroll left" => AppStrings.Text("RazerMappingButtonScrollLeft"),
        "Scroll right" => AppStrings.Text("RazerMappingButtonScrollRight"),
        "DPI up" => AppStrings.Text("RazerMappingButtonDpiUp"),
        "DPI down" => AppStrings.Text("RazerMappingButtonDpiDown"),
        "User select" => AppStrings.Text("RazerMappingButtonUserSelect"),
        "Profile switch" => AppStrings.Text("RazerMappingButtonProfileSwitch"),
        "Sniper" => AppStrings.Text("RazerMappingButtonSniper"),
        "Boss Key" => AppStrings.Text("RazerMappingButtonBossKey"),
        "DPI cycle up" => AppStrings.Text("RazerMappingButtonDpiCycleUp"),
        "DPI cycle down" => AppStrings.Text("RazerMappingButtonDpiCycleDown"),
        "Previous page" => AppStrings.Text("RazerMappingButtonPreviousPage"),
        "Next page" => AppStrings.Text("RazerMappingButtonNextPage"),
        "Volume up" => AppStrings.Text("RazerMappingButtonVolumeUp"),
        "Volume down" => AppStrings.Text("RazerMappingButtonVolumeDown"),
        "Mute" => AppStrings.Text("RazerMappingButtonMute"),
        "Scroll mode switch" => AppStrings.Text("RazerMappingButtonScrollModeSwitch"),
        "Macro Key 1" => AppStrings.Text("RazerMappingMacroKey1"),
        "Macro Key 2" => AppStrings.Text("RazerMappingMacroKey2"),
        _ when name.StartsWith("Button ", StringComparison.Ordinal) &&
            int.TryParse(name.AsSpan(7), NumberStyles.None, CultureInfo.InvariantCulture, out var number) =>
            AppStrings.FormatText("RazerMappingButtonNumber", number),
        _ => name,
    };
}
