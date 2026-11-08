using OpenSynapse.Core.Devices;
using OpenSynapse.Core.Profiles;

namespace OpenSynapse.App.ViewModels;

internal static partial class ProfileSettingsRegistration
{
    static partial void RegisterBladeSettings()
    {
        ProfileSettingCatalog.Register(new("blade.startup-animation", DeviceCategory.Laptop, true));
        ProfileSettingCatalog.Register(new("blade.auto-sleep", DeviceCategory.Laptop, true));
        ProfileSettingCatalog.Register(new("blade.time-to-dim", DeviceCategory.Laptop, true));
        ProfileSettingCatalog.Register(new("blade.indicator-led", DeviceCategory.Laptop, true));
        ProfileSettingCatalog.Register(new("blade.gpu-card-detection", DeviceCategory.Laptop, true));
        ProfileSettingCatalog.Register(new("blade.native-display-mode", DeviceCategory.Laptop, true));
        ProfileSettingCatalog.Register(new("blade.local-dimming", DeviceCategory.Laptop, true));
        ProfileSettingCatalog.Register(new("blade.cpu-power-boost", DeviceCategory.Laptop, true));
        ProfileSettingCatalog.Register(new("blade.display-brightness", DeviceCategory.Laptop, true));
        ProfileSettingCatalog.Register(new("blade.cpu-power-limit", DeviceCategory.Laptop, true));
        ProfileSettingCatalog.Register(new("blade.cpu-curve", DeviceCategory.Laptop, true));
        ProfileSettingCatalog.Register(new("blade.gpu-clock-offset", DeviceCategory.Laptop, true));
    }

    static partial void RegisterAudioSettings()
    {
        ProfileSettingCatalog.Register(new("headset.advanced", DeviceCategory.Headset, true));
        ProfileSettingCatalog.Register(new("headset.anc", DeviceCategory.Headset, true));
        ProfileSettingCatalog.Register(new("headset.auto-pause", DeviceCategory.Headset, true));
        ProfileSettingCatalog.Register(new("headset.auto-power-off", DeviceCategory.Headset, true));
        ProfileSettingCatalog.Register(new("headset.bass", DeviceCategory.Headset, true));
        ProfileSettingCatalog.Register(new("headset.chroma-brightness", DeviceCategory.Headset, true));
        ProfileSettingCatalog.Register(new("headset.chroma-effect", DeviceCategory.Headset, true));
        ProfileSettingCatalog.Register(new("headset.custom-eq", DeviceCategory.Headset, true));
        ProfileSettingCatalog.Register(new("headset.dnd", DeviceCategory.Headset, true));
        ProfileSettingCatalog.Register(new("headset.dolby", DeviceCategory.Headset, true));
        ProfileSettingCatalog.Register(new("headset.eq-preset", DeviceCategory.Headset, true));
        ProfileSettingCatalog.Register(new("headset.eq.bands", DeviceCategory.Headset, true));
        ProfileSettingCatalog.Register(new("headset.eq.preset", DeviceCategory.Headset, true));
        ProfileSettingCatalog.Register(new("headset.game-chat", DeviceCategory.Headset, true));
        ProfileSettingCatalog.Register(new("headset.game-mode", DeviceCategory.Headset, true));
        ProfileSettingCatalog.Register(new("headset.gesture-control", DeviceCategory.Headset, true));
        ProfileSettingCatalog.Register(new("headset.haptics", DeviceCategory.Headset, true));
        ProfileSettingCatalog.Register(new("headset.lighting.brightness", DeviceCategory.Headset, true));
        ProfileSettingCatalog.Register(new("headset.lighting.effect", DeviceCategory.Headset, true));
        ProfileSettingCatalog.Register(new("headset.mic-compressor", DeviceCategory.Headset, true));
        ProfileSettingCatalog.Register(new("headset.mic-custom-eq", DeviceCategory.Headset, true));
        ProfileSettingCatalog.Register(new("headset.mic-eq-preset", DeviceCategory.Headset, true));
        ProfileSettingCatalog.Register(new("headset.mic-monitoring", DeviceCategory.Headset, true));
        ProfileSettingCatalog.Register(new("headset.mic-processing", DeviceCategory.Headset, true));
        ProfileSettingCatalog.Register(new("headset.mic.de-esser", DeviceCategory.Headset, true));
        ProfileSettingCatalog.Register(new("headset.mic.eq-bands", DeviceCategory.Headset, true));
        ProfileSettingCatalog.Register(new("headset.mic.limiter", DeviceCategory.Headset, true));
        ProfileSettingCatalog.Register(new("headset.mic.reverb", DeviceCategory.Headset, true));
        ProfileSettingCatalog.Register(new("headset.microphone", DeviceCategory.Headset, true));
        ProfileSettingCatalog.Register(new("headset.night-mode", DeviceCategory.Headset, true));
        ProfileSettingCatalog.Register(new("headset.noise-suppression", DeviceCategory.Headset, true));
        ProfileSettingCatalog.Register(new("headset.parametric-eq", DeviceCategory.Headset, true));
        ProfileSettingCatalog.Register(new("headset.sidetone", DeviceCategory.Headset, true));
        ProfileSettingCatalog.Register(new("headset.source", DeviceCategory.Headset, true));
        ProfileSettingCatalog.Register(new("headset.thx", DeviceCategory.Headset, true));
        ProfileSettingCatalog.Register(new("headset.thx.bands", DeviceCategory.Headset, true));
        ProfileSettingCatalog.Register(new("headset.thx.bass-boost", DeviceCategory.Headset, true));
        ProfileSettingCatalog.Register(new("headset.thx.normalization", DeviceCategory.Headset, true));
        ProfileSettingCatalog.Register(new("headset.thx.preset", DeviceCategory.Headset, true));
        ProfileSettingCatalog.Register(new("headset.thx.sidetone", DeviceCategory.Headset, true));
        ProfileSettingCatalog.Register(new("headset.thx.spatial", DeviceCategory.Headset, true));
        ProfileSettingCatalog.Register(new("headset.thx.voice-clarity", DeviceCategory.Headset, true));
        ProfileSettingCatalog.Register(new("headset.voice-prompts", DeviceCategory.Headset, true));
        ProfileSettingCatalog.Register(new("headset.volume", DeviceCategory.Headset, true));
    }

    static partial void RegisterOtherSettings()
    {
        ProfileSettingCatalog.Register(new("coolingpad.fan-curve", DeviceCategory.CoolingPad, true));
        ProfileSettingCatalog.Register(new("coolingpad.fan-mode", DeviceCategory.CoolingPad, false));
        ProfileSettingCatalog.Register(new("coolingpad.fan-target", DeviceCategory.CoolingPad, false));
        ProfileSettingCatalog.Register(new("coolingpad.lighting", DeviceCategory.CoolingPad, false));
        ProfileSettingCatalog.Register(new("gamepad.dpad", DeviceCategory.Gamepad, false));
        ProfileSettingCatalog.Register(new("gamepad.buttons", DeviceCategory.Gamepad, false));
        ProfileSettingCatalog.Register(new("gamepad.lighting", DeviceCategory.Gamepad, false));
        ProfileSettingCatalog.Register(new("gamepad.polling-rate", DeviceCategory.Gamepad, false));
        ProfileSettingCatalog.Register(new("gamepad.stick-deadzone", DeviceCategory.Gamepad, false));
        ProfileSettingCatalog.Register(new("gamepad.stick-sensitivity", DeviceCategory.Gamepad, false));
        ProfileSettingCatalog.Register(new("gamepad.stick-circularity", DeviceCategory.Gamepad, false));
        ProfileSettingCatalog.Register(new("gamepad.stick-prevent-double-deadzone", DeviceCategory.Gamepad, false));
        ProfileSettingCatalog.Register(new("gamepad.trigger-mode", DeviceCategory.Gamepad, false));
        ProfileSettingCatalog.Register(new("gamepad.trigger", DeviceCategory.Gamepad, false));
        ProfileSettingCatalog.Register(new("gamepad.vibration", DeviceCategory.Gamepad, false));
        ProfileSettingCatalog.Register(new("chroma.argb-frame", DeviceCategory.Lighting, false));
        ProfileSettingCatalog.Register(new("chroma.brightness", DeviceCategory.Lighting, false));
        ProfileSettingCatalog.Register(new("chroma.effect", DeviceCategory.Lighting, false));
        ProfileSettingCatalog.Register(new("mixer.brightness", DeviceCategory.Accessory, false));
        ProfileSettingCatalog.Register(new("mixer.channel-mutes", DeviceCategory.Accessory, false));
        ProfileSettingCatalog.Register(new("mixer.mic-mute", DeviceCategory.Accessory, false));
        ProfileSettingCatalog.Register(new("kiyo.white-balance", DeviceCategory.Accessory, false));
    }

    static partial void RegisterMappingSettings()
    {
        // Button maps live inside the mouse and keyboard sections, so they share
        // those categories and are never power-scoped.
        ProfileSettingCatalog.Register(new("mouse.mapping", DeviceCategory.Mouse, false));
        ProfileSettingCatalog.Register(new("keyboard.mapping", DeviceCategory.Keyboard, false));
    }
}
