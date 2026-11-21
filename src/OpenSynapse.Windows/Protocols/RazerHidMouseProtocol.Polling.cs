using OpenSynapse.Windows.Devices;

namespace OpenSynapse.Windows.Protocols;

internal static partial class RazerHidMouseProtocol
{
    /// <summary>
    /// The command family the catalog prefers before the device has answered either
    /// family. Get and Set must agree, otherwise a device that only declares the set
    /// sequence starts on the wrong family.
    /// </summary>
    internal static bool PrefersHighRate(RazerHidDeviceDefinition device) =>
        device.Transactions.ContainsKey("razer_chroma_misc_get_polling_rate2") ||
        device.CommandSequences.ContainsKey("razer_chroma_misc_set_polling_rate2");

    internal static RazerHidRequest GetPollingRate(
        RazerHidDeviceDefinition device,
        byte profileId = 0x00,
        bool? useHighRate = null)
    {
        var highRate = useHighRate ?? PrefersHighRate(device);
        if (highRate)
        {
            // The middleware bundle pins profile 1 as the first builder argument for the
            // hyper-polling siblings (for example 1532:00C2). The same prefix is applied
            // to the dongle halves that inherit the family, so this matches what Synapse
            // sends; the active-profile read cannot override a declared fixed argument.
            var profile = device.GetFixedArgumentOrNull("razer_chroma_misc_get_polling_rate2", 0);
            var arguments = profile is byte value ? new[] { value } : new[] { profileId };
            return RazerHidStandardProtocol.Create(device,
                "razer_chroma_misc_get_polling_rate2",
                // The middleware declares the USB high speed polling read as two
                // bytes in every product bundle that ships it.
                device.GetCommandSize("razer_chroma_misc_get_polling_rate2", 0x02),
                0x00, 0xC0, arguments);
        }
        return RazerHidStandardProtocol.Create(device,
            "razer_chroma_misc_get_polling_rate", 0x01, 0x00, 0x85, []);
    }

    internal static IReadOnlyList<RazerHidRequest> SetPollingRate(
        RazerHidDeviceDefinition device,
        int hertz,
        byte profileId = 0x00,
        bool? useHighRate = null)
    {
        var highRate = useHighRate ?? PrefersHighRate(device);
        // A HyperPolling dock accepts rates that the wireless identity's stock ladder
        // does not list. The encoder still rejects steps that exist in neither table.
        if (device.PollingRates.Count > 0 &&
            !device.PollingRates.Contains(hertz) &&
            !(highRate && hertz > 1_000))
        {
            throw new ArgumentOutOfRangeException(nameof(hertz), "Polling rate is not declared for this device.");
        }
        if (highRate && device.GetFixedArgumentOrNull("razer_chroma_misc_set_polling_rate2", 0) is byte profile)
        {
            return [RazerHidStandardProtocol.Create(device,
                "razer_chroma_misc_set_polling_rate2",
                device.GetCommandSize("razer_chroma_misc_set_polling_rate2", 0x02),
                0x00, 0x40, [profile, EncodeHighPollingRate(hertz)])];
        }
        if (highRate && device.CommandSequences.TryGetValue("razer_chroma_misc_set_polling_rate2", out var sequence))
        {
            var rateCode = EncodeHighPollingRate(hertz);
            return sequence.Select(step => new RazerHidRequest(
                "razer_chroma_misc_set_polling_rate2",
                RazerFeatureReport.CreateRequest(step.TransactionId, 0x02, 0x00, 0x40,
                    [step.FixedArgument0, rateCode]))).ToArray();
        }
        if (highRate)
        {
            return [RazerHidStandardProtocol.Create(device,
                "razer_chroma_misc_set_polling_rate2", 0x02, 0x00, 0x40,
                [profileId, EncodeHighPollingRate(hertz)])];
        }
        return [RazerHidStandardProtocol.Create(device,
            "razer_chroma_misc_set_polling_rate", 0x01, 0x00, 0x05,
            [EncodePollingRate(hertz)])];
    }
}
