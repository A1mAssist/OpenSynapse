using System.IO;
using OpenSynapse.Core.Devices;
using OpenSynapse.Windows.Devices;
using OpenSynapse.Windows.Protocols;
using Xunit;

namespace OpenSynapse.Core.Tests;

/// <summary>
/// HyperBoost has to reach the power budget Custom can, so the preset write carries the
/// model's highest admitted CPU/GPU levels and leaving it puts the Custom ones back.
/// </summary>
public sealed class BladeHyperBoostPowerLevelTests
{
    private const string DevicePath =
        @"\\?\hid#vid_1532&pid_02c6&mi_00#test#{00000000-0000-0000-0000-000000000000}";

    [Fact]
    public async Task HyperBoostRaisesTheLevelsAndLeavingItRestoresTheCustomOnes()
    {
        var descriptor = new DeviceDescriptor(
            "blade-02c6-test",
            DevicePath,
            0x1532,
            0x02C6,
            DeviceAccessState.Available,
            DeviceCapabilityState.PendingValidation,
            91,
            1,
            2,
            "blade-710",
            DeviceCategory.Laptop);
        var transport = new Blade02C6Transport();
        var reader = new RazerDeviceTelemetryReader(transport, RazerDeviceRegistry.BuiltIn);

        var initial = await reader.ReadAsync([descriptor]);
        Assert.Equal<BladeCpuBoostMode?>(BladeCpuBoostMode.Medium, initial.BladeCpuBoostMode);
        Assert.Equal<BladeGpuBoostMode?>(BladeGpuBoostMode.Low, initial.BladeGpuBoostMode);

        await reader.SetBladePerformanceModeAsync([descriptor], BladePerformanceMode.Hyperboost);

        Assert.Equal(BladePerformanceMode.Hyperboost, transport.PerformanceMode);
        Assert.Equal((byte)BladeCpuBoostMode.High, transport.CpuLevel);
        Assert.Equal((byte)BladeGpuBoostMode.High, transport.GpuLevel);

        await reader.SetBladePerformanceModeAsync([descriptor], BladePerformanceMode.Balanced);

        Assert.Equal(BladePerformanceMode.Balanced, transport.PerformanceMode);
        Assert.Equal((byte)BladeCpuBoostMode.Medium, transport.CpuLevel);
        Assert.Equal((byte)BladeGpuBoostMode.Low, transport.GpuLevel);
    }

    private sealed class Blade02C6Transport : IRazerFeatureTransport
    {
        public BladePerformanceMode PerformanceMode { get; private set; } = BladePerformanceMode.Balanced;

        public BladeFanMode FanMode { get; private set; } = BladeFanMode.Automatic;

        public byte CpuLevel { get; private set; } = (byte)BladeCpuBoostMode.Medium;

        public byte GpuLevel { get; private set; } = (byte)BladeGpuBoostMode.Low;

        public Task<byte[]> QueryAsync(
            string devicePath,
            byte transactionId,
            byte dataSize,
            byte commandClass,
            byte commandId,
            ReadOnlyMemory<byte> arguments,
            TimeSpan deviceWait,
            CancellationToken cancellationToken,
            bool allowRemainingPacketsMismatch = false)
        {
            var request = RazerFeatureReport.CreateRequest(
                transactionId, dataSize, commandClass, commandId, arguments.Span);
            var response = request.ToArray();
            response[1] = 0x01;
            var args = arguments.Span;
            switch (commandClass, commandId)
            {
                case (0x0D, 0x02):
                    PerformanceMode = (BladePerformanceMode)args[2];
                    FanMode = (BladeFanMode)args[3];
                    break;
                case (0x0D, 0x82):
                    response[RazerFeatureReport.ArgumentsOffset + 1] = args[1];
                    response[RazerFeatureReport.ArgumentsOffset + 2] = (byte)PerformanceMode;
                    response[RazerFeatureReport.ArgumentsOffset + 3] = (byte)FanMode;
                    break;
                case (0x0D, 0x87):
                    response[RazerFeatureReport.ArgumentsOffset] = 0x00;
                    response[RazerFeatureReport.ArgumentsOffset + 1] = args[1];
                    response[RazerFeatureReport.ArgumentsOffset + 2] =
                        args[1] == BladeBoostProtocol.CpuCluster ? CpuLevel : GpuLevel;
                    break;
                case (0x0D, 0x07):
                    if (args[1] == BladeBoostProtocol.CpuCluster)
                    {
                        CpuLevel = args[2];
                    }
                    else
                    {
                        GpuLevel = args[2];
                    }
                    break;
                default:
                    throw new IOException(
                        $"Unexpected report {commandClass:X2}:{commandId:X2} for this test.");
            }
            return Task.FromResult(response);
        }
    }
}
