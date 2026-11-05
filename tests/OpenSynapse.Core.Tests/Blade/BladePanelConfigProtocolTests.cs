using OpenSynapse.Core.Devices;
using OpenSynapse.Windows.Protocols;
using Xunit;

namespace OpenSynapse.Core.Tests;

public sealed class BladePanelConfigProtocolTests
{
    private const string GetMiniLedPanelResolutionFrame =
        "00001F000000010D8E00000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000008200";

    private const string SetMiniLedPanelResolutionUhdFrame =
        "00001F000000010D0E00000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000200";

    private const string SetMiniLedPanelResolutionFhdFrame =
        "00001F000000010D0E01000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000300";

    private const string GetVgaConfigureSettingFrame =
        "00001F000000020D8900000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000008600";

    private const string SetVgaConfigureSettingFrame =
        "00001F000000020D0900020000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000400";

    private const string GetVgaHardwareInformationFrame =
        "00001F000000020D8A00000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000008500";

    private const string GetSynapseUiSettingFrame =
        "00001F000000020D8B00000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000008400";

    private const string GetSkuHardwareConfigurationFrame =
        "00001F000000010D8F00000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000008300";

    [Fact]
    public void MiniLedPanelResolutionRequestsAreByteExact()
    {
        Assert.Equal(
            GetMiniLedPanelResolutionFrame,
            Convert.ToHexString(BladePanelConfigProtocol.CreateGetMiniLedPanelResolutionRequest()));
        Assert.Equal(
            SetMiniLedPanelResolutionUhdFrame,
            Convert.ToHexString(BladePanelConfigProtocol.CreateSetMiniLedPanelResolutionRequest(
                BladeNativeDisplayMode.Uhd)));
        Assert.Equal(
            SetMiniLedPanelResolutionFhdFrame,
            Convert.ToHexString(BladePanelConfigProtocol.CreateSetMiniLedPanelResolutionRequest(
                BladeNativeDisplayMode.Fhd)));
    }

    [Fact]
    public void VgaConfigureRequestsAreByteExact()
    {
        Assert.Equal(
            GetVgaConfigureSettingFrame,
            Convert.ToHexString(BladePanelConfigProtocol.CreateGetVgaConfigureSettingRequest()));
        Assert.Equal(
            SetVgaConfigureSettingFrame,
            Convert.ToHexString(BladePanelConfigProtocol.CreateSetVgaConfigureSettingRequest(0x02)));
    }

    [Fact]
    public void GetVgaHardwareInformationRequestIsByteExact() =>
        Assert.Equal(
            GetVgaHardwareInformationFrame,
            Convert.ToHexString(BladePanelConfigProtocol.CreateGetVgaHardwareInformationRequest()));

    [Fact]
    public void GetSynapseUiSettingRequestIsByteExact() =>
        Assert.Equal(
            GetSynapseUiSettingFrame,
            Convert.ToHexString(BladePanelConfigProtocol.CreateGetSynapseUiSettingRequest()));

    [Fact]
    public void GetSkuHardwareConfigurationRequestIsByteExact() =>
        Assert.Equal(
            GetSkuHardwareConfigurationFrame,
            Convert.ToHexString(BladePanelConfigProtocol.CreateGetSkuHardwareConfigurationRequest()));

    [Theory]
    [InlineData((byte)0x00, BladeNativeDisplayMode.Uhd)]
    [InlineData((byte)0x01, BladeNativeDisplayMode.Fhd)]
    public void MiniLedPanelResolutionParsesTheJsEnum(
        byte raw,
        BladeNativeDisplayMode expected)
    {
        var response = Response(
            BladePanelConfigProtocol.CreateGetMiniLedPanelResolutionRequest(),
            raw);

        Assert.Equal(
            expected,
            BladePanelConfigProtocol.ParseMiniLedPanelResolution(response));
    }

    [Fact]
    public void MiniLedPanelResolutionRejectsAnUnknownEnum()
    {
        var response = Response(
            BladePanelConfigProtocol.CreateGetMiniLedPanelResolutionRequest(),
            0x02);

        Assert.Throws<InvalidOperationException>(
            () => BladePanelConfigProtocol.ParseMiniLedPanelResolution(response));
    }

    [Fact]
    public void MiniLedPanelResolutionSetterRejectsAnUnknownEnum()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => BladePanelConfigProtocol.CreateSetMiniLedPanelResolutionRequest(
                (BladeNativeDisplayMode)0x02));
    }

    [Fact]
    public void VgaConfigureSettingParsesTotalAndRawValue()
    {
        var response = Response(
            BladePanelConfigProtocol.CreateGetVgaConfigureSettingRequest(),
            0x01,
            0x02);

        var setting = BladePanelConfigProtocol.ParseVgaConfigureSetting(response);

        Assert.Equal((byte)0x01, setting.Total);
        Assert.Equal((byte)0x02, setting.Value);
    }

    [Fact]
    public void VgaHardwareInformationParsesAllFourJsBits()
    {
        var response = Response(
            BladePanelConfigProtocol.CreateGetVgaHardwareInformationRequest(),
            0x01,
            0x0F);

        var information = BladePanelConfigProtocol.ParseVgaHardwareInformation(response);

        Assert.Equal((byte)0x0F, information.Raw);
        Assert.True(information.MicrosoftHybrid);
        Assert.True(information.DedicatedGpuOnly);
        Assert.True(information.DynamicDisplaySwitch);
        Assert.True(information.DedicatedIntegratedGpuOnly);
    }

    [Fact]
    public void SynapseUiSettingMasksTheSwitchInterfaceNibble()
    {
        var response = Response(
            BladePanelConfigProtocol.CreateGetSynapseUiSettingRequest(),
            0x01,
            0xA3);

        var setting = BladePanelConfigProtocol.ParseSynapseUiSetting(response);

        Assert.Equal((byte)0x01, setting.Total);
        Assert.Equal((byte)0xA3, setting.Raw);
        Assert.Equal((byte)0x03, setting.SwitchInterface);
    }

    [Theory]
    [InlineData((byte)0x00, false, false, false)]
    [InlineData((byte)0x01, true, false, false)]
    [InlineData((byte)0x02, false, true, false)]
    [InlineData((byte)0x04, false, false, true)]
    [InlineData((byte)0x07, true, true, true)]
    public void SkuHardwareConfigurationParsesTheJsBits(
        byte raw,
        bool dds,
        bool miniLedResolution,
        bool illegalBatterySupport)
    {
        var response = Response(
            BladePanelConfigProtocol.CreateGetSkuHardwareConfigurationRequest(),
            raw);

        var configuration = BladePanelConfigProtocol.ParseSkuHardwareConfiguration(response);

        Assert.Equal(dds, configuration.Dds);
        Assert.Equal(miniLedResolution, configuration.MiniLedResolution);
        Assert.Equal(illegalBatterySupport, configuration.IllegalBatterySupport);
        Assert.Equal(raw, configuration.Raw);
    }

    [Fact]
    public void MiniLedPanelResolutionRejectsATruncatedResponse()
    {
        var response = Response(
            BladePanelConfigProtocol.CreateGetMiniLedPanelResolutionRequest(),
            0x00);

        Assert.Throws<InvalidOperationException>(
            () => BladePanelConfigProtocol.ParseMiniLedPanelResolution(response.AsSpan(0, 20)));
    }

    [Fact]
    public void VgaConfigureSettingRejectsATruncatedResponse()
    {
        var response = Response(
            BladePanelConfigProtocol.CreateGetVgaConfigureSettingRequest(),
            0x01,
            0x02);

        Assert.Throws<InvalidOperationException>(
            () => BladePanelConfigProtocol.ParseVgaConfigureSetting(response.AsSpan(0, 20)));
    }

    [Fact]
    public void VgaHardwareInformationRejectsATruncatedResponse()
    {
        var response = Response(
            BladePanelConfigProtocol.CreateGetVgaHardwareInformationRequest(),
            0x01,
            0x0F);

        Assert.Throws<InvalidOperationException>(
            () => BladePanelConfigProtocol.ParseVgaHardwareInformation(response.AsSpan(0, 20)));
    }

    [Fact]
    public void SynapseUiSettingRejectsATruncatedResponse()
    {
        var response = Response(
            BladePanelConfigProtocol.CreateGetSynapseUiSettingRequest(),
            0x01,
            0x03);

        Assert.Throws<InvalidOperationException>(
            () => BladePanelConfigProtocol.ParseSynapseUiSetting(response.AsSpan(0, 20)));
    }

    [Fact]
    public void SkuHardwareConfigurationRejectsATruncatedResponse()
    {
        var response = Response(
            BladePanelConfigProtocol.CreateGetSkuHardwareConfigurationRequest(),
            0x07);

        Assert.Throws<InvalidOperationException>(
            () => BladePanelConfigProtocol.ParseSkuHardwareConfiguration(response.AsSpan(0, 20)));
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
