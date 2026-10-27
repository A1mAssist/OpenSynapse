using OpenSynapse.Windows.Protocols;
using Xunit;

namespace OpenSynapse.Core.Tests;

public sealed class BladeReadOnlyProtocolsTests
{
    private const string FanInformationFrame =
        "00001F0000000C0D8300000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000008200";

    private const string DutyCycleFrame =
        "00001F000000030D8D01010000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000008300";

    private const string ThermalReadingFrame =
        "00001F000000500D850000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000D800";

    private const string LayoutSwitchFrame =
        "00001F0000000102A40000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000A700";

    private const string ExternalPowerFrame =
        "0000000000000100B70000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000B600";

    [Fact]
    public void FanInformationRequestIsByteExact() =>
        Assert.Equal(
            FanInformationFrame,
            Convert.ToHexString(BladeThermalProtocol.CreateGetFanInformationRequest()));

    [Fact]
    public void DutyCycleRequestIsByteExact() =>
        Assert.Equal(
            DutyCycleFrame,
            Convert.ToHexString(
                BladeThermalProtocol.CreateGetFanDutyCycleRequest(
                    BladeThermalProtocol.ProfileId,
                    BladeThermalProtocol.CpuFanId)));

    [Fact]
    public void ThermalReadingRequestIsByteExact() =>
        Assert.Equal(
            ThermalReadingFrame,
            Convert.ToHexString(BladeThermalProtocol.CreateGetThermalReadingRequest()));

    [Fact]
    public void LayoutSwitchRequestIsByteExact() =>
        Assert.Equal(
            LayoutSwitchFrame,
            Convert.ToHexString(BladeKeyboardLayoutProtocol.CreateGetLayoutSwitchRequest()));

    [Fact]
    public void ExternalPowerRequestIsByteExact() =>
        Assert.Equal(
            ExternalPowerFrame,
            Convert.ToHexString(BladePowerSupplyProtocol.CreateGetExternalPowerStatusRequest()));

    [Fact]
    public void FanInformationParsesTheJsOffsets()
    {
        var response = Response(
            BladeThermalProtocol.CreateGetFanInformationRequest(),
            0x00, 0x12, 0x34, 0x56, 0x78, 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07);

        var info = BladeThermalProtocol.ParseFanInformation(response);

        Assert.Equal((byte)0x00, info.ClassId);
        Assert.Equal((ushort)0x1234, info.Gpu);
        Assert.Equal((ushort)0x5678, info.Vram);
        Assert.Equal(
            new byte[] { 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07 },
            new[] { info.A, info.B, info.C, info.D, info.E, info.F, info.G });
        Assert.Equal((ushort)0x1234, info.ThermalSampling);
        Assert.Equal((byte)0x56, info.Hysteresis);
        Assert.Equal((ushort)0x7801, info.FanUpdate);
        Assert.Equal((ushort)0x0203, info.FanMaxDelta);
    }

    [Fact]
    public void FanInformationRejectsATruncatedResponse()
    {
        var response = Response(
            BladeThermalProtocol.CreateGetFanInformationRequest(),
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00);

        Assert.Throws<InvalidOperationException>(
            () => BladeThermalProtocol.ParseFanInformation(response.AsSpan(0, 20)));
    }

    [Fact]
    public void DutyCycleParsesTheSpeedPercent()
    {
        var response = Response(
            BladeThermalProtocol.CreateGetFanDutyCycleRequest(
                BladeThermalProtocol.ProfileId,
                BladeThermalProtocol.CpuFanId),
            BladeThermalProtocol.ProfileId,
            BladeThermalProtocol.CpuFanId,
            0x64);

        Assert.Equal(
            (byte)0x64,
            BladeThermalProtocol.ParseFanDutyCycle(
                response,
                BladeThermalProtocol.ProfileId,
                BladeThermalProtocol.CpuFanId));
    }

    [Fact]
    public void DutyCycleRejectsATruncatedResponse()
    {
        var response = Response(
            BladeThermalProtocol.CreateGetFanDutyCycleRequest(
                BladeThermalProtocol.ProfileId,
                BladeThermalProtocol.CpuFanId),
            BladeThermalProtocol.ProfileId,
            BladeThermalProtocol.CpuFanId,
            0x64);

        Assert.Throws<InvalidOperationException>(
            () => BladeThermalProtocol.ParseFanDutyCycle(
                response.AsSpan(0, 20),
                BladeThermalProtocol.ProfileId,
                BladeThermalProtocol.CpuFanId));
    }

    [Fact]
    public void ThermalReadingParsesTheCountAndRawValues()
    {
        var response = Response(
            BladeThermalProtocol.CreateGetThermalReadingRequest(),
            0x03, 0x2A, 0x2B, 0x2C);

        var reading = BladeThermalProtocol.ParseThermalReading(response);

        Assert.Equal((byte)0x03, reading.Total);
        Assert.Equal(new byte[] { 0x2A, 0x2B, 0x2C }, reading.Values.ToArray());
    }

    [Fact]
    public void ThermalReadingRejectsACountBeyondThePayload()
    {
        var response = Response(
            BladeThermalProtocol.CreateGetThermalReadingRequest(),
            0x50);

        Assert.Throws<InvalidOperationException>(
            () => BladeThermalProtocol.ParseThermalReading(response));
    }

    [Fact]
    public void ThermalReadingRejectsATruncatedResponse()
    {
        var response = Response(
            BladeThermalProtocol.CreateGetThermalReadingRequest(),
            0x03, 0x2A, 0x2B, 0x2C);

        Assert.Throws<InvalidOperationException>(
            () => BladeThermalProtocol.ParseThermalReading(response.AsSpan(0, 20)));
    }

    [Fact]
    public void LayoutSwitchParsesTheJsEnum()
    {
        var response = Response(
            BladeKeyboardLayoutProtocol.CreateGetLayoutSwitchRequest(),
            0x02);

        Assert.Equal(
            BladeKeyboardLayout.Windows,
            BladeKeyboardLayoutProtocol.ParseLayoutSwitch(response));
    }

    [Fact]
    public void LayoutSwitchRejectsAnUnknownValue()
    {
        var response = Response(
            BladeKeyboardLayoutProtocol.CreateGetLayoutSwitchRequest(),
            0x00);

        Assert.Throws<InvalidOperationException>(
            () => BladeKeyboardLayoutProtocol.ParseLayoutSwitch(response));
    }

    [Fact]
    public void LayoutSwitchRejectsATruncatedResponse()
    {
        var response = Response(
            BladeKeyboardLayoutProtocol.CreateGetLayoutSwitchRequest(),
            0x02);

        Assert.Throws<InvalidOperationException>(
            () => BladeKeyboardLayoutProtocol.ParseLayoutSwitch(response.AsSpan(0, 20)));
    }

    [Fact]
    public void ExternalPowerParsesTheRawStatus()
    {
        var response = Response(
            BladePowerSupplyProtocol.CreateGetExternalPowerStatusRequest(),
            0x01);

        Assert.Equal(
            (byte)0x01,
            BladePowerSupplyProtocol.ParseExternalPowerStatus(response));
    }

    [Fact]
    public void ExternalPowerRejectsATruncatedResponse()
    {
        var response = Response(
            BladePowerSupplyProtocol.CreateGetExternalPowerStatusRequest(),
            0x01);

        Assert.Throws<InvalidOperationException>(
            () => BladePowerSupplyProtocol.ParseExternalPowerStatus(response.AsSpan(0, 20)));
    }

    private static byte[] Response(byte[] request, params byte[] arguments)
    {
        var response = request.ToArray();
        response[1] = 0x02;
        arguments.AsSpan().CopyTo(response.AsSpan(RazerFeatureReport.ArgumentsOffset));
        response[89] = RazerFeatureReport.CalculateCrc(response);
        return response;
    }
}
