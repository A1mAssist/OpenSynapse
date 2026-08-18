using OpenSynapse.Core.Devices;
using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.Core.Tests;

public sealed class BladeProduct710ProtocolTests
{
    [Theory]
    [InlineData(BladeNativeDisplayMode.Uhd, 0x00)]
    [InlineData(BladeNativeDisplayMode.Fhd, 0x01)]
    public void BuildsAndParsesNativeDisplayMode(BladeNativeDisplayMode mode, byte raw)
    {
        var get = BladeProduct710Protocol.CreateGetNativeDisplayModeRequest();
        var set = BladeProduct710Protocol.CreateSetNativeDisplayModeRequest(mode);

        AssertHeader(get, 0x01, 0x0D, 0x8E, 0x00);
        AssertHeader(set, 0x01, 0x0D, 0x0E, raw);
        Assert.Equal(mode, BladeProduct710Protocol.ParseNativeDisplayMode(
            CreateResponse(0x01, 0x0D, 0x8E, raw)));
    }

    [Fact]
    public void ParsesSkuHardwareConfigurationBits()
    {
        var request = BladeProduct710Protocol.CreateGetSkuHardwareConfigurationRequest();
        AssertHeader(request, 0x01, 0x0D, 0x8F, 0x00);

        var configuration = BladeProduct710Protocol.ParseSkuHardwareConfiguration(
            CreateResponse(0x01, 0x0D, 0x8F, 0x07));

        Assert.True(configuration.Dds);
        Assert.True(configuration.MiniLedResolution);
        Assert.True(configuration.IllegalBatterySupport);
        Assert.Equal(0x07, configuration.Raw);
    }

    [Fact]
    public void RejectsUnknownNativeDisplayMode()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            BladeProduct710Protocol.CreateSetNativeDisplayModeRequest((BladeNativeDisplayMode)2));
        Assert.Throws<InvalidOperationException>(() =>
            BladeProduct710Protocol.ParseNativeDisplayMode(
                CreateResponse(0x01, 0x0D, 0x8E, 0x02)));
    }

    [Fact]
    public void AcceptsKnownDisplayRemainingPacketQuirkButStillRequiresValidCrc()
    {
        var mode = CreateResponse(0x01, 0x0D, 0x8E, 0x00);
        mode[4] = 0x01;
        mode[89] = RazerFeatureReport.CalculateCrc(mode);
        Assert.Equal(BladeNativeDisplayMode.Uhd, BladeProduct710Protocol.ParseNativeDisplayMode(mode));

        var sku = CreateResponse(0x01, 0x0D, 0x8F, 0x00);
        sku[4] = 0x01;
        sku[89] = RazerFeatureReport.CalculateCrc(sku);
        Assert.Equal(0, BladeProduct710Protocol.ParseSkuHardwareConfiguration(sku).Raw);

        sku[89] ^= 0xFF;
        Assert.Throws<InvalidOperationException>(() =>
            BladeProduct710Protocol.ParseSkuHardwareConfiguration(sku));
    }

    private static byte[] CreateResponse(
        byte dataSize,
        byte commandClass,
        byte commandId,
        params byte[] arguments)
    {
        var response = RazerFeatureReport.CreateRequest(
            BladeProduct710Protocol.TransactionId,
            dataSize,
            commandClass,
            commandId,
            arguments);
        response[1] = 0x02;
        response[89] = RazerFeatureReport.CalculateCrc(response);
        return response;
    }

    private static void AssertHeader(
        byte[] report,
        byte dataSize,
        byte commandClass,
        byte commandId,
        byte argument)
    {
        Assert.Equal(BladeProduct710Protocol.TransactionId, report[2]);
        Assert.Equal(dataSize, report[6]);
        Assert.Equal(commandClass, report[7]);
        Assert.Equal(commandId, report[8]);
        Assert.Equal(argument, report[RazerFeatureReport.ArgumentsOffset]);
        Assert.Equal(RazerFeatureReport.CalculateCrc(report), report[89]);
    }
}
