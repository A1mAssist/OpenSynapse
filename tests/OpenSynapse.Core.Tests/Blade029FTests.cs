using OpenSynapse.Core.Devices;
using OpenSynapse.Windows.Devices;
using OpenSynapse.Windows.Protocols;
using Xunit;

namespace OpenSynapse.Core.Tests;

/// <summary>
/// Blade 16 (2023) RZ09-0483. Its middleware bundle is the source for every contract
/// pinned here: the fixed fan speed it drives from each performance mode, the charge
/// limiter report layout, and the display MUX reports.
/// </summary>
public sealed class Blade029FTests
{
    [Fact]
    public void TheBlade16_2023ManifestCarriesTheDisplayMuxReports()
    {
        var manifest = RazerDeviceRegistry.BuiltIn.Find(0x1532, 0x029F);

        Assert.NotNull(manifest);
        Assert.Equal("blade-671", manifest!.ProtocolFamily);

        var getter = manifest.GetRequiredCapability("display-mux.get");
        Assert.Equal((byte)0x1F, getter.TransactionId);
        Assert.Equal((byte)0x02, getter.MaximumDataSize);
        Assert.Equal((byte)0x0D, getter.CommandClass);
        Assert.Equal((byte)0x89, getter.CommandId);
        Assert.Equal(new byte[] { 0x00, 0x00 }, getter.Arguments.ToArray());

        Assert.Equal((byte)0x09, manifest.GetRequiredCapability("display-mux.set").CommandId);
        Assert.Equal(
            (byte)0x8A,
            manifest.GetRequiredCapability("display-mux-capabilities.get").CommandId);
    }

    /// <summary>
    /// The MUX reports are the same bytes the 710 family uses, but the Blade 14 (2022)
    /// and the Blade 15 share the blade-671 family and their middleware bundles never
    /// answered the VGA reports, so they must not inherit the switch.
    /// </summary>
    [Theory]
    [InlineData(0x029F, true)]
    [InlineData(0x028C, false)]
    [InlineData(0x0245, false)]
    public void OnlyTheBlade16_2023CarriesTheDisplayMuxReports(ushort productId, bool expected)
    {
        var manifest = RazerDeviceRegistry.BuiltIn.Find(0x1532, productId);

        Assert.NotNull(manifest);
        Assert.Equal(expected, manifest!.Capabilities.ContainsKey("display-mux.get"));
        Assert.Equal(expected, manifest.Capabilities.ContainsKey("display-mux.set"));
        Assert.Equal(expected, manifest.Capabilities.ContainsKey("display-mux-capabilities.get"));
    }

    /// <summary>
    /// The official middleware reads the charge limiter as a low-seven-bit threshold
    /// with the top bit as the enabled flag, so 0x4B is a disabled 75 percent limiter
    /// and every code OpenSynapse already knew keeps the value it reported.
    /// </summary>
    [Theory]
    [InlineData((byte)0xB2, 50)]
    [InlineData((byte)0xB7, 55)]
    [InlineData((byte)0xBC, 60)]
    [InlineData((byte)0xC1, 65)]
    [InlineData((byte)0xC6, 70)]
    [InlineData((byte)0xCB, 75)]
    [InlineData((byte)0xD0, 80)]
    [InlineData((byte)0x50, 100)]
    [InlineData((byte)0x4B, 100)]
    public void TheChargeLimiterReportDecodesTheThresholdAndTheEnabledFlag(
        byte raw,
        int expected) =>
        Assert.Equal(expected, RazerDeviceTelemetryReader.DecodeBladeChargeLimit(raw));

    [Fact]
    public void AnEnabledThresholdTheFirmwareDoesNotOfferIsRejected()
    {
        Assert.Throws<InvalidOperationException>(
            () => RazerDeviceTelemetryReader.DecodeBladeChargeLimit(0xC8));
    }

    /// <summary>
    /// The Blade 16 (2023) official modes reach 5200 RPM and Custom reaches 5300, but
    /// the shared fan-target report takes the same rpm/100 value the official middleware
    /// writes, so the slider and the write guard reach the ceiling the model reports.
    /// </summary>
    [Fact]
    public void TheFixedFanSliderReachesTheCeilingTheFirmwareReports()
    {
        const ushort productId = 0x029F;

        Assert.Equal(5300, BladeModelCapabilities.MaximumFanTargetRpmFor(productId));
        Assert.Equal(5300, BladeModelCapabilities.MaximumWritableFanTargetRpmFor(productId));
    }

    /// <summary>
    /// A target the firmware already stored above the write ceiling must still read
    /// back, so the readback ceiling stays the model maximum.
    /// </summary>
    [Fact]
    public void AStoredTargetAboveTheWriteCeilingStillReadsBack()
    {
        var request = BladeFanProtocol.CreateGetTargetRequest(BladeFanProtocol.ZoneCpu);
        var response = request.ToArray();
        response[1] = 0x02;
        response[RazerFeatureReport.ArgumentsOffset + 2] = 0x35;
        response[89] = RazerFeatureReport.CalculateCrc(response);

        Assert.Equal(
            5300,
            BladeFanProtocol.ParseTarget(
                response,
                BladeFanProtocol.ZoneCpu,
                request,
                BladeModelCapabilities.MaximumFanTargetRpmFor(0x029F)));
    }

    /// <summary>
    /// The fixed speed rides the shared fan-target report, so the request keeps the
    /// 100 RPM step the firmware stores.
    /// </summary>
    [Fact]
    public void TheFixedFanTargetUsesTheSharedHundredRpmStep()
    {
        var request = BladeFanProtocol.CreateSetTargetRequest(BladeFanProtocol.ZoneCpu, 4200);

        Assert.Equal((byte)0x1F, request[2]);
        Assert.Equal((byte)0x03, request[6]);
        Assert.Equal((byte)0x0D, request[7]);
        Assert.Equal((byte)0x01, request[8]);
        Assert.Equal((byte)0x00, request[RazerFeatureReport.ArgumentsOffset]);
        Assert.Equal((byte)0x01, request[RazerFeatureReport.ArgumentsOffset + 1]);
        Assert.Equal((byte)0x2A, request[RazerFeatureReport.ArgumentsOffset + 2]);
    }

    /// <summary>
    /// The official middleware writes the fan speed as rpm/100, so the top speed the
    /// official Performance mode asks for still fits in the report's speed byte.
    /// </summary>
    [Fact]
    public void TheTopOfficialSpeedFitsInTheFanTargetByte()
    {
        var request = BladeFanProtocol.CreateSetTargetRequest(
            BladeFanProtocol.ZoneCpu,
            5400,
            BladeModelCapabilities.MaximumWritableFanTargetRpmFor(0x02C6));

        Assert.Equal((byte)0x36, request[RazerFeatureReport.ArgumentsOffset + 2]);
    }

    /// <summary>
    /// The Blade 16 (2023) firmware reports performance mode 0x01 on a chassis whose
    /// own mode table omits it. Failing that read used to hide the whole fan-control
    /// card, because the performance mode and the fan mode share one report.
    /// </summary>
    [Fact]
    public async Task AFanControlSurvivesAPerformanceModeTheModelDoesNotPublish()
    {
        var telemetry = await new RazerDeviceTelemetryReader(
            new Blade029FTransport(performanceMode: 0x01),
            RazerDeviceRegistry.BuiltIn).ReadAsync([Blade029FDescriptor()]);

        Assert.Empty(telemetry.Errors);
        Assert.Equal(BladePerformanceMode.Gaming, telemetry.BladePerformanceMode);
        Assert.Equal(BladeFanMode.Automatic, telemetry.BladeFanMode);
    }

    /// <summary>
    /// The reported byte has to survive a fan change: writing the named mode back
    /// would move the machine from the mode the firmware reported to another one.
    /// </summary>
    [Fact]
    public async Task AFanChangeKeepsThePerformanceByteTheFirmwareReported()
    {
        var transport = new Blade029FTransport(performanceMode: 0x01);
        var reader = new RazerDeviceTelemetryReader(transport, RazerDeviceRegistry.BuiltIn);
        var devices = new[] { Blade029FDescriptor() };
        await reader.ReadAsync(devices);

        await reader.SetBladeFanAsync(devices, BladeFanMode.Manual, 4600, 4600);

        Assert.Equal((byte)0x01, transport.LastPerformanceByte);
    }

    /// <summary>
    /// A performance byte no contract knows must still leave the fan controls usable:
    /// the two travel in one report but they are separate facts.
    /// </summary>
    [Fact]
    public async Task AnUnknownPerformanceByteStillLeavesTheFanControlsUsable()
    {
        var telemetry = await new RazerDeviceTelemetryReader(
            new Blade029FTransport(performanceMode: 0x0A),
            RazerDeviceRegistry.BuiltIn).ReadAsync([Blade029FDescriptor()]);

        Assert.Null(telemetry.BladePerformanceMode);
        Assert.Equal(BladeFanMode.Automatic, telemetry.BladeFanMode);
        Assert.Contains(telemetry.Errors, error => error.StartsWith("Performance mode:", StringComparison.Ordinal));
    }

    /// <summary>
    /// A mode the model does not publish is not a mode change, so the CPU/GPU level
    /// and Max Fan readings the firmware reported stay on screen.
    /// </summary>
    [Theory]
    [InlineData(0x029F, BladePerformanceMode.Performance, false)]
    [InlineData(0x029F, BladePerformanceMode.BatterySaver, false)]
    [InlineData(0x029F, BladePerformanceMode.Custom, true)]
    [InlineData(0x02C6, BladePerformanceMode.Performance, true)]
    public void AModeTheModelDoesNotPublishIsNotAModeChange(
        ushort productId,
        BladePerformanceMode mode,
        bool published)
    {
        Assert.Equal(
            published,
            BladeModelCapabilities.PublishesPerformanceMode(productId, mode));
    }

    private static DeviceDescriptor Blade029FDescriptor() =>
        new(
            "blade-671-test",
            @"\\?\hid#vid_1532&pid_029f&mi_02#test#{00000000-0000-0000-0000-000000000000}",
            0x1532,
            0x029F,
            DeviceAccessState.Available,
            DeviceCapabilityState.PendingValidation,
            91,
            1,
            2,
            "blade-671",
            DeviceCategory.Laptop);

    /// <summary>
    /// A Blade 16 (2023) EC that answers the thermal report with the given
    /// performance byte.
    /// </summary>
    private sealed class Blade029FTransport : IRazerFeatureTransport
    {
        private readonly byte _performanceMode;
        private BladeFanMode _fanMode = BladeFanMode.Automatic;
        private int _cpuTargetRpm = 4100;
        private int _gpuTargetRpm = 4100;

        public Blade029FTransport(byte performanceMode) => _performanceMode = performanceMode;

        public byte LastPerformanceByte { get; private set; }

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
            response[1] = 0x02;
            switch (commandClass, commandId)
            {
                case (0x0D, 0x02):
                    LastPerformanceByte = arguments.Span[2];
                    _fanMode = (BladeFanMode)arguments.Span[3];
                    break;
                case (0x0D, 0x82):
                    response[RazerFeatureReport.ArgumentsOffset + 1] = arguments.Span[1];
                    response[RazerFeatureReport.ArgumentsOffset + 2] = _performanceMode;
                    response[RazerFeatureReport.ArgumentsOffset + 3] = (byte)_fanMode;
                    break;
                case (0x0D, 0x81):
                    response[RazerFeatureReport.ArgumentsOffset + 1] = arguments.Span[1];
                    var stored = arguments.Span[1] == BladeFanProtocol.ZoneCpu
                        ? _cpuTargetRpm
                        : _gpuTargetRpm;
                    response[RazerFeatureReport.ArgumentsOffset + 2] =
                        (byte)(stored / BladeFanLimits.StepRpm);
                    break;
                case (0x0D, 0x01):
                    if (arguments.Span[1] == BladeFanProtocol.ZoneCpu)
                    {
                        _cpuTargetRpm = arguments.Span[2] * BladeFanLimits.StepRpm;
                    }
                    else
                    {
                        _gpuTargetRpm = arguments.Span[2] * BladeFanLimits.StepRpm;
                    }

                    break;
                case (0x0D, 0x88):
                    response[RazerFeatureReport.ArgumentsOffset + 2] = 30;
                    break;
                case (0x0D, 0x87):
                    response[RazerFeatureReport.ArgumentsOffset + 1] = arguments.Span[1];
                    response[RazerFeatureReport.ArgumentsOffset + 2] = 1;
                    break;
                case (0x07, 0x92):
                    response[RazerFeatureReport.ArgumentsOffset] = 0xD0;
                    break;
                case (0x07, 0x8F):
                    response[RazerFeatureReport.ArgumentsOffset] = 0;
                    break;
                case (0x07, 0x8C):
                    response[RazerFeatureReport.ArgumentsOffset] = 0x0C;
                    response[RazerFeatureReport.ArgumentsOffset + 1] = 0x0C;
                    break;
                case (0x03, 0x83):
                    response[RazerFeatureReport.ArgumentsOffset + 2] = 0xC8;
                    break;
                case (0x0F, 0x82):
                    response[RazerFeatureReport.ArgumentsOffset] = 0x01;
                    response[RazerFeatureReport.ArgumentsOffset + 1] = 0x05;
                    response[RazerFeatureReport.ArgumentsOffset + 2] = 0x00;
                    break;
                default:
                    throw new InvalidOperationException(
                        $"Unexpected Blade 029F command {commandClass:X2}:{commandId:X2}.");
            }

            response[89] = RazerFeatureReport.CalculateCrc(response);
            return Task.FromResult(response);
        }
    }
}
