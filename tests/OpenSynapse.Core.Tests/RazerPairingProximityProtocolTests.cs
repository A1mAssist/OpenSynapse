using OpenSynapse.Windows.Protocols;
using Xunit;

namespace OpenSynapse.Core.Tests;

public sealed class RazerPairingProximityProtocolTests
{
    [Fact]
    public void PairingFramesMatchTheMiddlewareTuplesAndPayloads()
    {
        AssertFrame(
            RazerPairingProtocol.CreateSetDevicePairingModeRequest(0x21, 0x02, 0x1234),
            0x21,
            0x03,
            0x00,
            0x41,
            0x02,
            0x12,
            0x34);
        AssertFrame(
            RazerPairingProtocol.CreateSetDeviceUnpairRequest(0x22, 0xBEEF),
            0x22,
            0x02,
            0x00,
            0x42,
            0xBE,
            0xEF);
        AssertFrame(
            RazerPairingProtocol.CreateGetMultipleDeviceWirelessConnectionStatusV2Request(0x23),
            0x23,
            0x50,
            0x00,
            0xBF);
        AssertFrame(
            RazerPairingProtocol.CreateGetActiveWirelessConnectionStateRequest(0x24),
            0x24,
            0x02,
            0x00,
            0xC7,
            0x00,
            0x00);
        AssertFrame(
            RazerPairingProtocol.CreateSetActiveWirelessConnectionStateRequest(0x25, 0x03, 0x01),
            0x25,
            0x02,
            0x00,
            0x47,
            0x03,
            0x01);
    }

    [Fact]
    public void PairingParsersReadTheMiddlewareResponseLayouts()
    {
        var pairingRequest = RazerPairingProtocol.CreateSetDevicePairingModeRequest(0x31, 0x02, 0x1234);
        var pairingMode = RazerPairingProtocol.ParsePairingMode(
            Response(0x31, 0x01, 0x00, 0x41, 0x02),
            pairingRequest);

        var statusRequest = RazerPairingProtocol.CreateGetMultipleDeviceWirelessConnectionStatusV2Request(0x32);
        var statuses = RazerPairingProtocol.ParseMultipleDeviceWirelessConnectionStatusV2(
            Response(0x32, 0x07, 0x00, 0xBF, 0x02, 0x01, 0x12, 0x34, 0x00, 0xAB, 0xCD),
            statusRequest);

        var activeRequest = RazerPairingProtocol.CreateGetActiveWirelessConnectionStateRequest(0x33);
        var active = RazerPairingProtocol.ParseActiveWirelessConnectionState(
            Response(0x33, 0x02, 0x00, 0xC7, 0x03, 0x01),
            activeRequest);

        Assert.Equal(0x02, pairingMode);
        Assert.Equal(2, statuses.Count);
        Assert.Equal(new RazerWirelessDeviceConnectionStatus(0x01, 0x1234), statuses[0]);
        Assert.Equal(new RazerWirelessDeviceConnectionStatus(0x00, 0xABCD), statuses[1]);
        Assert.Equal(new RazerWirelessConnectionState(0x03, 0x01), active);
    }

    [Fact]
    public void TruncatedWirelessStatusIsRejected()
    {
        var request = RazerPairingProtocol.CreateGetMultipleDeviceWirelessConnectionStatusV2Request(0x34);

        Assert.Throws<InvalidOperationException>(
            () => RazerPairingProtocol.ParseMultipleDeviceWirelessConnectionStatusV2(
                Response(0x34, 0x04, 0x00, 0xBF, 0x03, 0x01, 0x12),
                request));
    }

    [Fact]
    public void ProximityFramesMatchTheMiddlewareTuplesAndPayloads()
    {
        AssertFrame(
            RazerProximitySensorProtocol.CreateSetProximitySensorConfigurationRequest(
                0x41,
                0x0B,
                0x05,
                new byte[] { 0x10, 0x11, 0x12, 0x13, 0x14, 0x15, 0x16, 0x17 }),
            0x41,
            0x0A,
            0x0B,
            0x05,
            0x0B,
            0x05,
            0x10,
            0x11,
            0x12,
            0x13,
            0x14,
            0x15,
            0x16,
            0x17);
        AssertFrame(
            RazerProximitySensorProtocol.CreateGetProximitySensorConfigurationRequest(0x42, 0x0B, 0x05),
            0x42,
            0x0A,
            0x0B,
            0x85,
            0x0B,
            0x05,
            0x00,
            0x00,
            0x00,
            0x00,
            0x00,
            0x00,
            0x00,
            0x00);
        AssertFrame(
            RazerProximitySensorProtocol.CreateSetProximitySensorStateRequest(0x43, 0x0B, 0x05, 0x02),
            0x43,
            0x03,
            0x0B,
            0x03,
            0x0B,
            0x05,
            0x02);
        AssertFrame(
            RazerProximitySensorProtocol.CreateSetProximitySensorThresholdRequest(
                0x44,
                0x0B,
                0x05,
                0x1234,
                0x0102,
                0x00FF),
            0x44,
            0x08,
            0x0B,
            0x02,
            0x0B,
            0x05,
            0x12,
            0x34,
            0x01,
            0x02,
            0x00,
            0xFF);
        AssertFrame(
            RazerProximitySensorProtocol.CreateSetProximitySensorLiftSettingRequest(
                0x45,
                0x0B,
                0x05,
                0x02,
                0x0A),
            0x45,
            0x04,
            0x0B,
            0x0B,
            0x0B,
            0x05,
            0x02,
            0x0A);
        AssertFrame(
            RazerProximitySensorProtocol.CreateSetProximitySensorControlRequest(
                0x46,
                0x0B,
                0x05,
                0x03,
                0x04),
            0x46,
            0x04,
            0x0B,
            0x09,
            0x0B,
            0x05,
            0x03,
            0x04);
        AssertFrame(
            RazerProximitySensorProtocol.CreateGetProximitySensorControlRequest(0x47, 0x0B, 0x05),
            0x47,
            0x04,
            0x0B,
            0x89,
            0x0B,
            0x05,
            0x00,
            0x00);
    }

    [Fact]
    public void ProximityParsersReadTheMiddlewareResponseLayouts()
    {
        var configurationRequest = RazerProximitySensorProtocol.CreateGetProximitySensorConfigurationRequest(
            0x51,
            0x0B,
            0x05);
        var configuration = RazerProximitySensorProtocol.ParseConfiguration(
            Response(0x51, 0x0B, 0x0B, 0x85, 0x0B, 0x05, 0x20, 0x21, 0x22, 0x23, 0x24, 0x25, 0x26, 0x27, 0x28),
            configurationRequest);

        var stateRequest = RazerProximitySensorProtocol.CreateSetProximitySensorStateRequest(0x52, 0x0B, 0x05, 0x02);
        var state = RazerProximitySensorProtocol.ParseState(
            Response(0x52, 0x03, 0x0B, 0x03, 0x0B, 0x05, 0x02),
            stateRequest);

        var thresholdRequest = RazerProximitySensorProtocol.CreateSetProximitySensorThresholdRequest(
            0x53,
            0x0B,
            0x05,
            0x1234,
            0x0102,
            0x00FF);
        var threshold = RazerProximitySensorProtocol.ParseThreshold(
            Response(0x53, 0x08, 0x0B, 0x02, 0x0B, 0x05, 0x12, 0x34, 0x01, 0x02, 0x00, 0xFF),
            thresholdRequest);

        var liftRequest = RazerProximitySensorProtocol.CreateSetProximitySensorLiftSettingRequest(
            0x54,
            0x0B,
            0x05,
            0x02,
            0x0A);
        var lift = RazerProximitySensorProtocol.ParseLiftSetting(
            Response(0x54, 0x04, 0x0B, 0x0B, 0x0B, 0x05, 0x02, 0x0A),
            liftRequest);

        var controlRequest = RazerProximitySensorProtocol.CreateGetProximitySensorControlRequest(0x55, 0x0B, 0x05);
        var control = RazerProximitySensorProtocol.ParseControl(
            Response(0x55, 0x04, 0x0B, 0x89, 0x0B, 0x05, 0x03, 0x04),
            controlRequest);

        Assert.Equal(0x0B, configuration.ClassId);
        Assert.Equal(0x05, configuration.SensorId);
        Assert.Equal(
            new byte[] { 0x20, 0x21, 0x22, 0x23, 0x24, 0x25, 0x26, 0x27, 0x28 },
            configuration.Parameters);
        Assert.Equal(new RazerProximitySensorState(0x0B, 0x05, 0x02), state);
        Assert.Equal(new RazerProximitySensorThreshold(0x0B, 0x05, 0x1234, 0x0102, 0x00FF), threshold);
        Assert.Equal(new RazerProximitySensorLiftSetting(0x0B, 0x05, 0x02, 0x0A), lift);
        Assert.Equal(new RazerProximitySensorControl(0x0B, 0x05, 0x03, 0x04), control);
    }

    [Fact]
    public void ProximityConfigurationAndTruncatedResponsesAreRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => RazerProximitySensorProtocol.CreateSetProximitySensorConfigurationRequest(
                0x61,
                0x0B,
                0x05,
                new byte[9]));

        var request = RazerProximitySensorProtocol.CreateSetProximitySensorThresholdRequest(
            0x62,
            0x0B,
            0x05,
            0x1234,
            0x0102,
            0x00FF);
        Assert.Throws<InvalidOperationException>(
            () => RazerProximitySensorProtocol.ParseThreshold(
                Response(0x62, 0x07, 0x0B, 0x02, 0x0B, 0x05, 0x12, 0x34, 0x01, 0x02, 0x00),
                request));
    }

    private static void AssertFrame(
        byte[] request,
        byte transactionId,
        byte dataSize,
        byte commandClass,
        byte commandId,
        params byte[] arguments)
    {
        var expected = new byte[RazerFeatureReport.Length];
        expected[2] = transactionId;
        expected[6] = dataSize;
        expected[7] = commandClass;
        expected[8] = commandId;
        arguments.CopyTo(expected, RazerFeatureReport.ArgumentsOffset);
        expected[89] = Crc(expected);

        Assert.Equal(expected, request);
    }

    private static byte[] Response(
        byte transactionId,
        byte dataSize,
        byte commandClass,
        byte commandId,
        params byte[] arguments)
    {
        var response = new byte[RazerFeatureReport.Length];
        response[1] = 0x02;
        response[2] = transactionId;
        response[6] = dataSize;
        response[7] = commandClass;
        response[8] = commandId;
        arguments.CopyTo(response, RazerFeatureReport.ArgumentsOffset);
        response[89] = Crc(response);
        return response;
    }

    private static byte Crc(byte[] report)
    {
        byte crc = 0;
        for (var index = 3; index <= 88; index++)
        {
            crc ^= report[index];
        }

        return crc;
    }
}
