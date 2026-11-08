using OpenSynapse.Core.Devices;
using OpenSynapse.Core.Profiles;

namespace OpenSynapse.App.ViewModels;

/// <summary>
/// Registers the settings the RazerHid device page now persists. The source
/// audit checks the attributes; this registry gives the runtime its descriptors.
/// </summary>
internal static partial class ProfileSettingsRegistration
{
    private static int _registered;

    public static void EnsureRegistered()
    {
        if (Interlocked.Exchange(ref _registered, 1) != 0)
        {
            return;
        }

        RegisterPeripheralSettings();
        RegisterBladeSettings();
        RegisterAudioSettings();
        RegisterOtherSettings();
        RegisterMappingSettings();
    }

    private static void RegisterPeripheralSettings()
    {

        ProfileSettingCatalog.Register(new("input.polling-rate", DeviceCategory.Unknown, true));
        ProfileSettingCatalog.Register(new("mouse.dpi", DeviceCategory.Mouse, true));
        ProfileSettingCatalog.Register(new("mouse.dpi-stages", DeviceCategory.Mouse, true));
        ProfileSettingCatalog.Register(new("mouse.idle-timeout", DeviceCategory.Mouse, true));
        ProfileSettingCatalog.Register(new("mouse.low-battery", DeviceCategory.Mouse, true));
        ProfileSettingCatalog.Register(new("mouse.auto-sleep", DeviceCategory.Mouse, true));
        ProfileSettingCatalog.Register(new("mouse.charge-limit", DeviceCategory.Mouse, true));
        ProfileSettingCatalog.Register(new("mouse.scroll-preset", DeviceCategory.Mouse, true));
        ProfileSettingCatalog.Register(new("mouse.scroll-mode", DeviceCategory.Mouse, true));
        ProfileSettingCatalog.Register(new("mouse.scroll-free-notched", DeviceCategory.Mouse, true));
        ProfileSettingCatalog.Register(new("mouse.scroll-acceleration", DeviceCategory.Mouse, true));
        ProfileSettingCatalog.Register(new("mouse.scroll-acceleration-level", DeviceCategory.Mouse, true));
        ProfileSettingCatalog.Register(new("mouse.smart-reel", DeviceCategory.Mouse, true));
        ProfileSettingCatalog.Register(new("mouse.smart-reel-threshold", DeviceCategory.Mouse, true));
        ProfileSettingCatalog.Register(new("mouse.onboard-profile", DeviceCategory.Mouse, true));
        ProfileSettingCatalog.Register(new("mouse.haptic-scroll-level", DeviceCategory.Mouse, true));
        ProfileSettingCatalog.Register(new("keyboard.keyswitch", DeviceCategory.Keyboard, false));
        ProfileSettingCatalog.Register(new("keyboard.fn-primary", DeviceCategory.Keyboard, false));
        ProfileSettingCatalog.Register(new("keyboard.snap-tap", DeviceCategory.Keyboard, true));
        ProfileSettingCatalog.Register(new("keyboard.analog-key", DeviceCategory.Keyboard, true));
        ProfileSettingCatalog.Register(new("keyboard.oled", DeviceCategory.Keyboard, false));
        ProfileSettingCatalog.Register(new("keyboard.knob", DeviceCategory.Keyboard, false));
        ProfileSettingCatalog.Register(new("keyboard.wrist-rest", DeviceCategory.Keyboard, false));
        ProfileSettingCatalog.Register(new("keyboard.debounce", DeviceCategory.Keyboard, false));
    }

    static partial void RegisterBladeSettings();
    static partial void RegisterAudioSettings();
    static partial void RegisterOtherSettings();
    static partial void RegisterMappingSettings();
}
