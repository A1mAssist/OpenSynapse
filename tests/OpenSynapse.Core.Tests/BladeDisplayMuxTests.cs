using OpenSynapse.Core.Displays;
using OpenSynapse.Windows.Devices;
using OpenSynapse.Windows.Protocols;
using Xunit;

namespace OpenSynapse.Core.Tests;

public sealed class BladeDisplayMuxTests
{
    [Fact]
    public void ManifestAdmitsTheBladeHidMuxReports()
    {
        var manifest = RazerDeviceRegistry.BuiltIn.Find(0x1532, 0x02C6);
        Assert.NotNull(manifest);

        var getter = manifest.GetRequiredCapability("display-mux.get");
        Assert.Equal((byte)0x1F, getter.TransactionId);
        Assert.Equal((byte)0x02, getter.MaximumDataSize);
        Assert.Equal((byte)0x0D, getter.CommandClass);
        Assert.Equal((byte)0x89, getter.CommandId);
        Assert.Equal(new byte[] { 0x00, 0x00 }, getter.Arguments.ToArray());

        var setter = manifest.GetRequiredCapability("display-mux.set");
        Assert.Equal((byte)0x09, setter.CommandId);

        var capabilities = manifest.GetRequiredCapability("display-mux-capabilities.get");
        Assert.Equal((byte)0x8A, capabilities.CommandId);
    }

    [Fact]
    public void Blade15Advanced2021UsesTheBundleVgaConfigureReports()
    {
        var manifest = RazerDeviceRegistry.BuiltIn.Find(0x1532, 0x0276);
        Assert.NotNull(manifest);

        var getter = manifest!.GetRequiredCapability("legacy-display-mux.get");
        Assert.Equal((byte)0x03, getter.MaximumDataSize);
        Assert.Equal((byte)0x90, getter.CommandId);
        Assert.Equal(new byte[] { 0x00, 0x00, 0x00 }, getter.Arguments.ToArray());

        var setter = manifest.GetRequiredCapability("legacy-display-mux.set");
        Assert.Equal((byte)0x03, setter.MaximumDataSize);
        Assert.Equal((byte)0x10, setter.CommandId);
        var setRequest = setter.CreateRequest([0x00, (byte)BladeDisplayMuxMode.Discrete]);
        Assert.Equal((byte)0x03, setRequest[6]);
        Assert.Equal((byte)0x00, setRequest[11]);

        var capabilities = manifest.GetRequiredCapability("legacy-display-mux-capabilities.get");
        Assert.Equal((byte)0x8B, capabilities.CommandId);
    }

    [Fact]
    public void GetRequestUsesTheVgaConfigureCommand()
    {
        var request = BladeDisplayMuxProtocol.CreateGetModeRequest();

        Assert.Equal((byte)0x1F, request[2]);
        Assert.Equal((byte)0x02, request[6]);
        Assert.Equal((byte)0x0D, request[7]);
        Assert.Equal((byte)0x89, request[8]);
        Assert.Equal((byte)0x00, request[9]);
        Assert.Equal((byte)0x00, request[10]);
    }

    [Theory]
    [InlineData(BladeDisplayMuxMode.Hybrid, (byte)0x00)]
    [InlineData(BladeDisplayMuxMode.Discrete, (byte)0x01)]
    [InlineData(BladeDisplayMuxMode.Dds, (byte)0x02)]
    public void SetRequestCarriesTheTargetMode(BladeDisplayMuxMode mode, byte raw)
    {
        var request = BladeDisplayMuxProtocol.CreateSetModeRequest(mode);

        Assert.Equal((byte)0x02, request[6]);
        Assert.Equal((byte)0x0D, request[7]);
        Assert.Equal((byte)0x09, request[8]);
        Assert.Equal((byte)0x00, request[9]);
        Assert.Equal(raw, request[10]);
    }

    [Theory]
    [InlineData((byte)0x00, BladeDisplayMuxMode.Hybrid)]
    [InlineData((byte)0x01, BladeDisplayMuxMode.Discrete)]
    [InlineData((byte)0x02, BladeDisplayMuxMode.Dds)]
    public void ParseModeReadsDataOne(byte raw, BladeDisplayMuxMode expected)
    {
        var request = BladeDisplayMuxProtocol.CreateGetModeRequest();
        var response = request.ToArray();
        response[1] = 0x02;
        response[RazerFeatureReport.ArgumentsOffset] = 0x01;
        response[RazerFeatureReport.ArgumentsOffset + 1] = raw;
        response[89] = RazerFeatureReport.CalculateCrc(response);

        Assert.Equal(expected, BladeDisplayMuxProtocol.ParseMode(response, request));
    }

    [Fact]
    public void ParseCapabilitiesReadsTheHardwareBits()
    {
        var request = BladeDisplayMuxProtocol.CreateGetCapabilitiesRequest();
        var response = request.ToArray();
        response[1] = 0x02;
        response[RazerFeatureReport.ArgumentsOffset] = 0x01;
        response[RazerFeatureReport.ArgumentsOffset + 1] = 0x06;
        response[89] = RazerFeatureReport.CalculateCrc(response);

        var capabilities = BladeDisplayMuxProtocol.ParseCapabilities(response, request);

        Assert.False(capabilities.HasFlag(BladeDisplayMuxCapabilities.Hybrid));
        Assert.True(capabilities.HasFlag(BladeDisplayMuxCapabilities.Discrete));
        Assert.True(capabilities.HasFlag(BladeDisplayMuxCapabilities.DynamicDisplaySwitch));
        Assert.Equal(
            new[] { BladeDisplayMuxMode.Dds, BladeDisplayMuxMode.Discrete },
            BladeDisplayMuxModes.GetSupportedModes(capabilities));
        Assert.True(BladeDisplayMuxModes.CanSwitch(capabilities));
    }

    [Fact]
    public void DdsIsMaskedWhenWritesAreNotAdmitted()
    {
        var capabilities =
            BladeDisplayMuxCapabilities.Hybrid |
            BladeDisplayMuxCapabilities.Discrete |
            BladeDisplayMuxCapabilities.DynamicDisplaySwitch;

        var admitted = BladeDisplayMuxModes.AdmittedCapabilities(capabilities, allowDds: false);

        Assert.False(admitted.HasFlag(BladeDisplayMuxCapabilities.DynamicDisplaySwitch));
        Assert.Equal(
            new[] { BladeDisplayMuxMode.Hybrid, BladeDisplayMuxMode.Discrete },
            BladeDisplayMuxModes.GetSupportedModes(admitted, allowDds: false));
        Assert.True(BladeDisplayMuxModes.CanSwitch(admitted, allowDds: false));
    }

    [Fact]
    public void UnknownModeIsRejected()
    {
        var request = BladeDisplayMuxProtocol.CreateGetModeRequest();
        var response = request.ToArray();
        response[1] = 0x02;
        response[RazerFeatureReport.ArgumentsOffset] = 0x01;
        response[RazerFeatureReport.ArgumentsOffset + 1] = 0x03;
        response[89] = RazerFeatureReport.CalculateCrc(response);

        Assert.Throws<InvalidOperationException>(
            () => BladeDisplayMuxProtocol.ParseMode(response, request));
    }
}
