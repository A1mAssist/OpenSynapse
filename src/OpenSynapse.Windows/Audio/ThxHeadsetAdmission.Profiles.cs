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
        AddV4(profiles, 0x0A4E, "Razer BlackShark V3 Pro (Xbox)", V4Dll113Name, V4Dll113Uri,
            V4Dll113Sha256, "razer_blacksharkv3pro_xbox", "headphones_game",
            "https://app-assets.razer.com/files/synapse/devices/2638/THXAPO_razer_blacksharkv3pro_xbox_v4.1.28.1059-75fb4b2-i1884n_MSSIGN-e6a07c7f.14527399.exe",
            "https://app-assets.razer.com/files/synapse/devices/2638/properties-updater/thx_razer_blacksharkv3pro_xbox_properties_updater-v4.1.28-f19f37d9-Razer-4.1.28-prod.exe",
            "https://app-assets.razer.com/files/synapse/devices/2638/714-patcher/thx_razer_blacksharkv3pro_xbox_v4.2.38-a844df5a-714_patcher_config_updater-v14-20260420.exe",
            installerLength: 51475600,
            installerSha256: "A53BFE9121C2AD6BA2DDCB4699A5EDF9B53071E6D9EBDF3326AB96210EA4749A");
        AddV4(profiles, 0x0A51, "Razer BlackShark V3 (Xbox)", V4Dll113Name, V4Dll113Uri,
            V4Dll113Sha256, "razer_blacksharkv3_xbox", "headphones_game",
            "https://app-assets.razer.com/files/synapse/devices/2641/THXAPO_razer_blacksharkv3_xbox_v4.1.28.1056-75fb4b2-i1879n_MSSIGN-e6a07c7f.f72a56cf.exe",
            "https://app-assets.razer.com/files/synapse/devices/2641/properties-updater/thx_razer_blacksharkv3_xbox_properties_updater-v4.1.28-f19f37d9-Razer-4.1.28-prod.exe",
            "https://app-assets.razer.com/files/synapse/devices/2641/714-patcher/thx_razer_blacksharkv3_xbox_v4.2.38-a844df5a-714_patcher_config_updater-v14-20260420.exe",
            installerLength: 51475152,
            installerSha256: "650FBBCBF19803B3306E456C67AE22357037879EB3526899DCAB427FBC012FAA");
        AddV4(profiles, 0x101E, "Razer BlackShark V3 (PlayStation)", V4Dll113Name, V4Dll113Uri,
            V4Dll113Sha256, "razer_blacksharkv3_ps", "headphones_game",
            "https://app-assets.razer.com/files/synapse/devices/4126/THXAPO_razer_blacksharkv3_ps_v4.1.28.1055-75fb4b2-i1883n_MSSIGN-e6a07c7f.698e281f.exe",
            "https://app-assets.razer.com/files/synapse/devices/4126/properties-updater/thx_razer_blacksharkv3_ps_properties_updater-v4.1.28-f19f37d9-Razer-4.1.28-prod.exe",
            "https://app-assets.razer.com/files/synapse/devices/4126/714-patcher/thx_razer_blacksharkv3_ps_v4.2.38-a844df5a-714_patcher_config_updater-v14-20260420.exe",
            installerLength: 51454160,
            installerSha256: "EDEDD7A4F491143709DB4D0151F15D3FAB1D4075E947543FC00BED61CA88AAF2");
        // 101F is the older PS package and pins the 1.0.8.1 native DLL with no game profiles.
        AddV4(profiles, 0x101F, "Razer BlackShark V3 (PlayStation)",
            "ThxV4Native_v1.0.8.1.dll",
            "https://app-assets.razer.com/files/synapse/devices/common/THX/ThxV4Native_v1.0.8.1.dll",
            "FAB5E5D7A57B468A90661475492A2B826DB0165A64D50A9A07AA8FE51139CDFE",
            "razer_blacksharkv3_ps", "headphones_game",
            "https://app-assets.razer.com/files/synapse/devices/4127/THXAPO_razer_blacksharkv3_ps-1532-101F!101E_v4.1.25-26bf1ee_build-779-i1487n_MSSIGN-WiX-Installer.exe",
            installerLength: 50304144,
            installerSha256: "753CA9F33DB2791419CD405A3AC073F49C027800965429AB14EF823812290434",
            nativeDllLength: 13681800);
    }

    private static void AddV4Alias(
        IDictionary<ushort, ThxHeadsetProfile> profiles,
        ushort productId,
        ushort sourceProductId) =>
        profiles[productId] = profiles[sourceProductId] with { ProductId = productId };
}
