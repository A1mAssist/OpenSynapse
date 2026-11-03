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
        if (profile.FanCurve is { } curve)
        {
            var points = string.Join(",", curve.CpuPoints.Select(FormatPoint)) + "/" +
                string.Join(",", curve.GpuPoints.Select(FormatPoint));
            return $"curve\n{owner}{curve.TemperatureMode}\n" +
                $"{curve.MinimumCpuTemperatureCelsius},{curve.MinimumGpuTemperatureCelsius}," +
                $"{curve.MinimumFanSpeedRpm}\n{points}";
        }
        return profile.FanMode is byte mode
            ? $"fixed\n{owner}{mode}\n{profile.FanTargetRpm}" : string.Empty;

        static string FormatPoint(BladeFanCurvePoint point) =>
            $"{point.TemperatureCelsius}:{point.CpuFanSpeedRpm}:{point.GpuFanSpeedRpm}";
    }

    public static bool MatchesRunning(string runningFingerprint, string targetFingerprint, bool completionActive) =>
        completionActive && targetFingerprint.Length > 0 &&
        StringComparer.Ordinal.Equals(runningFingerprint, targetFingerprint);
}
