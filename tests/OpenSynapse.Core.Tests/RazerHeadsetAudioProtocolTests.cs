using OpenSynapse.Windows.Protocols;
using Xunit;

namespace OpenSynapse.Core.Tests;

public sealed class RazerHeadsetAudioProtocolTests
{
    [Fact]
    public void GetAnc_DeclaresTheMiddlewareSizedRequestAndReadsThreeBytes()
    {
        var request = RazerHeadsetAudioProtocol.GetAnc(0x05);

        Assert.Equal(RazerFeatureReport.Length, request.Length);
        Assert.Equal(0x00, request[0]);
        Assert.Equal(0x05, request[2]);
        Assert.Equal(0x01, request[6]);
        Assert.Equal(0x18, request[7]);
        Assert.Equal(0x86, request[8]);
        Assert.Equal(RazerHeadsetAudioProtocol.ReadProfile, request[9]);
        Assert.Equal(RazerFeatureReport.CalculateCrc(request), request[89]);

        var response = Response(0x05, 0x18, 0x86, 3, 0x00, 0x01, 0x04);
        var anc = RazerHeadsetAudioProtocol.ReadAnc(response);
        Assert.Equal(RazerAncState.On, anc.State);
        Assert.Equal(4, anc.Level);
    }

    [Fact]
    public void SetAnc_EncodesStateAndLevel()
    {
        var request = RazerHeadsetAudioProtocol.SetAnc(0x06, RazerAncState.Ambient, 0x02);

        Assert.Equal(0x03, request[6]);
        Assert.Equal(0x18, request[7]);
        Assert.Equal(0x06, request[8]);
        Assert.Equal(RazerHeadsetAudioProtocol.WriteProfile, request[9]);
        Assert.Equal((byte)RazerAncState.Ambient, request[10]);
        Assert.Equal(0x02, request[11]);
        Assert.Equal(RazerFeatureReport.CalculateCrc(request), request[89]);
    }

    [Fact]
    public void EqBandRequests_UseNineBytesForEightBandsAndElevenForTen()
    {
        var eight = RazerHeadsetAudioProtocol.GetEqBands(0x00, tenBand: false);
        var ten = RazerHeadsetAudioProtocol.GetEqBands(0x00, tenBand: true);

        Assert.Equal(0x09, eight[6]);
        Assert.Equal(0x0B, ten[6]);
        Assert.Equal(0x84, eight[8]);
        Assert.Equal(0x84, ten[8]);

        byte[] bands = [1, 2, 3, 4, 5, 6, 7, 8];
        var set = RazerHeadsetAudioProtocol.SetEqBands(0x01, bands);
        Assert.Equal(0x09, set[6]);
        Assert.Equal(0x04, set[8]);
        Assert.Equal(RazerHeadsetAudioProtocol.WriteProfile, set[9]);
        Assert.Equal(bands, set[10..18]);
        Assert.Equal(RazerFeatureReport.CalculateCrc(set), set[89]);
    }

    [Fact]
    public void SettersAndGetters_UseTheProtocol25CommandTable()
    {
        Assert.Equal((0x02, 0x08, 0x82), Command(RazerHeadsetAudioProtocol.GetEqPreset(0)));
        Assert.Equal((0x02, 0x08, 0x02), Command(RazerHeadsetAudioProtocol.SetEqPreset(0, 16)));
        Assert.Equal((0x02, 0x08, 0x87), Command(RazerHeadsetAudioProtocol.GetBass(0)));
        Assert.Equal((0x02, 0x08, 0x07), Command(RazerHeadsetAudioProtocol.SetBass(0, 10)));
        Assert.Equal((0x02, 0x08, 0x88), Command(RazerHeadsetAudioProtocol.GetMicMute(0)));
        Assert.Equal((0x02, 0x08, 0x08), Command(RazerHeadsetAudioProtocol.SetMicMute(0, true)));
        Assert.Equal((0x02, 0x08, 0x97), Command(RazerHeadsetAudioProtocol.GetMicVolume(0)));
        Assert.Equal((0x02, 0x08, 0x17), Command(RazerHeadsetAudioProtocol.SetMicVolume(0, 40)));
        Assert.Equal((0x02, 0x08, 0x8A), Command(RazerHeadsetAudioProtocol.GetAudioSource(0)));
        Assert.Equal((0x02, 0x08, 0x0A), Command(RazerHeadsetAudioProtocol.SetAudioSource(0, RazerAudioSource.Usb)));
        Assert.Equal((0x02, 0x08, 0x9C), Command(RazerHeadsetAudioProtocol.GetNoiseSuppression(0)));
        Assert.Equal((0x02, 0x08, 0x1C), Command(RazerHeadsetAudioProtocol.SetNoiseSuppression(0, true)));
        Assert.Equal((0x02, 0x00, 0x81), Command(RazerHeadsetAudioProtocol.GetFirmware(0)));
        Assert.Equal((0x02, 0x07, 0x80), Command(RazerHeadsetAudioProtocol.GetBatteryLevel(0)));
        Assert.Equal((0x02, 0x07, 0x84), Command(RazerHeadsetAudioProtocol.GetChargingStatus(0)));
    }

    [Fact]
    public void Readers_TakeTheSecondArgumentByteAsTheValue()
    {
        Assert.Equal(16, RazerHeadsetAudioProtocol.ReadEqPreset(Response(1, 4, 5, 2, 0x00, 16)));
        Assert.Equal(9, RazerHeadsetAudioProtocol.ReadBass(Response(1, 4, 5, 2, 0x00, 9)));
        Assert.True(RazerHeadsetAudioProtocol.ReadMicMute(Response(1, 4, 5, 2, 0x00, 1)));
        Assert.Equal(55, RazerHeadsetAudioProtocol.ReadMicVolume(Response(1, 4, 5, 2, 0x00, 55)));
        Assert.Equal(RazerAudioSource.Bluetooth,
            RazerHeadsetAudioProtocol.ReadAudioSource(Response(1, 4, 5, 2, 0x00, 3)));
        Assert.True(RazerHeadsetAudioProtocol.ReadNoiseSuppression(Response(1, 4, 5, 2, 0x00, 1)));
        Assert.True(RazerHeadsetAudioProtocol.ReadChargingStatus(Response(1, 4, 5, 2, 0x00, 1)));
        Assert.Equal(64, RazerHeadsetAudioProtocol.ReadBatteryLevel(Response(1, 4, 5, 2, 0x00, 64)));
        Assert.Equal(new Version(1, 2), RazerHeadsetAudioProtocol.ReadFirmware(Response(1, 4, 5, 2, 1, 2)));
        Assert.Equal([1, 2, 3, 4, 5, 6, 7, 8],
            RazerHeadsetAudioProtocol.ReadEqBands(Response(1, 4, 5, 9, 0, 1, 2, 3, 4, 5, 6, 7, 8), tenBand: false));
    }

    private static (byte Size, byte Class, byte Command) Command(byte[] request) =>
        (request[6], request[7], request[8]);

    private static byte[] Response(byte transaction, byte commandClass, byte command, byte dataSize, params byte[] arguments)
    {
        var response = new byte[RazerFeatureReport.Length];
        response[1] = 0x02;
        response[2] = transaction;
        response[6] = dataSize;
        response[7] = commandClass;
        response[8] = command;
        arguments.CopyTo(response.AsSpan(RazerFeatureReport.ArgumentsOffset));
        response[89] = RazerFeatureReport.CalculateCrc(response);
        return response;
    }
}
