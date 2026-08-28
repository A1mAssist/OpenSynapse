using System.Text;
using OpenSynapse.Windows.Devices;

namespace OpenSynapse.Windows.Protocols;

internal sealed record OpenRazerRequest(string BuilderName, byte[] Report);

internal static class OpenRazerStandardProtocol
{
    internal const byte NoStore = 0x00;
    internal const byte VariableStore = 0x01;
    internal const byte ZeroLed = 0x00;
    internal const byte ScrollWheelLed = 0x01;
    internal const byte LogoLed = 0x04;
    internal const byte BacklightLed = 0x05;
    internal const byte MacroLed = 0x07;
    internal const byte GameLed = 0x08;
    internal const byte RightSideLed = 0x10;
    internal const byte LeftSideLed = 0x11;
    internal const byte ChargingLed = 0x20;
    internal const byte FastChargingLed = 0x21;
    internal const byte FullyChargedLed = 0x22;

    internal static OpenRazerRequest GetLedState(OpenRazerDeviceDefinition device, byte storage, byte ledId) =>
        Create(device, "razer_chroma_standard_get_led_state", 0x03, 0x03, 0x80, [storage, ledId]);

    internal static OpenRazerRequest GetFirmware(OpenRazerDeviceDefinition device) =>
        Create(device, "razer_chroma_standard_get_firmware_version", 0x02, 0x00, 0x81, []);

    internal static OpenRazerRequest GetSerial(OpenRazerDeviceDefinition device) =>
        Create(device, "razer_chroma_standard_get_serial", 0x16, 0x00, 0x82, []);

    internal static OpenRazerRequest GetDeviceMode(OpenRazerDeviceDefinition device) =>
        Create(device, "razer_chroma_standard_get_device_mode", 0x02, 0x00, 0x84, []);

    internal static OpenRazerRequest SetLedState(OpenRazerDeviceDefinition device, byte storage, byte ledId, bool enabled) =>
        Create(device, "razer_chroma_standard_set_led_state", 0x03, 0x03, 0x00,
            [storage, ledId, enabled ? (byte)1 : (byte)0]);

    internal static OpenRazerRequest GetLedEffect(OpenRazerDeviceDefinition device, byte storage, byte ledId) =>
        Create(device, "razer_chroma_standard_get_led_effect", 0x03, 0x03, 0x82, [storage, ledId]);

    internal static OpenRazerRequest SetLedEffect(
        OpenRazerDeviceDefinition device,
        byte storage,
        byte ledId,
        OpenRazerClassicLedEffect effect)
    {
        if (!Enum.IsDefined(effect))
        {
            throw new ArgumentOutOfRangeException(nameof(effect));
        }
        return Create(device, "razer_chroma_standard_set_led_effect", 0x03, 0x03, 0x02,
            [storage, ledId, (byte)effect]);
    }

    internal static OpenRazerRequest GetLedColor(OpenRazerDeviceDefinition device, byte storage, byte ledId) =>
        Create(device, "razer_chroma_standard_get_led_rgb", 0x05, 0x03, 0x81, [storage, ledId]);

    internal static OpenRazerRequest SetLedColor(
        OpenRazerDeviceDefinition device,
        byte storage,
        byte ledId,
        OpenRazerColor color) =>
        Create(device, "razer_chroma_standard_set_led_rgb", 0x05, 0x03, 0x01,
            [storage, ledId, color.Red, color.Green, color.Blue]);

    internal static OpenRazerRequest SetLedBlinking(OpenRazerDeviceDefinition device, byte storage, byte ledId) =>
        Create(device, "razer_chroma_standard_set_led_blinking", 0x04, 0x03, 0x04,
            [storage, ledId, 0x05, 0x05]);

    internal static OpenRazerRequest GetLedBrightness(OpenRazerDeviceDefinition device, byte storage, byte ledId) =>
        Create(device, "razer_chroma_standard_get_led_brightness", 0x03, 0x03, 0x83, [storage, ledId]);

    internal static OpenRazerRequest SetLedBrightness(
        OpenRazerDeviceDefinition device,
        byte storage,
        byte ledId,
        byte brightness) =>
        Create(device, "razer_chroma_standard_set_led_brightness", 0x03, 0x03, 0x03,
            [storage, ledId, brightness]);

    internal static bool ParseLedState(ReadOnlySpan<byte> response, byte storage, byte ledId)
    {
        var arguments = LedArguments(response, 3, storage, ledId);
        return arguments[2] switch
        {
            0 => false,
            1 => true,
            _ => throw new InvalidDataException("OpenRazer returned an invalid LED state."),
        };
    }

    internal static OpenRazerClassicLedEffect ParseLedEffect(
        ReadOnlySpan<byte> response,
        byte storage,
        byte ledId)
    {
        var value = (OpenRazerClassicLedEffect)LedArguments(response, 3, storage, ledId)[2];
        return Enum.IsDefined(value)
            ? value
            : throw new InvalidDataException("OpenRazer returned an unknown classic LED effect.");
    }

    internal static OpenRazerColor ParseLedColor(ReadOnlySpan<byte> response, byte storage, byte ledId)
    {
        var arguments = LedArguments(response, 5, storage, ledId);
        return new OpenRazerColor(arguments[2], arguments[3], arguments[4]);
    }

    internal static string ParseSerial(ReadOnlySpan<byte> response)
    {
        var arguments = Arguments(response, 1);
        var terminator = arguments.IndexOf((byte)0);
        return Encoding.ASCII.GetString(terminator >= 0 ? arguments[..terminator] : arguments).Trim();
    }

    internal static Version ParseFirmware(ReadOnlySpan<byte> response)
    {
        var arguments = Arguments(response, 2);
        return new Version(arguments[0], arguments[1]);
    }

    internal static ReadOnlySpan<byte> Arguments(ReadOnlySpan<byte> response, byte minimumLength)
    {
        if (response.Length != RazerFeatureReport.Length || !RazerFeatureReport.IsAcceptedStatus(response[1]) ||
            response[6] < minimumLength || response[6] > 80 ||
            response[89] != RazerFeatureReport.CalculateCrc(response))
        {
            throw new InvalidDataException("OpenRazer response is invalid or unsuccessful.");
        }
        return response.Slice(RazerFeatureReport.ArgumentsOffset, response[6]);
    }

    private static ReadOnlySpan<byte> LedArguments(
        ReadOnlySpan<byte> response,
        byte minimumLength,
        byte storage,
        byte ledId)
    {
        var arguments = Arguments(response, minimumLength);
        if (arguments[0] != storage || arguments[1] != ledId)
        {
            throw new InvalidDataException("OpenRazer LED response does not match the requested storage and zone.");
        }
        return arguments;
    }

    internal static OpenRazerRequest Create(
        OpenRazerDeviceDefinition device,
        string builderName,
        byte dataSize,
        byte commandClass,
        byte commandId,
        ReadOnlySpan<byte> arguments,
        bool allowArgumentsBeyondDeclaredSize = false)
    {
        var transactionId = device.GetRequiredTransaction(builderName);
        var report = allowArgumentsBeyondDeclaredSize
            ? RazerFeatureReport.CreateRequestWithDeclaredSize(
                transactionId, dataSize, commandClass, commandId, arguments)
            : RazerFeatureReport.CreateRequest(
                transactionId, dataSize, commandClass, commandId, arguments);
        return new OpenRazerRequest(builderName, report);
    }
}

public readonly record struct OpenRazerColor(byte Red, byte Green, byte Blue);

public enum OpenRazerStorage : byte
{
    Temporary = 0x00,
    Persistent = 0x01,
}

public enum OpenRazerLedZone : byte
{
    All = 0x00,
    ScrollWheel = 0x01,
    Battery = 0x03,
    Logo = 0x04,
    Backlight = 0x05,
    Macro = 0x07,
    Game = 0x08,
    ProfileRed = 0x0C,
    ProfileGreen = 0x0D,
    ProfileBlue = 0x0E,
    RightSide = 0x10,
    LeftSide = 0x11,
    Charging = 0x20,
    FastCharging = 0x21,
    FullyCharged = 0x22,
}

public enum OpenRazerClassicLedEffect : byte
{
    Static = 0x00,
    Blinking = 0x01,
    Breathing = 0x02,
    Spectrum = 0x04,
}

public enum OpenRazerLightingEffect
{
    Off,
    Static,
    Spectrum,
    Wave,
    Reactive,
    Blinking,
    BreathingRandom,
    BreathingSingle,
    BreathingDual,
    BreathingTriple,
    StarlightRandom,
    StarlightSingle,
    StarlightDual,
    Wheel,
    Custom,
}

public sealed record OpenRazerLightingSettings(
    OpenRazerLightingEffect Effect,
    byte Speed = 2,
    byte Direction = 1,
    OpenRazerColor Primary = default,
    OpenRazerColor Secondary = default,
    OpenRazerStorage? Storage = null,
    OpenRazerLedZone? Zone = null,
    OpenRazerColor Tertiary = default);
