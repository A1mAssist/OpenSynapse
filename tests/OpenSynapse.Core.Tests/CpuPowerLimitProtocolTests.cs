using System.Text.Json;
using OpenSynapse.Core.Devices;
using OpenSynapse.Windows.Lifecycle;
using Xunit;

namespace OpenSynapse.Core.Tests;

public sealed class CpuPowerLimitProtocolTests
{
    [Fact]
    public void APowerLimitStatusRequestRoutesToTheStatusAction()
    {
        var request = Request(DedicatedGpuModeServiceProtocol.CpuPowerLimitStatusOperation);

        Assert.Equal(
            DedicatedGpuModeServiceAction.CpuPowerLimitStatus,
            DedicatedGpuModeServiceRequestParser.Parse(request));
    }

    [Fact]
    public void ASetRequestWithBothWallsRoutesToTheSetAction()
    {
        var request = Request(
            DedicatedGpuModeServiceProtocol.SetCpuPowerLimitOperation,
            pl1: 45,
            pl2: 60);

        Assert.Equal(
            DedicatedGpuModeServiceAction.SetCpuPowerLimit,
            DedicatedGpuModeServiceRequestParser.Parse(request));
    }

    [Fact]
    public void ASetRequestMissingEitherWallIsUnsupported()
    {
        var missingPl2 = Request(
            DedicatedGpuModeServiceProtocol.SetCpuPowerLimitOperation,
            pl1: 45);
        var missingPl1 = Request(
            DedicatedGpuModeServiceProtocol.SetCpuPowerLimitOperation,
            pl2: 60);

        Assert.Equal(
            DedicatedGpuModeServiceAction.Unsupported,
            DedicatedGpuModeServiceRequestParser.Parse(missingPl2));
        Assert.Equal(
            DedicatedGpuModeServiceAction.Unsupported,
            DedicatedGpuModeServiceRequestParser.Parse(missingPl1));
    }

    [Fact]
    public void ARestoreRequestRoutesWithOrWithoutWalls()
    {
        var firmware = Request(DedicatedGpuModeServiceProtocol.RestoreCpuPowerLimitOperation);
        var explicitWalls = Request(
            DedicatedGpuModeServiceProtocol.RestoreCpuPowerLimitOperation,
            pl1: 150,
            pl2: 175);

        Assert.Equal(
            DedicatedGpuModeServiceAction.RestoreCpuPowerLimit,
            DedicatedGpuModeServiceRequestParser.Parse(firmware));
        Assert.Equal(
            DedicatedGpuModeServiceAction.RestoreCpuPowerLimit,
            DedicatedGpuModeServiceRequestParser.Parse(explicitWalls));
    }

    [Fact]
    public void ARestoreRequestWithHalfAWallIsUnsupported()
    {
        var missingPl2 = Request(
            DedicatedGpuModeServiceProtocol.RestoreCpuPowerLimitOperation,
            pl1: 150);
        var missingPl1 = Request(
            DedicatedGpuModeServiceProtocol.RestoreCpuPowerLimitOperation,
            pl2: 175);

        Assert.Equal(
            DedicatedGpuModeServiceAction.Unsupported,
            DedicatedGpuModeServiceRequestParser.Parse(missingPl2));
        Assert.Equal(
            DedicatedGpuModeServiceAction.Unsupported,
            DedicatedGpuModeServiceRequestParser.Parse(missingPl1));
    }

    [Fact]
    public void APowerLimitRequestWithTheWrongVersionIsUnsupported()
    {
        var request = Request(
            DedicatedGpuModeServiceProtocol.SetCpuPowerLimitOperation,
            pl1: 45,
            pl2: 60,
            version: DedicatedGpuModeServiceProtocol.Version + 1);

        Assert.Equal(
            DedicatedGpuModeServiceAction.Unsupported,
            DedicatedGpuModeServiceRequestParser.Parse(request));
    }

    [Fact]
    public void AResponseWithoutAPowerLimitStaysNullForOtherOperations()
    {
        var response = new DedicatedGpuModeServiceResponse(
            DedicatedGpuModeServiceProtocol.Version,
            Handled: true,
            DedicatedGpuSwitchOutcome.Success,
            ExitCode: 0,
            Status: null,
            Error: null,
            CoreOffsetMhz: 25);

        Assert.Null(response.PowerLimit);

        var roundTripped = JsonSerializer.Deserialize<DedicatedGpuModeServiceResponse>(
            JsonSerializer.Serialize(response));
        Assert.Null(roundTripped!.PowerLimit);
    }

    [Fact]
    public void APowerLimitReportSurvivesTheWireFormat()
    {
        var report = new CpuPowerLimitReport(
            CpuPowerLimitAvailability.Available,
            null,
            45,
            60,
            150,
            175,
            CpuPowerLimit.MaximumWatts,
            IsLocked: false);
        var response = new DedicatedGpuModeServiceResponse(
            DedicatedGpuModeServiceProtocol.Version,
            Handled: true,
            DedicatedGpuSwitchOutcome.Success,
            ExitCode: 0,
            Status: null,
            Error: null,
            PowerLimit: report);

        var roundTripped = JsonSerializer.Deserialize<DedicatedGpuModeServiceResponse>(
            JsonSerializer.Serialize(response));

        Assert.Equal(report, roundTripped!.PowerLimit);
    }

    [Fact]
    public void ClampingKeepsPl2AtOrAbovePl1()
    {
        var crossed = new CpuPowerLimit(95, 15, 0, 0).Clamp();
        var aboveCeiling = new CpuPowerLimit(150, 175, 0, 0).Clamp();

        Assert.Equal(95, crossed.Pl1Watts);
        Assert.Equal(95, crossed.Pl2Watts);
        Assert.Equal(CpuPowerLimit.MaximumWatts, aboveCeiling.Pl1Watts);
        Assert.Equal(CpuPowerLimit.MaximumWatts, aboveCeiling.Pl2Watts);
    }

    private static DedicatedGpuModeServiceRequest Request(
        string operation,
        double? pl1 = null,
        double? pl2 = null,
        int version = DedicatedGpuModeServiceProtocol.Version) =>
        new(
            version,
            operation,
            Enabled: null,
            Pl1Watts: pl1,
            Pl2Watts: pl2);
}
