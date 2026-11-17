namespace OpenSynapse.Windows.Audio;

internal static partial class ThxHeadsetAdmission
{
    private static void AddConsoleProfiles(IDictionary<ushort, ThxHeadsetProfile> profiles)
    {
        AddV4(profiles, 0x101C, "Razer BlackShark V3 Pro (PlayStation)", V4Dll113Name, V4Dll113Uri,
            V4Dll113Sha256, "razer_blacksharkv3pro_ps", "headphones_game",
            "https://app-assets.razer.com/files/synapse/devices/4124/THXAPO_razer_blacksharkv3pro_ps_v4.1.28.1058-75fb4b2-i1881n_MSSIGN-e6a07c7f.c70f783e.exe",
            "https://app-assets.razer.com/files/synapse/devices/4124/properties-updater/thx_razer_blacksharkv3pro_ps_properties_updater-v4.1.28-f19f37d9-Razer-4.1.28-prod.exe",
            "https://app-assets.razer.com/files/synapse/devices/4124/714-patcher/thx_razer_blacksharkv3pro_ps_v4.2.38-a844df5a-714_patcher_config_updater-v14-20260420.exe",
            installerLength: 51476624,
            installerSha256: "1F98E75CB6191C5C1DFD0B8434C70F48AFA9501DDCD165C57E4D1D92CB36E3BE");
        // 101B is the PS Pro 2.4 GHz dongle; it reports its own pid but uses the same package.
        AddV4Alias(profiles, 0x101B, 0x101C);
    }

    private static void AddV4Alias(
        IDictionary<ushort, ThxHeadsetProfile> profiles,
        ushort productId,
        ushort sourceProductId) =>
        profiles[productId] = profiles[sourceProductId] with { ProductId = productId };
}
