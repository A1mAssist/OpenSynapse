using OpenSynapse.Core.Devices;
using OpenSynapse.Core.Sensors;

namespace OpenSynapse.Core.Profiles;

/// <summary>Identity of an explicitly started fan control, independent of refresh history.</summary>
public static class BladeFanProfileFingerprint
{
    public static string Create(BladeProfileSettings profile, string devicePath,
        PowerSourceKind powerState, string activeProfileName)
    {
        var owner = $"{devicePath}\n{powerState}\n{activeProfileName}\n";
        if (profile.ResolvedFanControlMode == BladeFanControlMode.Curve &&
            profile.FanCurve is { } curve)
        {
            var points = string.Join(",", curve.CpuPoints.Select(FormatPoint)) + "/" +
                string.Join(",", curve.GpuPoints.Select(FormatPoint));
            return $"curve\n{owner}{curve.TemperatureMode}\n" +
                $"{curve.MinimumCpuTemperatureCelsius},{curve.MinimumGpuTemperatureCelsius}," +
                $"{curve.MinimumFanSpeedRpm}\n{points}";
        }
        return profile.ResolvedFanControlMode switch
        {
            BladeFanControlMode.Fixed =>
                $"fixed\n{owner}{(profile.FanMode ?? (byte)BladeFanMode.Manual)}\n{profile.FanTargetRpm}",
            BladeFanControlMode.Automatic => $"automatic\n{owner}",
            _ => string.Empty,
        };

        static string FormatPoint(BladeFanCurvePoint point) =>
            $"{point.TemperatureCelsius}:{point.CpuFanSpeedRpm}:{point.GpuFanSpeedRpm}";
    }

    public static bool MatchesRunning(string runningFingerprint, string targetFingerprint, bool completionActive) =>
        completionActive && targetFingerprint.Length > 0 &&
        StringComparer.Ordinal.Equals(runningFingerprint, targetFingerprint);
}
