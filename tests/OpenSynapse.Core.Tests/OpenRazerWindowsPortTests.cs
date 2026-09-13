using OpenSynapse.Windows.Devices;
using OpenSynapse.Core.Devices;
using OpenSynapse.Core.Profiles;
using OpenSynapse.Windows.Lighting;
using OpenSynapse.Windows.Protocols;
using Xunit;

namespace OpenSynapse.Core.Tests;

public sealed class OpenRazerWindowsPortTests
{
    [Fact]
    public void WindowsFeatureReportWrapsTheOpenRazerLogicalReport()
    {
        var existingRequest = RazerFeatureReport.CreateRequest(
            0x1F,
            0x03,
            0x03,
            0x02,
            new byte[] { 0x01, 0x05, 0x06 });
        var logicalRequest = CreateOpenRazerLogicalRequest(
            0x1F,
            0x03,
            0x03,
            0x02,
            0x01, 0x05, 0x06);

        Assert.Equal(
            existingRequest,
            RazerFeatureReport.CreateRequestFromOpenRazerLogical(logicalRequest));
    }

    [Fact]
    public void LinuxTransportIndexDoesNotBecomeTheWindowsReportId()
    {
        const byte viperLinuxReportIndex = 0;
        const byte bladeLinuxReportIndex = 1;
        var viper = RazerFeatureReport.CreateRequestFromOpenRazerLogical(
            CreateOpenRazerLogicalRequest(0x1F, 0x02, 0x07, 0x80));
        var blade = RazerFeatureReport.CreateRequestFromOpenRazerLogical(
            CreateOpenRazerLogicalRequest(0xFF, 0x01, 0x03, 0x0A, 0x00));

        Assert.Equal(viperLinuxReportIndex, viper[0]);
        Assert.NotEqual(bladeLinuxReportIndex, blade[0]);
        Assert.Equal(0x00, blade[0]);
    }

    [Fact]
    public void OpenRazerLogicalImportRejectsCorruptCrc()
    {
        var logical = CreateOpenRazerLogicalRequest(0x1F, 0x02, 0x07, 0x80);
        logical[88] ^= 0x01;

        Assert.Throws<ArgumentException>(() =>
            RazerFeatureReport.CreateRequestFromOpenRazerLogical(logical));
    }

    [Fact]
    public void BusyResponsesAreAcceptedLikeOpenRazer()
    {
        Assert.True(RazerFeatureReport.IsAcceptedStatus(0x01));
        Assert.True(RazerFeatureReport.IsAcceptedStatus(0x02));
        Assert.False(RazerFeatureReport.IsAcceptedStatus(0x03));
    }

    [Fact]
    public void WindowsReportIdIsTransportMetadataOutsideTheLogicalReport()
    {
        var logical = CreateOpenRazerLogicalRequest(0xFF, 0x01, 0x03, 0x0A, 0x00);

        var windows = RazerFeatureReport.CreateRequestFromOpenRazerLogical(logical, reportId: 0x02);

        Assert.Equal(0x02, windows[0]);
        Assert.Equal(logical, windows[1..]);
        Assert.Equal(logical[88], windows[89]);
    }

    [Fact]
    public void Viper184BuildersMatchOpenRazerCommandPayloads()
    {
        AssertOpenRazerLogicalReport(ViperProduct184Protocol.CreateGetBatteryRequest(), 0x1F, 0x02, 0x07, 0x80);
        AssertOpenRazerLogicalReport(ViperProduct184Protocol.CreateGetPollingRateRequest(), 0x1F, 0x01, 0x00, 0x85);
        AssertOpenRazerLogicalReport(ViperProduct184Protocol.CreateSetPollingRateRequest(1000), 0x1F, 0x01, 0x00, 0x05, 0x01);
        AssertOpenRazerLogicalReport(ViperProduct184Protocol.CreateGetDpiRequest(), 0x1F, 0x07, 0x04, 0x85, 0x00);
        AssertOpenRazerLogicalReport(ViperProduct184Protocol.CreateGetDpiStagesRequest(), 0x1F, 0x26, 0x04, 0x86, 0x01);
        AssertOpenRazerLogicalReport(
            ViperProduct184Protocol.CreateSetDpiStagesRequest(new ViperDpiStagesState(
                1,
                [
                    new(1, 800, 800),
                    new(2, 1600, 1600),
                    new(3, 2400, 2400),
                    new(4, 3200, 3200),
                    new(5, 4000, 4000),
                ])),
            0x1F,
            0x26,
            0x04,
            0x06,
            0x01, 0x01, 0x05,
            0x00, 0x03, 0x20, 0x03, 0x20, 0x00, 0x00,
            0x01, 0x06, 0x40, 0x06, 0x40, 0x00, 0x00,
            0x02, 0x09, 0x60, 0x09, 0x60, 0x00, 0x00,
            0x03, 0x0C, 0x80, 0x0C, 0x80, 0x00, 0x00,
            0x04, 0x0F, 0xA0, 0x0F, 0xA0, 0x00, 0x00);
        AssertOpenRazerLogicalReport(ViperProduct184Protocol.CreateGetIdleTimeoutRequest(), 0x1F, 0x02, 0x07, 0x83);
        AssertOpenRazerLogicalReport(ViperProduct184Protocol.CreateSetIdleTimeoutRequest(300), 0x1F, 0x02, 0x07, 0x03, 0x01, 0x2C);
        AssertOpenRazerLogicalReport(ViperProduct184Protocol.CreateGetLowBatteryThresholdRequest(), 0x1F, 0x01, 0x07, 0x81);
        AssertOpenRazerLogicalReport(ViperProduct184Protocol.CreateSetLowBatteryThresholdRequest(20), 0x1F, 0x01, 0x07, 0x01, 0x33);
    }

    [Fact]
    public void Blade710BuildersMatchOpenRazerCommandPayloads()
    {
        AssertOpenRazerLogicalReport(BladeLightingProtocol.CreateOffRequest(), 0xFF, 0x01, 0x03, 0x0A, 0x00);
        AssertOpenRazerLogicalReport(BladeLightingProtocol.CreateWaveRequest(BladeWaveDirection.Left), 0xFF, 0x02, 0x03, 0x0A, 0x01, 0x01);
        AssertOpenRazerLogicalReport(BladeLightingProtocol.CreateSpectrumRequest(), 0xFF, 0x01, 0x03, 0x0A, 0x04);
        AssertOpenRazerLogicalReport(BladeLightingProtocol.CreateReactiveRequest(2, new(0x11, 0x22, 0x33)), 0xFF, 0x05, 0x03, 0x0A, 0x02, 0x02, 0x11, 0x22, 0x33);
        AssertOpenRazerLogicalReport(BladeLightingProtocol.CreateStaticRequest(new(0x11, 0x22, 0x33)), 0xFF, 0x04, 0x03, 0x0A, 0x06, 0x11, 0x22, 0x33);
        AssertOpenRazerLogicalReport(BladeLightingProtocol.CreateBreathingRandomRequest(), 0xFF, 0x08, 0x03, 0x0A, 0x03, 0x03, 0, 0, 0, 0, 0, 0);
        AssertOpenRazerLogicalReport(BladeLightingProtocol.CreateBreathingSingleRequest(new(0x11, 0x22, 0x33)), 0xFF, 0x08, 0x03, 0x0A, 0x03, 0x01, 0x11, 0x22, 0x33, 0, 0, 0);
        AssertOpenRazerLogicalReport(BladeLightingProtocol.CreateBreathingDualRequest(new(0x11, 0x22, 0x33), new(0x44, 0x55, 0x66)), 0xFF, 0x08, 0x03, 0x0A, 0x03, 0x02, 0x11, 0x22, 0x33, 0x44, 0x55, 0x66);
        AssertOpenRazerLogicalReport(BladeLightingProtocol.CreateStarlightRandomRequest(2), 0xFF, 0x01, 0x03, 0x0A, 0x19, 0x03, 0x02, 0, 0, 0, 0, 0, 0);
        AssertOpenRazerLogicalReport(BladeLightingProtocol.CreateStarlightSingleRequest(2, new(0x11, 0x22, 0x33)), 0xFF, 0x01, 0x03, 0x0A, 0x19, 0x01, 0x02, 0x11, 0x22, 0x33, 0, 0, 0);
        AssertOpenRazerLogicalReport(BladeLightingProtocol.CreateStarlightDualRequest(2, new(0x11, 0x22, 0x33), new(0x44, 0x55, 0x66)), 0xFF, 0x01, 0x03, 0x0A, 0x19, 0x02, 0x02, 0x11, 0x22, 0x33, 0x44, 0x55, 0x66);

        var blade = RazerDeviceRegistry.BuiltIn.Find(0x1532, 0x02C6)!;
        AssertOpenRazerLogicalReport(blade.GetRequiredCapability("keyboard-brightness.get").CreateRequest(), 0xFF, 0x02, 0x0E, 0x84, 0x01, 0x00);
        AssertOpenRazerLogicalReport(blade.GetRequiredCapability("keyboard-brightness.set").CreateRequest([0x01, 0x7F]), 0xFF, 0x02, 0x0E, 0x04, 0x01, 0x7F);
        AssertOpenRazerLogicalReport(BladeLogoProtocol.CreateGetPowerRequest(), 0xFF, 0x03, 0x03, 0x80, 0x01, 0x04, 0x00);
        AssertOpenRazerLogicalReport(BladeLogoProtocol.CreateSetPowerRequest(true), 0xFF, 0x03, 0x03, 0x00, 0x01, 0x04, 0x01);
    }

    [Fact]
    public async Task BladeStarlightSendsTheCompletePreparedReport()
    {
        var transport = new PreparedReportRecordingTransport();
        await using var controller = new BladeLightingController(transport);
        var device = new DeviceDescriptor(
            "blade",
            "Blade 16",
            0x1532,
            0x02C6,
            DeviceAccessState.Available,
            DeviceCapabilityState.PendingValidation,
            91,
            1,
            2,
            "blade-710");
        var first = new RazerRgb(0x11, 0x22, 0x33);
        var second = new RazerRgb(0x44, 0x55, 0x66);
        var effects = new[]
        {
            new BladeLightingEffect(BladeLightingMode.Starlight, StarlightSpeed: 2,
                StarlightColorMode: BladeStarlightColorMode.Random),
            new BladeLightingEffect(BladeLightingMode.Starlight, first, StarlightSpeed: 2,
                StarlightColorMode: BladeStarlightColorMode.Single),
            new BladeLightingEffect(BladeLightingMode.Starlight, first, SecondColor: second,
                StarlightSpeed: 2, StarlightColorMode: BladeStarlightColorMode.Dual),
        };
        var expected = new[]
        {
            BladeLightingProtocol.CreateStarlightRandomRequest(2),
            BladeLightingProtocol.CreateStarlightSingleRequest(2, first),
            BladeLightingProtocol.CreateStarlightDualRequest(2, first, second),
        };

        foreach (var effect in effects)
        {
            await controller.ApplyAsync([device], effect);
        }
        await controller.StopAsync();

        Assert.Equal(expected, transport.PreparedRequests);
    }

    [Fact]
    public void BladeNativeLightingParametersRoundTripThroughProfiles()
    {
        var reactive = BladeLightingProfileCodec.Parse(new LightingProfile
        {
            Effect = "reactive",
            Parameters = new() { ["color"] = "112233", ["speed"] = "4" },
        });
        Assert.Equal((byte)4, reactive.ReactiveSpeed);

        var starlight = BladeLightingProfileCodec.Parse(new LightingProfile
        {
            Effect = "starlight",
            Parameters = new()
            {
                ["color"] = "112233",
                ["color2"] = "445566",
                ["speed"] = "3",
                ["colorMode"] = "dual",
            },
        });
        Assert.Equal((byte)3, starlight.StarlightSpeed);
        Assert.Equal(BladeStarlightColorMode.Dual, starlight.StarlightColorMode);
        Assert.Equal(new RazerRgb(0x44, 0x55, 0x66), starlight.SecondColor);

        var encoded = BladeLightingProfileCodec.Create(starlight);
        Assert.Equal("3", encoded.Parameters["speed"]);
        Assert.Equal("dual", encoded.Parameters["colorMode"]);
        Assert.Equal("445566", encoded.Parameters["color2"]);
    }

    [Fact]
    public void BladeOpenRazerCustomRowPreservesTheDeclaredSizeQuirk()
    {
        var logical = CreateOpenRazerLogicalRequest(
            0xFF,
            0x46,
            0x03,
            0x0B,
            0xFF, 0x00, 0x00, 0x00, 0x11, 0x22, 0x33);

        var windows = RazerFeatureReport.CreateRequestFromOpenRazerLogical(logical);

        Assert.Equal(0x46, windows[6]);
        Assert.Equal(new byte[] { 0xFF, 0x00, 0x00, 0x00, 0x11, 0x22, 0x33 }, windows[9..16]);
        Assert.Equal(RazerFeatureReport.CalculateCrc(windows), windows[89]);
    }

    private sealed class PreparedReportRecordingTransport : IRazerFeatureTransport
    {
        internal List<byte[]> PreparedRequests { get; } = [];

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
            var response = new byte[RazerFeatureReport.Length];
            response[6] = 2;
            return Task.FromResult(response);
        }

        public Task<byte[]> QueryPreparedAsync(
            string devicePath,
            ReadOnlyMemory<byte> request,
            TimeSpan deviceWait,
            CancellationToken cancellationToken,
            bool allowRemainingPacketsMismatch = false)
        {
            PreparedRequests.Add(request.ToArray());
            return Task.FromResult(new byte[RazerFeatureReport.Length]);
        }
    }

    private static void AssertOpenRazerLogicalReport(
        byte[] windowsReport,
        byte transactionId,
        byte dataSize,
        byte commandClass,
        byte commandId,
        params byte[] arguments)
    {
        var logical = CreateOpenRazerLogicalRequest(
            transactionId,
            dataSize,
            commandClass,
            commandId,
            arguments);

        Assert.Equal(
            windowsReport,
            RazerFeatureReport.CreateRequestFromOpenRazerLogical(logical));
    }

    private static byte[] CreateOpenRazerLogicalRequest(
        byte transactionId,
        byte dataSize,
        byte commandClass,
        byte commandId,
        params byte[] arguments)
    {
        var logical = new byte[RazerFeatureReport.OpenRazerLogicalLength];
        logical[1] = transactionId;
        logical[5] = dataSize;
        logical[6] = commandClass;
        logical[7] = commandId;
        arguments.CopyTo(logical, 8);

        byte crc = 0;
        for (var index = 2; index <= 87; index++)
        {
            crc ^= logical[index];
        }

        logical[88] = crc;
        return logical;
    }
}
