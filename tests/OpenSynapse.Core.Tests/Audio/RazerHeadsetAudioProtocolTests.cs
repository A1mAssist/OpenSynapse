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
    public void AudioDnd_UsesTheHandlerCommandAndRoundTripsState()
    {
        var request = RazerHeadsetAudioProtocol.SetAudioDnd(0x30, 1, profile: 1);

        byte[] expected =
        [
            0x00, 0x00, 0x30, 0x00, 0x00, 0x00, 0x02, 0x18, 0x05, 0x01, 0x01,
            .. new byte[78],
            0x1F, 0x00,
        ];
        Assert.Equal(expected, request);

        var read = RazerHeadsetAudioProtocol.GetAudioDnd(0x40, profile: 1);
        Assert.Equal((0x01, 0x18, 0x85), Command(read));
        Assert.Equal(1, read[9]);
        Assert.Equal(RazerFeatureReport.CalculateCrc(read), read[89]);
        Assert.True(RazerHeadsetAudioProtocol.ReadAudioDnd(Response(0x40, 0x18, 0x85, 2, 1, 1)));
        Assert.False(RazerHeadsetAudioProtocol.ReadAudioDnd(Response(0x40, 0x18, 0x85, 2, 1, 0)));
    }

    [Fact]
    public void AudioVoicePrompts_WritesPromptIndexAndState()
    {
        var request = RazerHeadsetAudioProtocol.SetAudioVoicePrompts(
            0x31,
            state: 1,
            promptIndex: 3,
            profile: 1);

        byte[] expected =
        [
            0x00, 0x00, 0x31, 0x00, 0x00, 0x00, 0x03, 0x18, 0x10, 0x01, 0x03, 0x01,
            .. new byte[77],
            0x08, 0x00,
        ];
        Assert.Equal(expected, request);

        var read = RazerHeadsetAudioProtocol.GetAudioVoicePrompts(0x41, promptIndex: 3, profile: 1);
        Assert.Equal((0x02, 0x18, 0x90), Command(read));
        Assert.Equal(1, read[9]);
        Assert.Equal(3, read[10]);
        Assert.Equal(RazerFeatureReport.CalculateCrc(read), read[89]);
        var all = RazerHeadsetAudioProtocol.GetAudioVoicePrompts(0x41, profile: 1);
        Assert.Equal(RazerHeadsetAudioProtocol.AudioPromptAll, all[10]);
        Assert.Equal(
            ((byte)3, true),
            RazerHeadsetAudioProtocol.ReadAudioVoicePrompts(Response(0x41, 0x18, 0x90, 3, 1, 3, 1)));
    }

    [Fact]
    public void AudioAutoPause_UsesTheHandlerCommandAndRoundTripsState()
    {
        var request = RazerHeadsetAudioProtocol.SetAudioAutoPause(0x32, 1, profile: 1);

        byte[] expected =
        [
            0x00, 0x00, 0x32, 0x00, 0x00, 0x00, 0x02, 0x18, 0x1E, 0x01, 0x01,
            .. new byte[78],
            0x04, 0x00,
        ];
        Assert.Equal(expected, request);

        var read = RazerHeadsetAudioProtocol.GetAudioAutoPause(0x42, profile: 1);
        Assert.Equal((0x01, 0x18, 0x9E), Command(read));
        Assert.Equal(1, read[9]);
        Assert.Equal(RazerFeatureReport.CalculateCrc(read), read[89]);
        Assert.True(RazerHeadsetAudioProtocol.ReadAudioAutoPause(Response(0x42, 0x18, 0x9E, 2, 1, 1)));
        Assert.False(RazerHeadsetAudioProtocol.ReadAudioAutoPause(Response(0x42, 0x18, 0x9E, 2, 1, 0)));
    }

    [Fact]
    public void AudioGestureControl_UsesTheHandlerCommandAndRoundTripsState()
    {
        var request = RazerHeadsetAudioProtocol.SetAudioGestureControl(0x33, 1, profile: 1);

        byte[] expected =
        [
            0x00, 0x00, 0x33, 0x00, 0x00, 0x00, 0x02, 0x18, 0x22, 0x01, 0x01,
            .. new byte[78],
            0x38, 0x00,
        ];
        Assert.Equal(expected, request);

        var read = RazerHeadsetAudioProtocol.GetAudioGestureControl(0x43, profile: 1);
        Assert.Equal((0x02, 0x18, 0xA2), Command(read));
        Assert.Equal(1, read[9]);
        Assert.Equal(0, read[10]);
        Assert.Equal(RazerFeatureReport.CalculateCrc(read), read[89]);
        Assert.True(RazerHeadsetAudioProtocol.ReadAudioGestureControl(Response(0x43, 0x18, 0xA2, 2, 1, 1)));
        Assert.False(RazerHeadsetAudioProtocol.ReadAudioGestureControl(Response(0x43, 0x18, 0xA2, 2, 1, 0)));
    }

    [Fact]
    public void DolbyState_WritesTheElevenBytePayload()
    {
        var request = RazerHeadsetAudioProtocol.SetDolbyState(0x34, 1, profile: 1);

        byte[] expected =
        [
            0x00, 0x00, 0x34, 0x00, 0x00, 0x00, 0x0B, 0x08, 0x0C, 0x01, 0x01,
            .. new byte[78],
            0x0F, 0x00,
        ];
        Assert.Equal(expected, request);

        var read = RazerHeadsetAudioProtocol.GetDolbyState(0x44, profile: 1);
        Assert.Equal((0x0B, 0x08, 0x8C), Command(read));
        Assert.Equal(1, read[9]);
        Assert.Equal(RazerFeatureReport.CalculateCrc(read), read[89]);
        Assert.True(RazerHeadsetAudioProtocol.ReadDolbyState(Response(0x44, 0x08, 0x8C, 11, 1, 1)));
        Assert.False(RazerHeadsetAudioProtocol.ReadDolbyState(Response(0x44, 0x08, 0x8C, 11, 1, 0)));
    }

    [Fact]
    public void EqNightMode_UsesTheClassEightCommandAndRoundTripsState()
    {
        var request = RazerHeadsetAudioProtocol.SetEqNightMode(0x35, 1, profile: 1);

        byte[] expected =
        [
            0x00, 0x00, 0x35, 0x00, 0x00, 0x00, 0x02, 0x08, 0x12, 0x01, 0x01,
            .. new byte[78],
            0x18, 0x00,
        ];
        Assert.Equal(expected, request);

        var read = RazerHeadsetAudioProtocol.GetEqNightMode(0x45, profile: 1);
        Assert.Equal((0x02, 0x08, 0x92), Command(read));
        Assert.Equal(1, read[9]);
        Assert.Equal(RazerFeatureReport.CalculateCrc(read), read[89]);
        Assert.True(RazerHeadsetAudioProtocol.ReadEqNightMode(Response(0x45, 0x08, 0x92, 2, 1, 1)));
        Assert.False(RazerHeadsetAudioProtocol.ReadEqNightMode(Response(0x45, 0x08, 0x92, 2, 1, 0)));
    }

    [Fact]
    public void HeadsetControlSetters_RejectValuesAboveOne()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => RazerHeadsetAudioProtocol.SetAudioDnd(0, 2));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => RazerHeadsetAudioProtocol.SetAudioVoicePrompts(0, 2, promptIndex: 3));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => RazerHeadsetAudioProtocol.SetAudioAutoPause(0, 2));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => RazerHeadsetAudioProtocol.SetAudioGestureControl(0, 2));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => RazerHeadsetAudioProtocol.SetDolbyState(0, 2));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => RazerHeadsetAudioProtocol.SetEqNightMode(0, 2));
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
    public void GetParametricEqSetting_SelectsProfileDirectionAndBand()
    {
        var request = RazerHeadsetAudioProtocol.GetParametricEqSetting(0x41, band: 4, direction: 0, profile: 1);

        Assert.Equal((0x09, 0x08, 0x9A), Command(request));
        Assert.Equal(1, request[9]);
        Assert.Equal(0, request[10]);
        Assert.Equal(4, request[11]);
        Assert.Equal(RazerFeatureReport.CalculateCrc(request), request[89]);
    }

    [Fact]
    public void SetParametricEqSetting_WritesTheMiddlewareBandPayload()
    {
        var band = new RazerParametricEqBand(
            Profile: 0,
            Direction: 0,
            Index: 4,
            BandStatus: 1,
            Frequency: 1_000,
            GainTenths: -25,
            QFactorTenths: 14,
            FilterType: 1);

        var request = RazerHeadsetAudioProtocol.SetParametricEqSetting(0x42, band);

        byte[] expected =
        [
            0x00, 0x00, 0x42, 0x00, 0x00, 0x00, 0x09, 0x08, 0x1A,
            0x00, 0x00, 0x04, 0x01, 0x03, 0xE8, 0xE7, 0x0E, 0x01,
            .. new byte[71],
            0x1D, 0x00,
        ];
        Assert.Equal(expected, request);
    }

    [Fact]
    public void ParametricEqSettings_RoundTripBandsFromSeparateFirmwareResponses()
    {
        RazerParametricEqBand[] expected =
        [
            new(0, 0, 0, 1, 31, -120, 10, 1),
            new(0, 0, 5, 1, 1_000, -25, 14, 1),
            new(1, 0, 9, 0, 16_000, 120, 70, 4),
        ];

        foreach (var band in expected)
        {
            var response = Response(
                0x42,
                0x08,
                0x9A,
                9,
                band.Profile,
                band.Direction,
                band.Index,
                band.BandStatus,
                (byte)(band.Frequency >> 8),
                (byte)band.Frequency,
                unchecked((byte)band.GainTenths),
                band.QFactorTenths,
                band.FilterType);

            Assert.Equal(band, RazerHeadsetAudioProtocol.ReadParametricEqSetting(response));
        }
    }

    [Fact]
    public void ReadParametricEqSetting_RejectsATruncatedResponse()
    {
        var response = Response(0x42, 0x08, 0x9A, 8, 0, 0, 5, 1, 0x03, 0xE8, 0xE7, 0x0E);

        Assert.Throws<InvalidDataException>(
            () => RazerHeadsetAudioProtocol.ReadParametricEqSetting(response));
    }

    [Fact]
    public void SetMicCompressor_WritesTheNineByteMiddlewarePayload()
    {
        var setting = new RazerMicCompressor(
            Profile: 1,
            Mode: 1,
            Db: -20,
            RatioTenths: 25,
            AttackTime: 10,
            ReleaseTime: 100);

        var request = RazerHeadsetAudioProtocol.SetMicCompressor(0x10, setting);

        byte[] expected =
        [
            0x00, 0x00, 0x10, 0x00, 0x00, 0x00, 0x09, 0x08, 0x1D,
            0x01, 0x01, 0xEC, 0x00, 0x19, 0x00, 0x0A, 0x00, 0x64,
            .. new byte[71],
            0x87, 0x00,
        ];
        Assert.Equal(expected, request);
    }

    [Fact]
    public void SetMicDeEsser_WritesTheZeroToHundredValue()
    {
        var request = RazerHeadsetAudioProtocol.SetMicDeEsser(0x11, 65);

        byte[] expected =
        [
            0x00, 0x00, 0x11, 0x00, 0x00, 0x00, 0x02, 0x08, 0x1E, 0x00, 0x41,
            .. new byte[78],
            0x55, 0x00,
        ];
        Assert.Equal(expected, request);
    }

    [Fact]
    public void SetMicReverb_WritesRoomModeAndIntensity()
    {
        var request = RazerHeadsetAudioProtocol.SetMicReverb(
            0x12,
            RazerMicReverbRoomMode.Large,
            intensity: 35,
            profile: 1);

        byte[] expected =
        [
            0x00, 0x00, 0x12, 0x00, 0x00, 0x00, 0x03, 0x08, 0x1F, 0x01, 0x02, 0x23,
            .. new byte[77],
            0x34, 0x00,
        ];
        Assert.Equal(expected, request);
    }

    [Fact]
    public void SetMicLimiter_WritesTheSignedThreshold()
    {
        var request = RazerHeadsetAudioProtocol.SetMicLimiter(0x13, -12);

        byte[] expected =
        [
            0x00, 0x00, 0x13, 0x00, 0x00, 0x00, 0x02, 0x08, 0x20, 0x00, 0xF4,
            .. new byte[78],
            0xDE, 0x00,
        ];
        Assert.Equal(expected, request);
    }

    [Fact]
    public void GetMicCompressor_ReadsTheNineByteSetting()
    {
        var request = RazerHeadsetAudioProtocol.GetMicCompressor(0x20, profile: 1);

        Assert.Equal((0x09, 0x08, 0x9D), Command(request));
        Assert.Equal(1, request[9]);
        Assert.Equal(new byte[8], request[10..18]);
        Assert.Equal(RazerFeatureReport.CalculateCrc(request), request[89]);

        var response = Response(0x20, 0x08, 0x9D, 9, 1, 1, 0xEC, 0x00, 0x19, 0x00, 0x0A, 0x00, 0x64);
        var setting = RazerHeadsetAudioProtocol.ReadMicCompressor(response);

        Assert.Equal(new RazerMicCompressor(1, 1, -20, 25, 10, 100), setting);
        Assert.True(setting.Enabled);
    }

    [Fact]
    public void GetMicDeEsser_ReadsTheZeroToHundredValue()
    {
        var request = RazerHeadsetAudioProtocol.GetMicDeEsser(0x21, profile: 1);

        Assert.Equal((0x02, 0x08, 0x9E), Command(request));
        Assert.Equal(1, request[9]);
        Assert.Equal(RazerFeatureReport.CalculateCrc(request), request[89]);
        Assert.Equal(65, RazerHeadsetAudioProtocol.ReadMicDeEsser(
            Response(0x21, 0x08, 0x9E, 2, 1, 65)));
    }

    [Fact]
    public void GetMicReverb_ReadsRoomModeAndIntensity()
    {
        var request = RazerHeadsetAudioProtocol.GetMicReverb(0x22, profile: 1);

        Assert.Equal((0x03, 0x08, 0x9F), Command(request));
        Assert.Equal(1, request[9]);
        Assert.Equal(RazerFeatureReport.CalculateCrc(request), request[89]);
        Assert.Equal(
            (RazerMicReverbRoomMode.Large, (byte)35),
            RazerHeadsetAudioProtocol.ReadMicReverb(Response(0x22, 0x08, 0x9F, 3, 1, 2, 35)));
    }

    [Fact]
    public void GetMicLimiter_ReadsTheSignedThreshold()
    {
        var request = RazerHeadsetAudioProtocol.GetMicLimiter(0x23);

        Assert.Equal((0x02, 0x08, 0xA0), Command(request));
        Assert.Equal(RazerFeatureReport.CalculateCrc(request), request[89]);
        Assert.Equal(-12, RazerHeadsetAudioProtocol.ReadMicLimiter(
            Response(0x23, 0x08, 0xA0, 2, 0, 0xF4)));
    }

    [Fact]
    public void MicProcessingSetters_RejectValuesOutsideTheMiddlewareRanges()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => RazerHeadsetAudioProtocol.SetMicCompressor(
                0, new RazerMicCompressor(0, 1, 1, 10, 0, 0)));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => RazerHeadsetAudioProtocol.SetMicDeEsser(0, 101));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => RazerHeadsetAudioProtocol.SetMicReverb(0, (RazerMicReverbRoomMode)4, 50));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => RazerHeadsetAudioProtocol.SetMicReverb(0, RazerMicReverbRoomMode.Small, 101));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => RazerHeadsetAudioProtocol.SetMicLimiter(0, -61));
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
        Assert.False(RazerHeadsetAudioProtocol.ReadChargingStatus(Response(1, 4, 5, 2, 0x01, 0x00)));
        Assert.True(RazerHeadsetAudioProtocol.ReadChargingStatus(Response(1, 4, 5, 2, 0x00, 0x01)));
        Assert.Equal(25, RazerHeadsetAudioProtocol.ReadBatteryPercent(Response(1, 4, 5, 2, 0x05, 0x40)));
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
