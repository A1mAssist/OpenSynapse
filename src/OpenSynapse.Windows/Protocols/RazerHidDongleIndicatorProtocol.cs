using OpenSynapse.Windows.Devices;

namespace OpenSynapse.Windows.Protocols;

/// <summary>Three independent indicator LEDs on the wireless dongle.</summary>
public sealed record RazerHidIndicatorLedConfigurationV2(byte Led1, byte Led2, byte Led3);

internal static class RazerHidDongleIndicatorProtocol
{
    internal static RazerHidRequest Get(RazerHidDeviceDefinition device) =>
        RazerHidStandardProtocol.Create(device,
            "razer_chroma_misc_get_indicator_leds_configuration", 0x03, 0x07, 0x95, new byte[3]);

    internal static RazerHidRequest Set(
        RazerHidDeviceDefinition device,
        RazerHidIndicatorLedConfigurationV2 configuration)
    {
        if (configuration.Led1 > 4 || configuration.Led2 > 4 || configuration.Led3 > 4)
        {
            throw new ArgumentOutOfRangeException(nameof(configuration), "Indicator LED modes must be 0..4.");
        }

        // The bundle allocates 80 bytes, but its protocol header sets data size 3;
        // only the three LED statuses reach the wire.
        return RazerHidStandardProtocol.Create(device,
            "razer_chroma_misc_set_indicator_leds_configuration", 0x03, 0x07, 0x15,
            [configuration.Led1, configuration.Led2, configuration.Led3]);
    }

    internal static RazerHidIndicatorLedConfigurationV2 Parse(ReadOnlySpan<byte> response)
    {
        // The dongle answers this read with data_size 0 on some firmware revisions even
        // though the three modes are present at the normal argument offset. Razer's own
        // reader does not gate on the declared size, so follow the payload, not byte 6.
        var arguments = RazerHidStandardProtocol.RawArguments(response);
        if (arguments[0] > 4 || arguments[1] > 4 || arguments[2] > 4)
        {
            throw new InvalidDataException("Device returned an invalid indicator LED mode.");
        }
        return new RazerHidIndicatorLedConfigurationV2(arguments[0], arguments[1], arguments[2]);
    }
}
