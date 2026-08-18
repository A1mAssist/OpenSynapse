using OpenSynapse.Core.Devices;
using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.Core.Tests;

public sealed class BladeSynapsePolicyProtocolTests
{
    [Fact]
    public void BuildsProduct710GameModeDescriptors()
    {
        var get = BladeSynapsePolicyProtocol.CreateGetGameModeRequest();
        Assert.Equal((byte)0x00, get[2]);
        Assert.Equal((byte)0x04, get[6]);
        Assert.Equal((byte)0x00, get[7]);
        Assert.Equal((byte)0x88, get[8]);
        Assert.Equal((byte)0x08, BladeSynapsePolicyProtocol.CreateSetGameModeRequest(1)[8]);
        Assert.Equal((byte)1, BladeSynapsePolicyProtocol.CreateSetGameModeRequest(1)[9]);
    }

    [Fact]
    public void ParsesGameModeStateFromProduct710Response()
    {
        var response = Response(
            BladeSynapsePolicyProtocol.CreateGetGameModeRequest(),
            0x01, 0x02, 0x00);

        Assert.Equal(new BladeGameModeState(1, 2, 0),
            BladeSynapsePolicyProtocol.ParseGameMode(response));
    }

    [Fact]
    public void BuildsFnPrimaryAndLogoSequenceWithSynapseDescriptors()
    {
        var fn = BladeSynapsePolicyProtocol.CreateSetFnKeyStateRequest(multiFunctionPrimary: true);
        Assert.Equal(new byte[] { 0x00, 0x01 }, fn[9..11]);
        Assert.Equal(new byte[] { 0x00, 0x04, 0x02 },
            BladeSynapsePolicyProtocol.CreateSetLogoEffectRequest(BladeLogoMode.Breathing)[9..12]);
        Assert.Equal(new byte[] { 0x00, 0x04, 0x01 },
            BladeSynapsePolicyProtocol.CreateSetLogoStateRequest(BladeLogoMode.Static)[9..12]);
        Assert.Equal(new byte[] { 0x00, 0x04, 0x00 },
            BladeSynapsePolicyProtocol.CreateSetLogoStateRequest(BladeLogoMode.Off)[9..12]);
    }

    [Fact]
    public void RejectsUnknownLogoModeBeforeBuildingEitherCommand()
    {
        var unknown = (BladeLogoMode)byte.MaxValue;

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            BladeSynapsePolicyProtocol.CreateSetLogoEffectRequest(unknown));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            BladeSynapsePolicyProtocol.CreateSetLogoStateRequest(unknown));
    }

    [Fact]
    public void ParsesFnEchoAndRejectsWrongObject()
    {
        var request = BladeSynapsePolicyProtocol.CreateSetFnKeyStateRequest(multiFunctionPrimary: true);
        var response = Response(request, 0x00, 0x01);

        Assert.Equal(new BladeFnKeyState(0, true),
            BladeSynapsePolicyProtocol.ParseFnKeyState(response, request));

        response[9] = 0x01;
        response[89] = RazerFeatureReport.CalculateCrc(response);
        Assert.Throws<InvalidOperationException>(() =>
            BladeSynapsePolicyProtocol.ParseFnKeyState(response, request));
    }

    [Fact]
    public void ParsesLogoCommandResultAndRejectsWrongTransaction()
    {
        var request = BladeSynapsePolicyProtocol.CreateSetLogoEffectRequest(BladeLogoMode.Breathing);
        var response = Response(request, 0x00, 0x04, 0x02);
        Assert.Equal(new BladeLedCommandResult(0, 4, 2),
            BladeSynapsePolicyProtocol.ParseLedCommandResult(response, request));

        response[2] = 0x1F;
        response[89] = RazerFeatureReport.CalculateCrc(response);
        Assert.Throws<InvalidOperationException>(() =>
            BladeSynapsePolicyProtocol.ParseLedCommandResult(response, request));
    }

    [Fact]
    public void BuildsAndParsesGameModeIndicatorState()
    {
        var request = BladeSynapsePolicyProtocol.CreateSetGameModeIndicatorRequest(enabled: true);

        Assert.Equal((byte)0x00, request[2]);
        Assert.Equal((byte)0x03, request[6]);
        Assert.Equal((byte)0x03, request[7]);
        Assert.Equal((byte)0x00, request[8]);
        Assert.Equal(new byte[] { 0x00, 0x08, 0x01 }, request[9..12]);
        var response = Response(request, 0x00, 0x08, 0x01);
        Assert.Equal(new BladeLedCommandResult(0, 8, 1),
            BladeSynapsePolicyProtocol.ParseLedCommandResult(response, request));
    }

    [Fact]
    public void BuildsAndParsesProduct710StartupAnimationReports()
    {
        var get = BladeSynapsePolicyProtocol.CreateGetStartupAnimationRequest();
        Assert.Equal((byte)0x1F, get[2]);
        Assert.Equal((byte)0x01, get[6]);
        Assert.Equal((byte)0x0F, get[7]);
        Assert.Equal((byte)0x98, get[8]);
        Assert.Equal((byte)0x00, get[9]);

        var enabled = BladeSynapsePolicyProtocol.CreateSetStartupAnimationRequest(enabled: true);
        var disabled = BladeSynapsePolicyProtocol.CreateSetStartupAnimationRequest(enabled: false);
        Assert.Equal(new byte[] { 0x00, 0x00 }, enabled[9..11]);
        Assert.Equal(new byte[] { 0x00, 0x01 }, disabled[9..11]);

        var oneByte = Response(get, 0x00);
        Assert.Equal(new BladeStartupAnimationState(null, true),
            BladeSynapsePolicyProtocol.ParseStartupAnimation(oneByte));

        var twoByte = ResponseWithDataSize(get, 0x02, 0x00, 0x01);
        Assert.Equal(new BladeStartupAnimationState(0, false),
            BladeSynapsePolicyProtocol.ParseStartupAnimation(twoByte));
    }

    [Fact]
    public void RejectsUnknownStartupAnimationState()
    {
        var get = BladeSynapsePolicyProtocol.CreateGetStartupAnimationRequest();
        var response = Response(get, 0x02);

        Assert.Throws<InvalidOperationException>(() =>
            BladeSynapsePolicyProtocol.ParseStartupAnimation(response));
    }

    [Fact]
    public void BuildsAndParsesAudioMuteLedSynchronization()
    {
        var request = BladeSynapsePolicyProtocol.CreateSetAudioMuteStatusRequest(
            BladeAudioMuteTarget.Microphone,
            muted: true,
            transactionId: 0x1E);
        Assert.Equal((byte)0x1E, request[2]);
        Assert.Equal((byte)0x03, request[6]);
        Assert.Equal((byte)0x18, request[7]);
        Assert.Equal((byte)0x04, request[8]);
        Assert.Equal(new byte[] { 0x00, 0x02, 0x01 }, request[9..12]);

        var response = Response(request, 0x00, 0x02, 0x01);
        Assert.Equal(new BladeAudioMuteState(BladeAudioMuteTarget.Microphone, true),
            BladeSynapsePolicyProtocol.ParseAudioMuteCommandResult(response, request));

        response[10] = 0x01;
        response[89] = RazerFeatureReport.CalculateCrc(response);
        Assert.Throws<InvalidOperationException>(() =>
            BladeSynapsePolicyProtocol.ParseAudioMuteCommandResult(response, request));
    }

    private static byte[] Response(byte[] request, params byte[] arguments)
    {
        var response = (byte[])request.Clone();
        arguments.CopyTo(response, RazerFeatureReport.ArgumentsOffset);
        response[1] = 0x02;
        response[89] = RazerFeatureReport.CalculateCrc(response);
        return response;
    }

    private static byte[] ResponseWithDataSize(
        byte[] request,
        byte dataSize,
        params byte[] arguments)
    {
        var response = Response(request, arguments);
        response[6] = dataSize;
        response[89] = RazerFeatureReport.CalculateCrc(response);
        return response;
    }
}
