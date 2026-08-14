using OpenSynapse.Core.Devices;

namespace OpenSynapse.Windows.Protocols;

public static class BladeBoostProtocol
{
    public const byte CpuCluster = 0x01;
    public const byte GpuCluster = 0x02;

    public static BladeCpuBoostMode ParseCpu(ReadOnlySpan<byte> response) =>
        ParseValue(response, CpuCluster, "CPU") switch
        {
            0x00 => BladeCpuBoostMode.Low,
            0x01 => BladeCpuBoostMode.Medium,
            0x02 => BladeCpuBoostMode.High,
            0x03 => BladeCpuBoostMode.Boost,
            0x04 => BladeCpuBoostMode.Undervolt,
            var value => throw new InvalidOperationException($"Blade 返回了未知 CPU Boost 值 0x{value:X2}。"),
        };

    public static BladeGpuBoostMode ParseGpu(ReadOnlySpan<byte> response) =>
        ParseValue(response, GpuCluster, "GPU") switch
        {
            0x00 => BladeGpuBoostMode.Low,
            0x01 => BladeGpuBoostMode.Medium,
            0x02 => BladeGpuBoostMode.High,
            var value => throw new InvalidOperationException($"Blade 返回了未知 GPU Boost 值 0x{value:X2}。"),
        };

    private static byte ParseValue(ReadOnlySpan<byte> response, byte expectedCluster, string name)
    {
        if (response.Length != RazerFeatureReport.Length)
        {
            throw new InvalidOperationException($"Blade {name} Boost 响应长度不是 {RazerFeatureReport.Length} 字节。");
        }
        if (response[6] < 3)
        {
            throw new InvalidOperationException($"Blade {name} Boost 响应长度不足：{response[6]} < 3。");
        }

        var arguments = response[RazerFeatureReport.ArgumentsOffset..];
        if (arguments[0] != 0x00 || arguments[1] != expectedCluster)
        {
            throw new InvalidOperationException(
                $"Blade 返回了错误的 {name} Boost 分组：{arguments[0]:X2}/{arguments[1]:X2}。");
        }

        return arguments[2];
    }
}
