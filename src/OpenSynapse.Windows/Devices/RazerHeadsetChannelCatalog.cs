using System.Collections.Frozen;

namespace OpenSynapse.Windows.Devices;

public enum RazerHeadsetChannel
{
    Feature91,
    Mxic64,
    Valkyrie64,
    Billie64,
    Mixer053E,
    DedicatedBlackShark,
    Unsupported,
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
            [0x053B] = new("AudioMxicDevice", RazerHeadsetChannel.Mxic64),
            [0x0555] = new("AudioMxicDevice", RazerHeadsetChannel.Mxic64),
            [0x0583] = new("AudioMxicDevice", RazerHeadsetChannel.Mxic64),
            [0x0584] = new("AudioMxicDevice", RazerHeadsetChannel.Mxic64),

            // rzDevice25ValkyrieT3: class 0x18 triplets on 64-byte report id 7.
            [0x0A54] = new("rzDevice25ValkyrieT3", RazerHeadsetChannel.Mxic64),
            [0x1022] = new("rzDevice25ValkyrieT3", RazerHeadsetChannel.Mxic64),

            // rzDevice40: class 0x18 triplets inside the 64-byte 50 41 envelope.
            [0x0A61] = new("rzDevice40", RazerHeadsetChannel.Mxic64),
            [0x0A64] = new("rzDevice40", RazerHeadsetChannel.Mxic64),
            [0x0A68] = new("rzDevice40", RazerHeadsetChannel.Mxic64),
            [0x0A6C] = new("rzDevice40", RazerHeadsetChannel.Mxic64),
            [0x0A81] = new("rzDevice40", RazerHeadsetChannel.Mxic64),

            // AudioValkyrie: the 64-byte Valkyrie channel.
            [0x0579] = new("AudioValkyrie", RazerHeadsetChannel.Valkyrie64),
            [0x0A4E] = new("AudioValkyrie", RazerHeadsetChannel.Valkyrie64),
            [0x0A51] = new("AudioValkyrie", RazerHeadsetChannel.Valkyrie64),
            [0x101E] = new("AudioValkyrie", RazerHeadsetChannel.Valkyrie64),
            [0x101F] = new("AudioValkyrie", RazerHeadsetChannel.Valkyrie64),

            // AudioBillieT1/T2: the 64-byte Billie channel.
            [0x0567] = new("AudioBillieT1", RazerHeadsetChannel.Billie64),
            [0x056B] = new("AudioBillieT2", RazerHeadsetChannel.Billie64),

            // AudioBastetT1: same 64-byte report-id-2 frame and shared 56555 command
            // table as AudioBillieT2.
            [0x0587] = new("AudioBastetT1", RazerHeadsetChannel.Billie64),

            // rzDevice40HeadsetAiroha: 64-byte report-id-2 frame with the protocol40
            // header (class/id/size at 5..8), selected as the Mxic64 HID channel and
            // dispatched by the Protocol40 dialect in RazerMxicHeadsetService.
            [0x0593] = new("rzDevice40HeadsetAiroha", RazerHeadsetChannel.Mxic64),

            // BlackShark V3 Pro keeps its own THX panel instead of the generic headset page.
            // 0576 and 101C declare AudioValkyrie, but that channel belongs to the dedicated
            // panel here. 0577/101B have no bundle in the CDN snapshot; their device type is
            // therefore unverified.
            [0x0576] = new("AudioValkyrie", RazerHeadsetChannel.DedicatedBlackShark),
            [0x0577] = new("unknown", RazerHeadsetChannel.DedicatedBlackShark),
            [0x101B] = new("unknown", RazerHeadsetChannel.DedicatedBlackShark),
            [0x101C] = new("AudioValkyrie", RazerHeadsetChannel.DedicatedBlackShark),

            // 91-byte feature-report devices.
            [0x051A] = new("AudioAvneraDevice", RazerHeadsetChannel.Feature91),
            [0x051C] = new("AudioAvneraDevice", RazerHeadsetChannel.Feature91),
            [0x051E] = new("AudioAvneraDevice", RazerHeadsetChannel.Feature91),
            [0x0528] = new("AudioAvneraDeviceOdessaT1", RazerHeadsetChannel.Feature91),
            [0x0529] = new("AudioKrakenOdessaT2", RazerHeadsetChannel.Feature91),
            [0x0520] = new("AudioKrakenTE", RazerHeadsetChannel.Feature91),
            [0x0527] = new("AudioKrakenUltimate", RazerHeadsetChannel.Feature91),
            [0x0526] = new("AudioKrakenXUSB", RazerHeadsetChannel.Feature91),
            [0x0521] = new("AudioKrakenKitty", RazerHeadsetChannel.Feature91),
            [0x0560] = new("AudioKrakenKittyT3", RazerHeadsetChannel.Feature91),
            [0x0533] = new("AudioKrystalT2", RazerHeadsetChannel.Feature91),
            [0x0549] = new("AudioKrystalT2", RazerHeadsetChannel.Feature91),
            [0x0537] = new("AudioKrystalT3", RazerHeadsetChannel.Feature91),
            [0x056D] = new("AudioKrystalT3", RazerHeadsetChannel.Feature91),
            [0x0542] = new("AudioCamyT1", RazerHeadsetChannel.Feature91),
            [0x0543] = new("AudioCamyT2", RazerHeadsetChannel.Feature91),
            [0x0F1B] = new("AudioCamyT3Device", RazerHeadsetChannel.Feature91),
            // AudioKrakenKittyV2Pro only appends a 37-byte Chroma frame to the same
            // AudioKittyV2Pro audio base class, so its audio stays on the 91-byte channel.
            [0x0554] = new("AudioKittyV2Pro", RazerHeadsetChannel.Feature91),
            [0x0532] = new("rzDevice25Noa", RazerHeadsetChannel.Feature91),
            [0x0548] = new("rzDevice25Noa", RazerHeadsetChannel.Feature91),
            [0x055A] = new("rzDevice25NommoV2Pro", RazerHeadsetChannel.Feature91),
            [0x055C] = new("rzDevice25NommoV2Pro", RazerHeadsetChannel.Feature91),
            [0x055E] = new("rzDevice25NommoV2Pro", RazerHeadsetChannel.Feature91),
            [0x056A] = new("rzDevice25AudioCamyT3V2", RazerHeadsetChannel.Feature91),
            [0x0F66] = new("rzDevice25MarciT1", RazerHeadsetChannel.Feature91),
            [0x056E] = new("AudioOdessaT2Wireless", RazerHeadsetChannel.Feature91),

            // AudioMixer uses the dedicated 64-byte feature / 16-byte report-9
            // collection; it must not fall back to the 91-byte feature channel.
            [0x053E] = new("AudioMixer", RazerHeadsetChannel.Mixer053E),

            // Bundles that declare a channel this app does not build.
            [0x0309] = new("AudioAWKittyBLE", RazerHeadsetChannel.Unsupported),
            [0x0530] = new("AudioAWKittyBLE", RazerHeadsetChannel.Unsupported),
            [0x0534] = new("AudioAWKittyBLE", RazerHeadsetChannel.Unsupported),
            [0x0562] = new("AudioAWKittyBLE", RazerHeadsetChannel.Unsupported),
            [0x052D] = new("AudioNXPDevice", RazerHeadsetChannel.Unsupported),
            // These two have no bundle in the CDN snapshot, so their device type is unknown.
            [0x053C] = new("unknown", RazerHeadsetChannel.Unsupported),
            [0x0565] = new("unknown", RazerHeadsetChannel.Unsupported),
        }.ToFrozenDictionary();

    public static RazerHeadsetChannel Resolve(ushort productId) =>
        Entries.TryGetValue(productId, out var entry) ? entry.Channel : RazerHeadsetChannel.Unsupported;

    public static bool IsSupported(ushort productId) =>
        Resolve(productId) != RazerHeadsetChannel.Unsupported;

    /// <summary>True when the PID has an audited catalog entry, even if that entry is Unsupported.</summary>
    internal static bool IsKnown(ushort productId) => Entries.ContainsKey(productId);

    public static string Describe(ushort productId) =>
        Entries.TryGetValue(productId, out var entry)
            ? $"{productId:X4} {entry.DeviceType} {entry.Channel}"
            : $"{productId:X4} unknown Unsupported";
}
