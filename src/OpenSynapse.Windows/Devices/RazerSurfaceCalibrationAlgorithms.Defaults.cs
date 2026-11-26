using System.Text.Json;

namespace OpenSynapse.Windows.Devices;

public static partial class RazerSurfaceCalibrationAlgorithms
{
    public static RazerSurfaceCalibrationConfiguration? CreateDefaultConfiguration(RazerProximitySensorProfile profile)
    {
        if (profile.Calibration is not { } product) return null;
        var initial = product.InitialCalibration;
        if (initial.ValueKind == JsonValueKind.Array)
            initial = initial.GetArrayLength() == 0 ? default : initial[0];
        if (initial.ValueKind != JsonValueKind.Object) return null;
        if (profile.Kind is RazerProximityCalibrationKind.Orca5 or RazerProximityCalibrationKind.Orca5Plus or RazerProximityCalibrationKind.SmartPixart)
        {
            if (!product.Capabilities.HasFlag(RazerCalibrationCapabilities.Adaptive) ||
                !initial.TryGetProperty("smartTracking", out var smart) || smart.ValueKind != JsonValueKind.Object ||
                !TryInt(smart, "trackingDistance", out var tracking) ||
                !TryInt(smart, "liftOffDistance", out var lift) || !TryInt(smart, "landingDistance", out var landing) ||
                !smart.TryGetProperty("isAsymmetric", out var asymmetric) ||
                asymmetric.ValueKind is not (JsonValueKind.True or JsonValueKind.False)) return null;
            var isAsymmetric = asymmetric.GetBoolean();
            return new()
            {
                Mode = isAsymmetric && profile.Kind != RazerProximityCalibrationKind.SmartPixart
                    ? RazerSurfaceCalibrationMode.Adjustable : RazerSurfaceCalibrationMode.Adaptive,
                TrackingDistance = tracking, LiftOffDistance = lift, LandingDistance = landing,
                IsAsymmetric = isAsymmetric,
            };
        }
        // The shared apply path explicitly disables the sensor for a surface of type "default".
        if (product.Capabilities.HasFlag(RazerCalibrationCapabilities.EnableDisable) &&
            initial.TryGetProperty("profiles", out var profiles) && profiles.ValueKind == JsonValueKind.Array &&
            profiles.EnumerateArray().Any(item => item.ValueKind == JsonValueKind.Object &&
                item.TryGetProperty("type", out var type) && type.ValueKind == JsonValueKind.String && type.GetString() == "default"))
            return new() { Mode = RazerSurfaceCalibrationMode.Disabled };
        return null;
    }

    private static bool TryInt(JsonElement value, string name, out int result)
    {
        result = 0;
        return value.TryGetProperty(name, out var property) && property.ValueKind == JsonValueKind.Number && property.TryGetInt32(out result);
    }
}
