using System.Text;
using OpenSynapse.Windows.Devices;

namespace OpenSynapse.Windows.Protocols;

internal sealed record RazerHidRequest(string BuilderName, byte[] Report);

internal static class RazerHidStandardProtocol
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

    internal static RazerHidRequest GetLedState(RazerHidDeviceDefinition device, byte storage, byte ledId) =>
        Create(device, "razer_chroma_standard_get_led_state", 0x03, 0x03, 0x80, [storage, ledId]);

    // Synapse 2 Info firmware reads use 00/87 with a four-byte payload for
    // dockType 1, and 00/93 for dockType 0. Modern RazerHid devices keep the
    // existing 00/81 two-byte read. The three dockType-0 product ids are listed
    // in RazerHidDeviceCatalog.LegacyReadProbes.cs.
    internal static RazerHidRequest GetFirmware(RazerHidDeviceDefinition device) =>
        Create(device, "razer_chroma_standard_get_firmware_version",
            device.UsesLegacyInfo ? (byte)0x04 : (byte)0x02, 0x00,
            FirmwareCommand(device, fallback: false), []);

    /// <summary>
    /// Synapse 2 lists 00:93/00:87 ahead of 00:91/00:81 for Info firmware. The
    /// alternate is used only after the first read fails, so devices that answer
    /// the primary read still take the fast path. Modern devices keep the
    /// existing 00/81 primary and 00/87 fallback pair.
    /// </summary>
    internal static RazerHidRequest GetFirmwareFallback(RazerHidDeviceDefinition device) =>
        Create(device, "razer_chroma_standard_get_firmware_version", 0x02, 0x00,
            FirmwareCommand(device, fallback: true), []);

    private static byte FirmwareCommand(RazerHidDeviceDefinition device, bool fallback)
    {
        var dockZero = RazerHidDeviceCatalog.UsesLegacyDockTypeZero(device.ProductId);
        if (device.UsesLegacyInfo)
        {
            return (dockZero, fallback) switch
            {
                (true, false) => 0x93,
                (true, true) => 0x91,
                (false, false) => 0x87,
                _ => 0x81,
            };
        }

        return fallback ? (byte)(dockZero ? 0x93 : 0x87) : (byte)0x81;
    }

    internal static RazerHidRequest GetSerial(RazerHidDeviceDefinition device) =>
        Create(device, "razer_chroma_standard_get_serial", 0x16, 0x00,
            RazerHidDeviceCatalog.UsesLegacyDockTypeZero(device.ProductId) ? (byte)0x92 : (byte)0x82, []);

    internal const byte NormalDeviceMode = 0x00;
    internal const byte DriverDeviceMode = 0x03;

    internal static RazerHidRequest GetDeviceMode(RazerHidDeviceDefinition device) =>
        Create(device, "razer_chroma_standard_get_device_mode", 0x02, 0x00, 0x84, []);

    /// <summary>
    /// Writes device mode back to driver mode. RazerHid sends mode 0x03 with a zero parameter
    /// for every wireless mouse it drives. A wireless mouse left in normal mode never joins
    /// the 2.4 GHz link, so this restores it.
    /// </summary>
    internal static RazerHidRequest SetDriverDeviceMode(RazerHidDeviceDefinition device) =>
        Create(device, "razer_chroma_standard_set_device_mode", 0x02, 0x00, 0x04,
            [DriverDeviceMode, NoStore]);

    internal static byte ParseDeviceMode(ReadOnlySpan<byte> response) => Arguments(response, 2)[0];

    // Synapse 2 P1 encodes DPI as (value - 100) / 25 in a single byte; the
    // modern byte encoding uses a 0..6750 scale. Same frame, different scale.
    // P1 answers 04/81 with the two encoded bytes at argument offsets 0/1. The
    // third argument byte is not decoded: the handoff leaves its meaning unconfirmed.
    internal static (int X, int Y) ParseP1Dpi(ReadOnlySpan<byte> response)
    {
        var arguments = Arguments(response, 2);
        return (arguments[0] * 25 + 100, arguments[1] * 25 + 100);
    }

    internal static RazerHidRequest SetLedState(RazerHidDeviceDefinition device, byte storage, byte ledId, bool enabled) =>
        Create(device, "razer_chroma_standard_set_led_state", 0x03, 0x03, 0x00,
            [storage, ledId, enabled ? (byte)1 : (byte)0]);

    internal static RazerHidRequest GetLedEffect(RazerHidDeviceDefinition device, byte storage, byte ledId) =>
        Create(device, "razer_chroma_standard_get_led_effect", 0x03, 0x03, 0x82, [storage, ledId]);

    internal static RazerHidRequest SetLedEffect(
        RazerHidDeviceDefinition device,
        byte storage,
        byte ledId,
        RazerHidClassicLedEffect effect)
    {
        if (!Enum.IsDefined(effect))
        {
            throw new ArgumentOutOfRangeException(nameof(effect));
        }
        return Create(device, "razer_chroma_standard_set_led_effect", 0x03, 0x03, 0x02,
            [storage, ledId, (byte)effect]);
    }

    internal static RazerHidRequest GetLedColor(RazerHidDeviceDefinition device, byte storage, byte ledId) =>
        Create(device, "razer_chroma_standard_get_led_rgb", 0x05, 0x03, 0x81, [storage, ledId]);

    internal static RazerHidRequest SetLedColor(
        RazerHidDeviceDefinition device,
        byte storage,
        byte ledId,
        RazerHidColor color) =>
        Create(device, "razer_chroma_standard_set_led_rgb", 0x05, 0x03, 0x01,
            [storage, ledId, color.Red, color.Green, color.Blue]);

    internal static RazerHidRequest SetLedBlinking(RazerHidDeviceDefinition device, byte storage, byte ledId) =>
        Create(device, "razer_chroma_standard_set_led_blinking", 0x04, 0x03, 0x04,
            [storage, ledId, 0x05, 0x05]);

    internal static RazerHidRequest GetLedBrightness(RazerHidDeviceDefinition device, byte storage, byte ledId) =>
        Create(device, "razer_chroma_standard_get_led_brightness", 0x03, 0x03, 0x83, [storage, ledId]);

    internal static RazerHidRequest SetLedBrightness(
        RazerHidDeviceDefinition device,
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
            _ => throw new InvalidDataException("RazerHid returned an invalid LED state."),
        };
    }

    internal static RazerHidClassicLedEffect ParseLedEffect(
        ReadOnlySpan<byte> response,
        byte storage,
        byte ledId)
    {
        var value = (RazerHidClassicLedEffect)LedArguments(response, 3, storage, ledId)[2];
        return Enum.IsDefined(value)
            ? value
            : throw new InvalidDataException("RazerHid returned an unknown classic LED effect.");
    }

    internal static RazerHidColor ParseLedColor(ReadOnlySpan<byte> response, byte storage, byte ledId)
    {
        var arguments = LedArguments(response, 5, storage, ledId);
        return new RazerHidColor(arguments[2], arguments[3], arguments[4]);
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
            !RazerFeatureReport.IsAcceptedChecksum(response))
        {
            throw new InvalidDataException("RazerHid response is invalid or unsuccessful.");
        }
        return response.Slice(RazerFeatureReport.ArgumentsOffset, response[6]);
    }

    /// <summary>
    /// Validates a response and returns its whole argument area. RazerHid reads some
    /// responses at a fixed argument index without trusting the declared data_size, so a
    /// caller that follows upstream must not be limited to the declared length.
    /// </summary>
    internal static ReadOnlySpan<byte> RawArguments(ReadOnlySpan<byte> response)
    {
        if (response.Length != RazerFeatureReport.Length || !RazerFeatureReport.IsAcceptedStatus(response[1]) ||
            response[6] > 80 ||
            !RazerFeatureReport.IsAcceptedChecksum(response))
        {
            throw new InvalidDataException("RazerHid response is invalid or unsuccessful.");
        }
        return response.Slice(RazerFeatureReport.ArgumentsOffset, 80);
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
            throw new InvalidDataException("RazerHid LED response does not match the requested storage and zone.");
        }
        return arguments;
    }

    internal static RazerHidRequest Create(
        RazerHidDeviceDefinition device,
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
        return new RazerHidRequest(builderName, report);
    }
}

public readonly record struct RazerHidColor(byte Red, byte Green, byte Blue);

public enum RazerHidStorage : byte
{
    Temporary = 0x00,
    Persistent = 0x01,
}

public enum RazerHidLedZone : byte
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

public enum RazerHidClassicLedEffect : byte
{
    Static = 0x00,
    Blinking = 0x01,
    Breathing = 0x02,
    Spectrum = 0x04,
}

public enum RazerHidLightingEffect
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
    SoftwareSpectrum,
    SoftwareWave,
    SoftwareFire,
    SoftwareAudioMeter,
    SoftwareWheel,
    SoftwareGradient,
    SoftwareRipple,
    SoftwareTidal,
    SoftwareRadiate,
    Fire,
    AudioMeter,
}

internal static class RazerHidLightingEffects
{
    internal static bool IsSoftware(RazerHidLightingEffect effect) => effect is
        RazerHidLightingEffect.SoftwareSpectrum or
        RazerHidLightingEffect.SoftwareWave or
        RazerHidLightingEffect.SoftwareFire or
        RazerHidLightingEffect.SoftwareAudioMeter or
        RazerHidLightingEffect.SoftwareWheel or
        RazerHidLightingEffect.SoftwareGradient or
        RazerHidLightingEffect.SoftwareRipple or
        RazerHidLightingEffect.SoftwareTidal or
        RazerHidLightingEffect.SoftwareRadiate;
}

public sealed record RazerHidLightingSettings(
    RazerHidLightingEffect Effect,
    byte Speed = 2,
    byte Direction = 1,
    RazerHidColor Primary = default,
    RazerHidColor Secondary = default,
    RazerHidStorage? Storage = null,
    RazerHidLedZone? Zone = null,
    RazerHidColor Tertiary = default);
