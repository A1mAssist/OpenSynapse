using System.Collections.Frozen;

namespace OpenSynapse.Windows.Devices;

public enum RazerHeadsetChannel
{
    Feature91,
    AvneraFeature65,
    Protocol25Feature65,
    Protocol25Feature64,
    Mxic64,
    Valkyrie64,
    Billie64,
    Mixer053E,
    DedicatedBlackShark,
    Unsupported,
    NativeDll,
    ValkyrieT3Feature64,
    Jolie64,
    KrakenBase37,
    CamyT1,
    NxpSerial,
    AudiowiseBle,
    ApBle,
    RipsawFeature17,
    Gip60,
}

/// <summary>
/// Audio transport declared by each device bundle through
/// <c>useFeature("rzDeviceType", ...)</c> in its main.js. Discovery follows this table
/// instead of assuming that every headset answers the 91-byte feature channel.
/// </summary>
public static class RazerHeadsetChannelCatalog
{
    private readonly record struct Entry(string DeviceType, RazerHeadsetChannel Channel);

    private static readonly FrozenDictionary<ushort, Entry> Entries =
        new Dictionary<ushort, Entry>
        {
            // Mxic64: 64-byte report-id-2 HID channel. AudioMxicDevice uses the
            // 50 41 envelope; 0593 uses the protocol40 header on the same channel.
            [0x0539] = new("AudioMxicDevice", RazerHeadsetChannel.Mxic64),
            [0x053A] = new("AudioMxicDevice", RazerHeadsetChannel.Mxic64),
            [0x053B] = new("AudioMxicDevice", RazerHeadsetChannel.Mxic64),
            [0x0555] = new("AudioMxicDevice", RazerHeadsetChannel.Mxic64),
            [0x0583] = new("AudioMxicDevice", RazerHeadsetChannel.Mxic64),
            [0x0584] = new("AudioMxicDevice", RazerHeadsetChannel.Mxic64),

            // rzDevice25ValkyrieT3: class 0x18 triplets on 64-byte report id 7.
            [0x057C] = new("rzDevice25ValkyrieT3", RazerHeadsetChannel.ValkyrieT3Feature64),
            [0x057D] = new("rzDevice25ValkyrieT3", RazerHeadsetChannel.ValkyrieT3Feature64),
            [0x05B6] = new("rzDevice25ValkyrieT3", RazerHeadsetChannel.ValkyrieT3Feature64),
            [0x05B3] = new("rzDevice25ValkyrieT3", RazerHeadsetChannel.ValkyrieT3Feature64),
            [0x0A54] = new("rzDevice25ValkyrieT3", RazerHeadsetChannel.ValkyrieT3Feature64),
            [0x0A5B] = new("rzDevice25ValkyrieT3", RazerHeadsetChannel.ValkyrieT3Feature64),
            [0x1021] = new("rzDevice25ValkyrieT3", RazerHeadsetChannel.ValkyrieT3Feature64),
            [0x1022] = new("rzDevice25ValkyrieT3", RazerHeadsetChannel.ValkyrieT3Feature64),

            // rzDeviceJolie (Razer Mako X): class 0x18 EQ and class 0x0F Chroma on
            // 64-byte report id 7. The ten-band setter adds group and preset bytes.
            [0x05AB] = new("rzDeviceJolie", RazerHeadsetChannel.Jolie64),
            [0x05AD] = new("rzDeviceJolie", RazerHeadsetChannel.Jolie64),

            // rzDevice40: 64-byte report-id-2 protocol40 frame (transaction at 2,
            // size/class/id at 6..8), not the AudioMxicDevice 50 41 envelope.
            [0x0A61] = new("rzDevice40", RazerHeadsetChannel.Mxic64),
            [0x0A64] = new("rzDevice40", RazerHeadsetChannel.Mxic64),
            [0x0A68] = new("rzDevice40", RazerHeadsetChannel.Mxic64),
            [0x0A6C] = new("rzDevice40", RazerHeadsetChannel.Mxic64),
            [0x0A81] = new("rzDevice40", RazerHeadsetChannel.Mxic64),

            // AudioValkyrie: the 64-byte Valkyrie channel for USB, AP-BLE for the
            // BLE identities.
            [0x0575] = new("AudioValkyrieBle", RazerHeadsetChannel.ApBle),
            [0x0578] = new("AudioValkyrieBle", RazerHeadsetChannel.ApBle),
            [0x0579] = new("AudioValkyrie", RazerHeadsetChannel.Valkyrie64),
            [0x057A] = new("AudioValkyrie", RazerHeadsetChannel.Valkyrie64),
            [0x0A4E] = new("AudioValkyrie", RazerHeadsetChannel.Valkyrie64),
            [0x0A4F] = new("AudioValkyrieBle", RazerHeadsetChannel.ApBle),
            [0x0A51] = new("AudioValkyrie", RazerHeadsetChannel.Valkyrie64),
            [0x0A52] = new("AudioValkyrieBle", RazerHeadsetChannel.ApBle),
            [0x0A55] = new("AudioValkyrie", RazerHeadsetChannel.Valkyrie64),
            [0x0A56] = new("rzDevice25ValkyrieT3Ble", RazerHeadsetChannel.ApBle),
            [0x0A58] = new("AudioValkyrie", RazerHeadsetChannel.Valkyrie64),
            [0x101D] = new("AudioValkyrieBle", RazerHeadsetChannel.ApBle),
            [0x101E] = new("AudioValkyrie", RazerHeadsetChannel.Valkyrie64),
            [0x101F] = new("AudioValkyrie", RazerHeadsetChannel.Valkyrie64),
            [0x1020] = new("AudioValkyrieBle", RazerHeadsetChannel.ApBle),

            // Native GIP identities from the Xbox UWP bundle. That bundle controls
            // them through Windows.Gaming.Input custom controllers; OpenSynapse's
            // Native GIP identities: 60-byte report-0x0F frames from the Xbox UWP
            // bundle, decoded by RazerGipHeadsetProtocol's six dialects.
            [0x0A25] = new("AudioSimone", RazerHeadsetChannel.Gip60),
            [0x0A27] = new("AudioSimone", RazerHeadsetChannel.Gip60),
            [0x0A30] = new("AudioSimoneX", RazerHeadsetChannel.Gip60),
            [0x0A32] = new("AudioSimoneX", RazerHeadsetChannel.Gip60),
            [0x0A41] = new("AudioOdessaT1R", RazerHeadsetChannel.Gip60),
            [0x0A4D] = new("AudioValkyrieT1", RazerHeadsetChannel.Gip60),
            [0x0A50] = new("AudioValkyrieT2", RazerHeadsetChannel.Gip60),
            [0x0A53] = new("AudioValkyrieT3", RazerHeadsetChannel.Gip60),
            [0x0A69] = new("rzDevice40Gip", RazerHeadsetChannel.Gip60),

            // AudioBillieT1/T2: the 64-byte Billie channel.
            [0x0567] = new("AudioBillieT1", RazerHeadsetChannel.Billie64),
            [0x0568] = new("AudioBillieT1Dongle", RazerHeadsetChannel.Billie64),
            [0x056B] = new("AudioBillieT2", RazerHeadsetChannel.Billie64),
            [0x056C] = new("AudioBillieT2Dongle", RazerHeadsetChannel.Billie64),

            // AudioBastetT1: same 64-byte report-id-2 frame and shared 56555 command
            // table as AudioBillieT2.
            [0x0587] = new("AudioBastetT1", RazerHeadsetChannel.Billie64),
            [0x0588] = new("AudioBastetT1Dongle", RazerHeadsetChannel.Billie64),
            [0x0589] = new("AudioBastetT1Ble", RazerHeadsetChannel.ApBle),

            // rzDevice40HeadsetAiroha: 64-byte report-id-2 frame with the protocol40
            // header (class/id/size at 5..8), selected as the Mxic64 HID channel and
            // dispatched by the Protocol40 dialect in RazerMxicHeadsetService.
            [0x0592] = new("rzDevice40HeadsetAiroha", RazerHeadsetChannel.Mxic64),
            [0x0593] = new("rzDevice40HeadsetAiroha", RazerHeadsetChannel.Mxic64),
            [0x059F] = new("rzDevice40BillieT3H", RazerHeadsetChannel.Mxic64),
            // 05B9 exposes the rzDevice40 frame only as a 64-byte FF14 feature report.
            [0x05B9] = new("rzDevice40", RazerHeadsetChannel.Mxic64),

            // BlackShark V3 Pro keeps its own THX panel instead of the generic headset page.
            // 0576 declares dongleId 1399 (0577); 101C declares 4123 (101B).
            [0x0576] = new("AudioValkyrie", RazerHeadsetChannel.DedicatedBlackShark),
            [0x0577] = new("AudioValkyrie", RazerHeadsetChannel.DedicatedBlackShark),
            [0x101B] = new("AudioValkyrie", RazerHeadsetChannel.DedicatedBlackShark),
            [0x101C] = new("AudioValkyrie", RazerHeadsetChannel.DedicatedBlackShark),

            // AudioAvnera uses a 64-byte feature frame with report id 255. The
            // 2020 BlackShark receiver answers with 64 bytes; other identities
            // use 65. 0528's OdessaT1 subclass only adds the extended firmware
            // command.
            [0x051A] = new("AudioAvneraDevice", RazerHeadsetChannel.AvneraFeature65),
            [0x051C] = new("AudioAvneraDevice", RazerHeadsetChannel.AvneraFeature65),
            [0x051E] = new("AudioAvneraDevice", RazerHeadsetChannel.AvneraFeature65),
            // 051F has no bundle in the local CDN snapshot. It shares the Nari
            // Essential identity with 051E and is kept on the same family path.
            [0x051F] = new("AudioAvneraDevice", RazerHeadsetChannel.AvneraFeature65),
            [0x0528] = new("AudioAvneraDeviceOdessaT1", RazerHeadsetChannel.AvneraFeature65),
            // Protocol 25 audio uses a shorter feature report. Noa/Camy/Linda
            // otherwise keep the same class/command table as the legacy page.
            [0x0532] = new("rzDevice25Noa", RazerHeadsetChannel.Protocol25Feature65),
            [0x0548] = new("rzDevice25Noa", RazerHeadsetChannel.Protocol25Feature65),
            [0x0570] = new("rzDevice25Carol", RazerHeadsetChannel.Protocol25Feature65),
            [0x05A2] = new("rzDevice25Carol", RazerHeadsetChannel.Protocol25Feature65),
            [0x05A3] = new("rzDevice25NoaT2ECR", RazerHeadsetChannel.Protocol25Feature64),
            [0x056A] = new("rzDevice25AudioCamyT3V2", RazerHeadsetChannel.Protocol25Feature65),
            [0x056F] = new("rzDevice25AudioCamy", RazerHeadsetChannel.Protocol25Feature65),
            [0x058E] = new("rzDevice25AudioMarieT1", RazerHeadsetChannel.Protocol25Feature65),
            [0x05A6] = new("rzDevice25AudioCamyT2V3R", RazerHeadsetChannel.Protocol25Feature65),
            [0x058C] = new("AudioLindaT3", RazerHeadsetChannel.Protocol25Feature65),
            // AudioKrakenBase sends fixed 37-byte HID reports through hid.write/hid.read,
            // not a feature-report channel. The same
            // base class backs the TE, Kitty, X USB, Ultimate and Kitty V2 models.
            [0x0529] = new("AudioKrakenOdessaT2", RazerHeadsetChannel.KrakenBase37),
            [0x0520] = new("AudioKrakenTE", RazerHeadsetChannel.KrakenBase37),
            [0x0527] = new("AudioKrakenUltimate", RazerHeadsetChannel.KrakenBase37),
            [0x0526] = new("AudioKrakenXUSB", RazerHeadsetChannel.KrakenBase37),
            [0x0521] = new("AudioKrakenKitty", RazerHeadsetChannel.KrakenBase37),
            [0x0560] = new("AudioKrakenKittyT3", RazerHeadsetChannel.KrakenBase37),
            [0x058A] = new("AudioKrakenKittyT3", RazerHeadsetChannel.KrakenBase37),
            // The KrystalT2/T3 bundles inherit AudioKrakenBase and use the
            // existing 37-byte write/33-byte read path.
            [0x0533] = new("AudioKrystalT2", RazerHeadsetChannel.KrakenBase37),
            [0x0549] = new("AudioKrystalT2", RazerHeadsetChannel.KrakenBase37),
            [0x0537] = new("AudioKrystalT3", RazerHeadsetChannel.KrakenBase37),
            [0x056D] = new("AudioKrystalT3", RazerHeadsetChannel.KrakenBase37),
            // Seiren V2 Pro/X send eight-byte HID output reports for the mic DSP
            // (RzNative_0542 1.0.25.0 / AudioCamyT1). Razer's UI routes gain and
            // mute through the Windows capture endpoint, but the DLL exposes the
            // same four values as HID commands; this app uses the HID path so the
            // readback matches what the slider shows.
            [0x0542] = new("AudioCamyT1", RazerHeadsetChannel.CamyT1),
            [0x0543] = new("AudioCamyT2", RazerHeadsetChannel.CamyT1),
            [0x0F1B] = new("AudioCamyT3Device", RazerHeadsetChannel.Feature91),
            // AudioKrakenKittyV2Pro only appends a 37-byte Chroma frame to the same
            // AudioKrakenBase audio class, so its audio uses the 37-byte channel too.
            [0x0554] = new("AudioKittyV2Pro", RazerHeadsetChannel.KrakenBase37),
            [0x055A] = new("rzDevice25NommoV2Pro", RazerHeadsetChannel.Feature91),
            [0x055C] = new("rzDevice25NommoV2Pro", RazerHeadsetChannel.Feature91),
            [0x055E] = new("rzDevice25NommoV2Pro", RazerHeadsetChannel.Feature91),
            [0x0F66] = new("rzDevice25MarciT1", RazerHeadsetChannel.Feature91),
            // AudioOdessaT2Wireless overrides the AudioMxicDevice sender with the
            // Billie-style 64-byte report-id-2 frame. 056E is the wired endpoint and
            // 0565 is its 2.4 GHz dongle; neither is a BlackShark V3 Pro identity.
            [0x056E] = new("AudioOdessaT2Wireless", RazerHeadsetChannel.Billie64),
            [0x0565] = new("AudioOdessaT2Wireless", RazerHeadsetChannel.Billie64),
            [0x0574] = new("AudioMabelT3C", RazerHeadsetChannel.Billie64),
            [0x0582] = new("AudioKrystalT3", RazerHeadsetChannel.KrakenBase37),

            // AudioMixer uses the dedicated 64-byte feature / 16-byte report-9
            // collection; it must not fall back to the 91-byte feature channel.
            [0x053E] = new("AudioMixer", RazerHeadsetChannel.Mixer053E),
            [0x0D03] = new("rzDeviceKateT2", RazerHeadsetChannel.RipsawFeature17),

            // Independent GATT discovery and controls in RazerAwBleHeadsetService.
            [0x0309] = new("AudioAWKittyBLE", RazerHeadsetChannel.AudiowiseBle),
            [0x0530] = new("AudioAWKittyBLE", RazerHeadsetChannel.AudiowiseBle),
            [0x0534] = new("AudioAWKittyBLE", RazerHeadsetChannel.AudiowiseBle),
            [0x0562] = new("AudioAWKittyBLE", RazerHeadsetChannel.AudiowiseBle),
            [0x057E] = new("AudioAWKittyBLE", RazerHeadsetChannel.AudiowiseBle),
            [0x057F] = new("AudioAWKittyBLE", RazerHeadsetChannel.AudiowiseBle),
            // Speaker/earbud BLE aliases; RazerApBleHeadsetService claims them only
            // when the device exposes a known AP-BLE characteristic pair.
            [0x0558] = new("Leviathan V2 Pro BLE alias", RazerHeadsetChannel.ApBle),
            [0x0559] = new("Leviathan V2 X BLE alias", RazerHeadsetChannel.ApBle),
            [0x0A62] = new("Hammerhead BLE alias", RazerHeadsetChannel.ApBle),
            [0x0A65] = new("Hammerhead BLE alias", RazerHeadsetChannel.ApBle),
            [0x0A6D] = new("Hammerhead BLE alias", RazerHeadsetChannel.ApBle),
            [0x0A82] = new("Hammerhead BLE alias", RazerHeadsetChannel.ApBle),
            // 052D declares dongleId 1324 (052C). Both use the NXP serial interface.
            [0x052C] = new("AudioNXPDevice", RazerHeadsetChannel.NxpSerial),
            [0x052D] = new("AudioNXPDevice", RazerHeadsetChannel.NxpSerial),
            // 053C is the Barracuda 2.4 dongle of the 053B bundle (dongleId 1340) and
            // uses the same AudioMxicDevice / Mabel T1 transport as 053B.
            [0x053C] = new("AudioMxicDevice", RazerHeadsetChannel.Mxic64),
        }.ToFrozenDictionary();

    public static RazerHeadsetChannel Resolve(ushort productId) =>
        Entries.TryGetValue(productId, out var entry) ? entry.Channel : RazerHeadsetChannel.Unsupported;

    public static bool IsSupported(ushort productId) =>
        Resolve(productId) != RazerHeadsetChannel.Unsupported;

    /// <summary>True when the PID has an audited catalog entry, even if that entry is Unsupported.</summary>
    internal static bool IsKnown(ushort productId) => Entries.ContainsKey(productId);

    internal static IEnumerable<ushort> ProductIds => Entries.Keys;

    /// <summary>BLE identities live outside the HID product tables on purpose.</summary>
    internal static bool IsBluetoothIdentity(ushort productId) =>
        Entries.TryGetValue(productId, out var entry) &&
        entry.DeviceType.Contains("Ble", StringComparison.OrdinalIgnoreCase);

    public static string Describe(ushort productId) =>
        Entries.TryGetValue(productId, out var entry)
            ? $"{productId:X4} {entry.DeviceType} {entry.Channel}"
            : $"{productId:X4} unknown Unsupported";
}
