using System.Collections.Frozen;
using System.Collections.Immutable;

namespace OpenSynapse.Windows.Devices;

public enum RazerProximityCalibrationKind
{
    Orca5Plus,
    Orca5,
    SmartPixart,
    Pixart,
    Pixart3349,
    Philips,
}

public readonly record struct RazerProximitySensorProfile(
    byte ClassId,
    byte SensorId,
    RazerProximityCalibrationKind Kind)
{
    public RazerCalibrationProductData? Calibration { get; init; }
}

public static class RazerProximitySensorProfiles
{
    public static ImmutableArray<RazerProximitySensorProfile> All => RazerCalibrationCatalogData.Products;

    private static readonly FrozenDictionary<ushort, RazerProximitySensorProfile> Profiles =
        All.SelectMany(profile => profile.Calibration!.UsbProductIds.Select(id => (id, profile)))
            .ToFrozenDictionary(item => item.id, item => item.profile);

    private static readonly FrozenDictionary<ushort, RazerProximitySensorProfile> BleProfiles =
        All.SelectMany(profile => profile.Calibration!.BleProductIds.Select(id => (id, profile)))
            .ToFrozenDictionary(item => item.id, item => item.profile);

    public static bool TryGet(ushort productId, out RazerProximitySensorProfile profile)
    {
        // Turret combo receivers need the logical mouse identity (0075/0A22) first.
        if (productId is 0x0904 or 0x0A21)
        {
            profile = default;
            return false;
        }
        // 0089 is the mouse identity explicitly aliased to the 00B9 product bundle.
        return Profiles.TryGetValue(productId == 0x0089 ? (ushort)0x00B9 : productId, out profile);
    }

    public static bool TryGetForConnection(
        ushort productId,
        RazerCalibrationConnection connection,
        out RazerProximitySensorProfile profile)
    {
        if (GetConnectionSupport(productId, connection) == RazerCalibrationSupport.Supported)
            return TryGetMetadata(productId, out profile);
        profile = default;
        return false;
    }

    /// <summary>Includes BLE metadata even when its calibration path is unavailable.</summary>
    public static bool TryGetMetadata(ushort productId, out RazerProximitySensorProfile profile) =>
        TryGet(productId, out profile) || BleProfiles.TryGetValue(productId, out profile);

    public static RazerCalibrationSupport GetConnectionSupport(
        ushort productId,
        RazerCalibrationConnection connection)
    {
        // A BLE PID is evidence for that transport only, even if its product also has USB.
        if (connection != RazerCalibrationConnection.Ble && BleProfiles.ContainsKey(productId))
            return RazerCalibrationSupport.Unknown;
        if (!TryGetMetadata(productId, out var profile))
            return RazerCalibrationSupport.Unknown;
        var product = profile.Calibration!;
        return connection switch
        {
            RazerCalibrationConnection.Wired => product.WiredSupport,
            RazerCalibrationConnection.Wireless => product.WirelessSupport,
            RazerCalibrationConnection.Ble => product.BleSupport,
            _ => RazerCalibrationSupport.Unknown,
        };
    }

    /// <summary>
    /// True only for a known USB/logical mouse profile with an evidenced adaptive mode.
    /// </summary>
    public static bool SupportsSmartTrackingUi(ushort productId) =>
        TryGet(productId, out var profile) &&
        profile.Calibration!.Capabilities.HasFlag(RazerCalibrationCapabilities.Adaptive);
}
