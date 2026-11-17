namespace OpenSynapse.Windows.Audio;

internal static partial class ThxHeadsetAdmission
{
    private static void AddConsoleProfiles(IDictionary<ushort, ThxHeadsetProfile> profiles)
    {
        AddV4(profiles, 0x101C, "Razer BlackShark V3 Pro (PlayStation)", V4Dll113Name, V4Dll113Uri,
            V4Dll113Sha256, "razer_blacksharkv3pro_ps", "headphones_game", 4124);
        // 101B is the PS Pro 2.4 GHz dongle; it reports its own pid but uses the same package.
        AddV4Alias(profiles, 0x101B, 0x101C);
        AddV4(profiles, 0x0A4E, "Razer BlackShark V3 Pro (Xbox)", V4Dll113Name, V4Dll113Uri,
            V4Dll113Sha256, "razer_blacksharkv3pro_xbox", "headphones_game", 2638);
        AddV4(profiles, 0x0A51, "Razer BlackShark V3 (Xbox)", V4Dll113Name, V4Dll113Uri,
            V4Dll113Sha256, "razer_blacksharkv3_xbox", "headphones_game", 2641);
        AddV4(profiles, 0x101E, "Razer BlackShark V3 (PlayStation)", V4Dll113Name, V4Dll113Uri,
            V4Dll113Sha256, "razer_blacksharkv3_ps", "headphones_game", 4126);
        // 101F is the older PS package and pins the 1.0.8.1 native DLL with no game profiles.
        AddV4(profiles, 0x101F, "Razer BlackShark V3 (PlayStation)",
            "ThxV4Native_v1.0.8.1.dll",
            "https://app-assets.razer.com/files/synapse/devices/common/THX/ThxV4Native_v1.0.8.1.dll",
            "FAB5E5D7A57B468A90661475492A2B826DB0165A64D50A9A07AA8FE51139CDFE",
            "razer_blacksharkv3_ps", "headphones_game", 4127,
            nativeDllLength: 13681800);
    }

    private static void AddV4Alias(
        IDictionary<ushort, ThxHeadsetProfile> profiles,
        ushort productId,
        ushort sourceProductId) =>
        profiles[productId] = profiles[sourceProductId] with { ProductId = productId };
}
