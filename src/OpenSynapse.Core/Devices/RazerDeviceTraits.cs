namespace OpenSynapse.Core.Devices;

/// <summary>Product traits that the protocol capability set cannot express.</summary>
public static class RazerDeviceTraits
{
    // AA/AAA-powered mice. Their charging report is absent or permanently "not charging".
    public static bool UsesReplaceableBattery(ushort productId) =>
        productId is 0x0013 or 0x0039 or 0x0048 or 0x0062 or 0x0083 or
            0x0094 or 0x0095 or 0x009A or 0x009C or 0x00B4 or
            0x00B8 or 0x00B9 or 0x00E0;
}
