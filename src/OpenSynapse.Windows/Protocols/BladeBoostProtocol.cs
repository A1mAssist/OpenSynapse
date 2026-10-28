using OpenSynapse.Core.Devices;

namespace OpenSynapse.Windows.Protocols;

public static class BladeBoostProtocol
{
    /// <summary>
    /// rzDevice25 getAdvFanMode()/setAdvFanMode() default to profile 1; every Boost
    /// read and write asks the firmware for that same profile.
    /// </summary>
    public const byte ProfileId = 0x01;

    public const byte CpuCluster = 0x01;
    public const byte GpuCluster = 0x02;

    public static byte[] CreateGetArguments(byte cluster) => [ProfileId, cluster, 0x00];

    public static byte[] CreateSetArguments(byte cluster, byte value) => [ProfileId, cluster, value];

    public static BladeCpuBoostMode ParseCpu(ReadOnlySpan<byte> response, byte profileId) =>
        ParseValue(response, profileId, CpuCluster, "CPU") switch
        {
            0x00 => BladeCpuBoostMode.Low,
            0x01 => BladeCpuBoostMode.Medium,
            0x02 => BladeCpuBoostMode.High,
            0x03 => BladeCpuBoostMode.Boost,
            0x04 => BladeCpuBoostMode.Undervolt,
            var value => throw new InvalidOperationException($"Blade returned an unknown CPU Boost value: 0x{value:X2}."),
        };

    public static BladeGpuBoostMode ParseGpu(ReadOnlySpan<byte> response, byte profileId) =>
        ParseValue(response, profileId, GpuCluster, "GPU") switch
        {
            0x00 => BladeGpuBoostMode.Low,
            0x01 => BladeGpuBoostMode.Medium,
            0x02 => BladeGpuBoostMode.High,
            var value => throw new InvalidOperationException($"Blade returned an unknown GPU Boost value: 0x{value:X2}."),
        };

    private static byte ParseValue(ReadOnlySpan<byte> response, byte profileId, byte expectedCluster, string name)
    {
        if (response.Length != RazerFeatureReport.Length)
        {
            throw new InvalidOperationException($"Blade {name} Boost response length is not {RazerFeatureReport.Length} bytes.");
        }
        if (response[6] < 3)
        {
            throw new InvalidOperationException($"Blade {name} Boost response is too short: {response[6]} < 3.");
        }

        var arguments = response[RazerFeatureReport.ArgumentsOffset..];
        if (arguments[0] != profileId || arguments[1] != expectedCluster)
        {
            throw new InvalidOperationException(
                $"Blade returned an incorrect {name} Boost group: {arguments[0]:X2}/{arguments[1]:X2}.");
        }

        return arguments[2];
    }
}
