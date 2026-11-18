using System.Globalization;

namespace OpenSynapse.App.ViewModels;

internal static class RazerMappingActionNames
{
    internal static string Localize(string name)
    {
        if (string.IsNullOrEmpty(name)) return name;

        var button = RazerMappingButtonNames.Localize(name);
        if (!string.Equals(button, name, StringComparison.Ordinal)) return button;

        if (name.EndsWith(" (hold)", StringComparison.Ordinal))
        {
            return Localize(name[..^7]) + AppStrings.Text("RazerMappingActionSuffixHold");
        }
        if (name.EndsWith(" (toggle)", StringComparison.Ordinal))
        {
            return Localize(name[..^9]) + AppStrings.Text("RazerMappingActionSuffixToggle");
        }
        if (name.EndsWith(" (sequence)", StringComparison.Ordinal))
        {
            return Localize(name[..^11]) + AppStrings.Text("RazerMappingActionSuffixSequence");
        }
        if (name.StartsWith("Turbo ", StringComparison.Ordinal))
        {
            return $"{AppStrings.Text("RazerMappingActionTurbo")} {Localize(name[6..])}";
        }
        if (name.StartsWith("KP ", StringComparison.Ordinal))
        {
            return AppStrings.FormatText("RazerMappingKeyNumpad", Localize(name[3..]));
        }
        if (name.StartsWith("Left ", StringComparison.Ordinal))
        {
            return $"{AppStrings.Text("RazerMappingKeyLeft")} {Localize(name[5..])}";
        }
        if (name.StartsWith("Right ", StringComparison.Ordinal))
        {
            return $"{AppStrings.Text("RazerMappingKeyRight")} {Localize(name[6..])}";
        }
        if (name.StartsWith("Key ", StringComparison.Ordinal) &&
            int.TryParse(name.AsSpan(4), NumberStyles.None, CultureInfo.InvariantCulture, out var key))
        {
            return AppStrings.FormatText("RazerMappingButtonNumber", key);
        }
        if (name.StartsWith("Macro ", StringComparison.Ordinal) &&
            int.TryParse(name.AsSpan(6), NumberStyles.None, CultureInfo.InvariantCulture, out var macro))
        {
            return AppStrings.FormatText("RazerMappingActionMacroNumber", macro);
        }
        if (name.StartsWith("Function ", StringComparison.Ordinal) &&
            int.TryParse(name.AsSpan(9), NumberStyles.None, CultureInfo.InvariantCulture, out var function))
        {
            return AppStrings.FormatText("RazerMappingActionFunctionNumber", function);
        }
        if (name.StartsWith("Profile ", StringComparison.Ordinal) &&
            int.TryParse(name.AsSpan(8), NumberStyles.None, CultureInfo.InvariantCulture, out var profile))
        {
            return AppStrings.FormatText("RazerMappingActionProfileNumber", profile);
        }
        if (name.StartsWith("Media key ", StringComparison.Ordinal))
        {
            return AppStrings.FormatText("RazerMappingActionMediaKeyNumber", name[10..]);
        }
        if (name.StartsWith("Power key ", StringComparison.Ordinal) &&
            int.TryParse(name.AsSpan(10), NumberStyles.None, CultureInfo.InvariantCulture, out var power))
        {
            return AppStrings.FormatText("RazerMappingActionPowerKeyNumber", power);
        }
        if (name.StartsWith("Scroll mode ", StringComparison.Ordinal) &&
            int.TryParse(name.AsSpan(12), NumberStyles.None, CultureInfo.InvariantCulture, out var scroll))
        {
            return AppStrings.FormatText("RazerMappingActionScrollModeNumber", scroll);
        }
        if (name.StartsWith("Razer key ", StringComparison.Ordinal) &&
            int.TryParse(name.AsSpan(10), NumberStyles.None, CultureInfo.InvariantCulture, out var razer))
        {
            return AppStrings.FormatText("RazerMappingActionRazerKeyNumber", razer);
        }
        if (name.StartsWith("Shortcut ", StringComparison.Ordinal) &&
            int.TryParse(name.AsSpan(9), NumberStyles.None, CultureInfo.InvariantCulture, out var shortcut))
        {
            return AppStrings.FormatText("RazerMappingActionShortcutNumber", shortcut);
        }
        var separator = name.IndexOf('+');
        if (separator > 0 && separator < name.Length - 1)
        {
            return $"{name[..separator]}+{Localize(name[(separator + 1)..])}";
        }

        return name switch
        {
            "Off" => AppStrings.Text("RazerMappingActionOff"),
            "Mouse button" => AppStrings.Text("RazerMappingActionMouseButton"),
            "Keyboard key" => AppStrings.Text("RazerMappingActionKeyboardKey"),
            "DPI clutch" => AppStrings.Text("ViperMappingDpiClutch"),
            "Specific profile" => AppStrings.Text("ViperMappingProfileSpecific"),
            "Double click" => AppStrings.Text("RazerMappingActionDoubleClick"),
            "HyperShift" => AppStrings.Text("ViperMappingHyperShift"),
            "Turbo" => AppStrings.Text("RazerMappingActionTurbo"),
            "Macro" => AppStrings.Text("RazerMappingActionMacro"),
            "Create a macro in the macro editor first" =>
                AppStrings.Text("RazerMappingActionCreateMacroFirst"),
            "Profile up" => AppStrings.Text("ViperMappingProfileUp"),
            "Profile down" => AppStrings.Text("ViperMappingProfileDown"),
            "Profile cycle up" => AppStrings.Text("ViperMappingProfileCycleUp"),
            "Profile cycle down" => AppStrings.Text("ViperMappingProfileCycleDown"),
            "Toggle scroll mode" => AppStrings.Text("RazerMappingActionToggleScrollMode"),
            "Scroll cycle up" => AppStrings.Text("ViperMappingScrollCycleUp"),
            "Scroll cycle down" => AppStrings.Text("ViperMappingScrollCycleDown"),
            "Fn" => AppStrings.Text("ViperMappingRazerFn"),
            "Razer key" => AppStrings.Text("ViperMappingRazerKey"),
            "Game mode" => AppStrings.Text("ViperMappingGameMode"),
            "Macro record" => AppStrings.Text("ViperMappingMacroRecord"),
            "Switch internal display" => AppStrings.Text("ViperMappingSwitchDisplay"),
            "Display brightness up" => AppStrings.Text("ViperMappingDisplayBrightnessUp"),
            "Display brightness down" => AppStrings.Text("ViperMappingDisplayBrightnessDown"),
            "Keyboard backlight up" => AppStrings.Text("ViperMappingKeyboardBacklightUp"),
            "Keyboard backlight down" => AppStrings.Text("ViperMappingKeyboardBacklightDown"),
            "Low power mode" => AppStrings.Text("ViperMappingLowPower"),
            "Macro keys" => AppStrings.Text("ViperMappingMacroKeys"),
            "Print Screen" => AppStrings.Text("RazerMappingKeyPrintScreen"),
            "System sleep" => AppStrings.Text("ViperMappingSystemSleep"),
            "Actuation adjustment" => AppStrings.Text("ViperMappingActuation"),
            "Rapid trigger adjustment" => AppStrings.Text("ViperMappingRapidTrigger"),
            "SnapTap toggle" => AppStrings.Text("ViperMappingSnapTap"),
            "Mic mute" => AppStrings.Text("ViperMappingMicMute"),
            "Screenshot" => AppStrings.Text("ViperMappingScreenshot"),
            "Xbox Game Bar" => AppStrings.Text("ViperMappingXboxGameBar"),
            "Cycle apps" => AppStrings.Text("ViperMappingCycleApps"),
            "Task Manager" => AppStrings.Text("RazerMappingActionTaskManager"),
            "Close app" => AppStrings.Text("RazerMappingActionCloseApp"),
            "Switch apps" => AppStrings.Text("RazerMappingActionSwitchApps"),
            "Cut" => AppStrings.Text("RazerMappingActionCut"),
            "Copy" => AppStrings.Text("RazerMappingActionCopy"),
            "Paste" => AppStrings.Text("RazerMappingActionPaste"),
            "Play / Pause" => AppStrings.Text("MacroMediaPlayPause"),
            "Next track" => AppStrings.Text("MacroMediaNext"),
            "Previous track" => AppStrings.Text("MacroMediaPrevious"),
            "Stop" => AppStrings.Text("MacroMediaStop"),
            "Eject" => AppStrings.Text("ViperMappingMediaEject"),
            "Brightness up" => AppStrings.Text("ViperMappingMediaBrightnessUp"),
            "Brightness down" => AppStrings.Text("ViperMappingMediaBrightnessDown"),
            "Spotlight" => AppStrings.Text("ViperMappingMediaSpotlight"),
            "Power" => AppStrings.Text("ViperMappingPower"),
            "Sleep" => AppStrings.Text("ViperMappingSleep"),
            "Wake" => AppStrings.Text("ViperMappingWake"),
            "Haptic scroll up" => AppStrings.Text("ViperMappingHapticScrollUp"),
            "Haptic scroll down" => AppStrings.Text("ViperMappingHapticScrollDown"),
            "Haptic scroll cycle up" => AppStrings.Text("ViperMappingHapticScrollCycleUp"),
            "Haptic scroll cycle down" => AppStrings.Text("ViperMappingHapticScrollCycleDown"),
            "None" => AppStrings.Text("RazerMappingModifierNone"),
            "Enter" => AppStrings.Text("RazerMappingKeyEnter"),
            "Esc" => AppStrings.Text("RazerMappingKeyEsc"),
            "Backspace" => AppStrings.Text("RazerMappingKeyBackspace"),
            "Tab" => AppStrings.Text("RazerMappingKeyTab"),
            "Space" => AppStrings.Text("RazerMappingKeySpace"),
            "Caps Lock" => AppStrings.Text("RazerMappingKeyCapsLock"),
            "Scroll Lock" => AppStrings.Text("RazerMappingKeyScrollLock"),
            "Pause" => AppStrings.Text("RazerMappingKeyPause"),
            "Insert" => AppStrings.Text("RazerMappingKeyInsert"),
            "Home" => AppStrings.Text("RazerMappingKeyHome"),
            "Page Up" => AppStrings.Text("RazerMappingKeyPageUp"),
            "Delete" => AppStrings.Text("RazerMappingKeyDelete"),
            "End" => AppStrings.Text("RazerMappingKeyEnd"),
            "Page Down" => AppStrings.Text("RazerMappingKeyPageDown"),
            "Right Arrow" => AppStrings.Text("RazerMappingKeyRightArrow"),
            "Left Arrow" => AppStrings.Text("RazerMappingKeyLeftArrow"),
            "Down Arrow" => AppStrings.Text("RazerMappingKeyDownArrow"),
            "Up Arrow" => AppStrings.Text("RazerMappingKeyUpArrow"),
            "Num Lock" => AppStrings.Text("RazerMappingKeyNumLock"),
            _ => name,
        };
    }
}
