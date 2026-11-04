using OpenSynapse.Core.Devices;
using OpenSynapse.Windows.Devices;
using OpenSynapse.Windows.Protocols;
using Xunit;

namespace OpenSynapse.Core.Tests;

// These tests exercise production readers and recovery. The fake is not firmware evidence.
public sealed class BladeAuditTransactionTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AutomaticZeroTargetIsRestoredOnStopOrMissingSamples(bool missingSamples)
    {
        var transport = new StatefulBladeTransport(BladePerformanceMode.Custom, BladeFanMode.Automatic);
        var reader = new RazerDeviceTelemetryReader(transport, RazerDeviceRegistry.BuiltIn);
        var devices = new[] { Descriptor(0x02C6) };
        var samples = 0;
        var runtime = new BladeFanCurveRuntime(reader.ReadBladeFanControlStateAsync, reader.SetBladeFanTargetsAsync,
            async (d, m, c, g, t) => { await reader.SetBladeFanAsync(d, m, c, g, t); },
            (_, _, _) => ValueTask.FromResult<(double?, double?)>(
                Interlocked.Increment(ref samples) == 1 || !missingSamples ? (70, 70) : (null, null)),
            TimeSpan.FromMilliseconds(5));
        try
        {
            await runtime.StartAsync(devices, BladeFanCurve.CreateDefault());
            await transport.ManualTargetsWritten.Task.WaitAsync(TimeSpan.FromSeconds(5));
            if (missingSamples)
            {
                var error = await Assert.ThrowsAsync<InvalidOperationException>(
                    () => runtime.Completion.WaitAsync(TimeSpan.FromSeconds(5)));
                Assert.Contains("3 consecutive samples", error.Message);
                Assert.DoesNotContain("could not be restored", error.Message);
            }
            else await runtime.StopAsync();
            Assert.All(transport.FanModes, mode => Assert.Equal(BladeFanMode.Automatic, mode));
            Assert.DoesNotContain(transport.TargetWrites, rpm => rpm == 0);
        }
        finally
        {
            try { await runtime.DisposeAsync(); }
            catch (InvalidOperationException) when (missingSamples) { }
        }
    }

    [Theory]
    [InlineData(0x0239, BladePerformanceMode.BatterySaver, 5500)]
    [InlineData(0x0252, BladePerformanceMode.Balanced, 5500)]
    [InlineData(0x0259, BladePerformanceMode.Balanced, 5600)]
    public async Task FixedRuntimePreservesTheRealModelCeiling(ushort pid, BladePerformanceMode mode, int rpm)
    {
        var transport = new StatefulBladeTransport(mode, BladeFanMode.Automatic);
        var reader = new RazerDeviceTelemetryReader(transport, RazerDeviceRegistry.BuiltIn);
        await using var runtime = new BladeFanCurveRuntime(reader.ReadBladeFanControlStateAsync, reader.SetBladeFanTargetsAsync,
            async (d, m, c, g, t) => { await reader.SetBladeFanAsync(d, m, c, g, t); },
            (_, _, _) => ValueTask.FromResult<(double?, double?)>((null, null)), TimeSpan.FromHours(1));
        await runtime.StartFixedAsync([Descriptor(pid)], BladeFanMode.Manual, rpm);
        Assert.Contains(rpm, transport.TargetWrites);
        Assert.All(transport.TargetWrites, value => Assert.Equal(rpm, value));
        await runtime.StopAsync();
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => runtime.StartFixedAsync([Descriptor(pid)], BladeFanMode.Manual, rpm + 100));
    }

    [Fact]
    public async Task FixedCeilingDoesNotRelaxTheActualModeRange()
    {
        var reader = new RazerDeviceTelemetryReader(
            new StatefulBladeTransport(BladePerformanceMode.Balanced, BladeFanMode.Automatic), RazerDeviceRegistry.BuiltIn);
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () =>
            await reader.SetBladeFanAsync([Descriptor(0x0239)], BladeFanMode.Manual, 5500, 5500));
    }

    [Fact]
    public async Task DifferentFanZoneTargetsRemainAReadableTelemetryValue()
    {
        var transport = new StatefulBladeTransport(BladePerformanceMode.Custom, BladeFanMode.Manual);
        transport.Targets[0] = 3100;
        transport.Targets[1] = 2500;
        var (_, fan, device) = Readers(transport, 0x02C6);
        var blade = new RazerDeviceResolver(RazerDeviceRegistry.BuiltIn).FindReadyBlade([device])!;

        Assert.Equal(3100, await fan.ReadFanTargetRpmAsync(blade, default));
    }

    [Theory]
    [InlineData(BladePerformanceMode.Performance)]
    [InlineData(BladePerformanceMode.BatterySaver)]
    public async Task PerformanceSetterRejectsUnpublishedModeBeforeAnyQuery(BladePerformanceMode mode)
    {
        var transport = new StatefulBladeTransport(BladePerformanceMode.Custom, BladeFanMode.Automatic);
        var (power, _, device) = Readers(transport, 0x029F);
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await power.SetBladePerformanceModeAsync([device], mode));
        Assert.Empty(transport.Commands);
    }

    [Fact]
    public async Task Existing028CCustomModeAdmissionIsPreserved()
    {
        var transport = new StatefulBladeTransport(BladePerformanceMode.Balanced, BladeFanMode.Automatic);
        var (power, _, device) = Readers(transport, 0x028C);
        Assert.Equal(BladePerformanceMode.Custom,
            await power.SetBladePerformanceModeAsync([device], BladePerformanceMode.Custom));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FailedPresetRestoresMaxFanButPreservesOtherCurrentBits(bool cancel)
    {
        using var cancellation = new CancellationTokenSource();
        var transport = new StatefulBladeTransport(BladePerformanceMode.Custom, BladeFanMode.Automatic)
        {
            PowerMask = BladePowerModeProtocol.MaxFanBit,
            FailNextThermalWrite = true,
            CancelOnFailure = cancel ? cancellation : null,
        };
        var (power, _, device) = Readers(transport);
        if (cancel)
            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
                await power.SetBladePerformanceModeAsync([device], BladePerformanceMode.Balanced, cancellation.Token));
        else
        {
            var error = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await power.SetBladePerformanceModeAsync([device], BladePerformanceMode.Balanced));
            Assert.Contains("original state was restored", error.Message);
        }
        Assert.Equal((byte)(BladePowerModeProtocol.MaxFanBit | BladePowerModeProtocol.LocalDimmingBit), transport.PowerMask);
        Assert.Equal(0, transport.PowerWrites[0] & BladePowerModeProtocol.MaxFanBit);
        Assert.NotEqual(0, transport.PowerWrites[^1] & BladePowerModeProtocol.MaxFanBit);
        Assert.All(transport.PerformanceModes, value => Assert.Equal(BladePerformanceMode.Custom, value));
    }

    [Fact]
    public async Task FailedHyperBoostReadbackRemovesItsNewMaxFanPinAndRestoresLevels()
    {
        var transport = new StatefulBladeTransport(BladePerformanceMode.Balanced, BladeFanMode.Automatic)
        { FailThermalReadAfterPin = true };
        var (power, _, device) = Readers(transport);
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await power.SetBladePerformanceModeAsync([device], BladePerformanceMode.Hyperboost));
        Assert.Equal((byte)0, transport.PowerMask);
        Assert.Equal((byte)BladeCpuBoostMode.Medium, transport.CpuBoost);
        Assert.Equal((byte)BladeGpuBoostMode.Low, transport.GpuBoost);
        Assert.All(transport.PerformanceModes, value => Assert.Equal(BladePerformanceMode.Balanced, value));
    }

    [Fact]
    public async Task PerformanceTransactionAndMaxFanCannotInterleave()
    {
        var transport = new StatefulBladeTransport(BladePerformanceMode.Custom, BladeFanMode.Automatic)
        { PauseNextThermalWrite = true };
        var (power, _, device) = Readers(transport);
        var performance = power.SetBladePerformanceModeAsync([device], BladePerformanceMode.Custom).AsTask();
        await transport.WriteEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var maxFan = power.SetBladeMaxFanModeAsync([device], BladeMaxFanMode.Enabled).AsTask();
        Assert.False(maxFan.IsCompleted);
        Assert.DoesNotContain((0x07, 0x0F), transport.Commands);
        transport.ContinueWrite.TrySetResult();
        await Task.WhenAll(performance, maxFan).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(BladePowerModeProtocol.MaxFanBit, transport.PowerMask);
    }

    private static (BladePowerTelemetryReader Power, BladeFanTelemetryReader Fan, DeviceDescriptor Device) Readers(
        StatefulBladeTransport transport, ushort pid = 0x029F)
    {
        var device = Descriptor(pid);
        var resolver = new RazerDeviceResolver(RazerDeviceRegistry.BuiltIn);
        var client = new RazerCapabilityClient(transport);
        var fan = new BladeFanTelemetryReader(resolver, client);
        // Explicitly seed the precondition normally established by telemetry probes.
        var validation = new BladeValidationState
        { PerformancePath = device.Id, BoostPath = device.Id, PowerModePath = device.Id };
        return (new BladePowerTelemetryReader(resolver, client, validation, fan), fan, device);
    }

    private static DeviceDescriptor Descriptor(ushort pid) => new(
        $"blade-audit-{pid:X4}", "Software-only Blade", 0x1532, pid, DeviceAccessState.Available,
        DeviceCapabilityState.PendingValidation, 91, 1, 2,
        RazerDeviceRegistry.BuiltIn.Find(0x1532, pid)!.ProtocolFamily, DeviceCategory.Laptop);

    private sealed class StatefulBladeTransport : IRazerFeatureTransport
    {
        public StatefulBladeTransport(BladePerformanceMode mode, BladeFanMode fanMode)
        { PerformanceModes = [mode, mode]; FanModes = [fanMode, fanMode]; }
        public BladePerformanceMode[] PerformanceModes { get; }
        public BladeFanMode[] FanModes { get; }
        public int[] Targets { get; } = [0, 0];
        public byte PowerMask { get; set; }
        public byte CpuBoost { get; private set; } = (byte)BladeCpuBoostMode.Medium;
        public byte GpuBoost { get; private set; } = (byte)BladeGpuBoostMode.Low;
        public bool FailNextThermalWrite { get; set; }
        public bool FailThermalReadAfterPin { get; set; }
        public bool PauseNextThermalWrite { get; set; }
        public CancellationTokenSource? CancelOnFailure { get; init; }
        public TaskCompletionSource ManualTargetsWritten { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource WriteEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ContinueWrite { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public List<int> TargetWrites { get; } = [];
        public List<byte> PowerWrites { get; } = [];
        public List<(int Class, int Id)> Commands { get; } = [];

        public async Task<byte[]> QueryAsync(string devicePath, byte transactionId, byte dataSize,
            byte commandClass, byte commandId, ReadOnlyMemory<byte> arguments, TimeSpan deviceWait,
            CancellationToken cancellationToken, bool allowRemainingPacketsMismatch = false)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var args = arguments.ToArray();
            var response = RazerFeatureReport.CreateRequest(transactionId, dataSize, commandClass, commandId, args);
            response[1] = 0x02;
            var zone = args.Length > 1 ? args[1] - 1 : 0;
            var offset = RazerFeatureReport.ArgumentsOffset;
            Commands.Add((commandClass, commandId));
            switch (commandClass, commandId)
            {
                case (0x0D, 0x82):
                    if (FailThermalReadAfterPin && (PowerMask & BladePowerModeProtocol.MaxFanBit) != 0)
                    { FailThermalReadAfterPin = false; throw new IOException("Injected readback failure after pin."); }
                    response[offset + 2] = (byte)PerformanceModes[zone];
                    response[offset + 3] = (byte)FanModes[zone];
                    break;
                case (0x0D, 0x02):
                    PerformanceModes[zone] = (BladePerformanceMode)args[2];
                    FanModes[zone] = (BladeFanMode)args[3];
                    if (PauseNextThermalWrite)
                    {
                        PauseNextThermalWrite = false; WriteEntered.TrySetResult();
                        await ContinueWrite.Task.WaitAsync(cancellationToken);
                    }
                    if (FailNextThermalWrite)
                    {
                        FailNextThermalWrite = false;
                        PowerMask |= BladePowerModeProtocol.LocalDimmingBit;
                        if (CancelOnFailure is not null)
                        { CancelOnFailure.Cancel(); throw new OperationCanceledException(cancellationToken); }
                        throw new IOException("Injected partial thermal write failure.");
                    }
                    break;
                case (0x0D, 0x81): response[offset + 2] = (byte)(Targets[zone] / 100); break;
                case (0x0D, 0x01):
                    Assert.NotEqual(0, args[2]);
                    Targets[zone] = args[2] * 100; TargetWrites.Add(Targets[zone]);
                    if (TargetWrites.Count >= 4 && FanModes.All(mode => mode == BladeFanMode.Manual))
                        ManualTargetsWritten.TrySetResult();
                    break;
                case (0x07, 0x8F): response[offset] = PowerMask; break;
                case (0x07, 0x0F): PowerMask = args[0]; PowerWrites.Add(PowerMask); break;
                case (0x0D, 0x87):
                    response[offset + 2] = args[1] == BladeBoostProtocol.CpuCluster ? CpuBoost : GpuBoost; break;
                case (0x0D, 0x07):
                    if (args[1] == BladeBoostProtocol.CpuCluster) CpuBoost = args[2]; else GpuBoost = args[2]; break;
                default: throw new IOException($"No fake report for {commandClass:X2}:{commandId:X2}.");
            }
            response[89] = RazerFeatureReport.CalculateCrc(response);
            return response;
        }
    }
}
