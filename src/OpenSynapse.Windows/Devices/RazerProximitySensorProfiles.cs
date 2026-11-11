using System.Collections.Frozen;

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
    RazerProximityCalibrationKind Kind);

public static class RazerProximitySensorProfiles
{
    private static readonly FrozenDictionary<ushort, RazerProximitySensorProfile> Profiles =
        new Dictionary<ushort, RazerProximitySensorProfile>
        {
            [0x00E2] = new(0x00, 0x04, RazerProximityCalibrationKind.Orca5Plus),
            [0x00E3] = new(0x00, 0x04, RazerProximityCalibrationKind.Orca5Plus),
            [0x00EA] = new(0x00, 0x04, RazerProximityCalibrationKind.Orca5Plus),
            [0x00EB] = new(0x00, 0x04, RazerProximityCalibrationKind.Orca5Plus),
            [0x00E5] = new(0x00, 0x04, RazerProximityCalibrationKind.Orca5Plus),
            [0x00E6] = new(0x00, 0x04, RazerProximityCalibrationKind.Orca5Plus),
            [0x00E7] = new(0x00, 0x04, RazerProximityCalibrationKind.Orca5Plus),
            [0x00E8] = new(0x00, 0x04, RazerProximityCalibrationKind.Orca5Plus),
            [0x00EF] = new(0x00, 0x04, RazerProximityCalibrationKind.Orca5Plus),
            [0x00F0] = new(0x00, 0x04, RazerProximityCalibrationKind.Orca5Plus),
            [0x00AA] = new(0x00, 0x04, RazerProximityCalibrationKind.Orca5Plus),
            [0x00AB] = new(0x00, 0x04, RazerProximityCalibrationKind.Orca5Plus),

            [0x00A3] = new(0x01, 0x04, RazerProximityCalibrationKind.Pixart3349),
            [0x00B9] = new(0x01, 0x04, RazerProximityCalibrationKind.Pixart3349),
            [0x00D3] = new(0x01, 0x04, RazerProximityCalibrationKind.Pixart3349),

            [0x0046] = new(0x00, 0x05, RazerProximityCalibrationKind.Philips),
            [0x0050] = new(0x00, 0x05, RazerProximityCalibrationKind.Philips),
            [0x0053] = new(0x00, 0x05, RazerProximityCalibrationKind.Philips),
            [0x0059] = new(0x00, 0x05, RazerProximityCalibrationKind.Philips),
        }.ToFrozenDictionary();

    public static bool TryGet(ushort productId, out RazerProximitySensorProfile profile) =>
        Profiles.TryGetValue(productId, out profile);

    /// <summary>
    /// Unknown mice keep the probe-gated behavior. Known non-Orca5Plus engines are
    /// deliberately kept out of the smart-tracking UI until their writes are verified.
    /// </summary>
    public static bool SupportsSmartTrackingUi(ushort productId) =>
        !Profiles.TryGetValue(productId, out var profile) ||
        profile.Kind == RazerProximityCalibrationKind.Orca5Plus;
}
