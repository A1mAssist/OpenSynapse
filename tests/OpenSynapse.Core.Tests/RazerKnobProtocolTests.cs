using OpenSynapse.Windows.Protocols;
using Xunit;

namespace OpenSynapse.Core.Tests;

public sealed class RazerKnobProtocolTests
{
    [Fact]
    public void KnobBuildersUseTheMiddlewareTuplesAndPayloadLayout()
    {
        var getConfiguration = RazerKnobProtocol.CreateGetConfigurationRequest(0x05, 0x03, 0x24);
        var stages = new RazerKnobStage[RazerKnobProtocol.StageCount];
        for (var index = 0; index < stages.Length; index++)
        {
            stages[index] = new RazerKnobStage(
                (byte)index,
                (byte)(index + 1),
                (RazerKnobMode)index,
                (byte)(0x10 + index),
                (byte)(0x20 + index),
                (byte)(0x30 + index));
        }

        var setConfiguration = RazerKnobProtocol.CreateSetConfigurationRequest(0x06, 0x03, 0x24, stages);
        var getMode = RazerKnobProtocol.CreateGetModeRequest(0x07, 0x03, 0x24);
        var setMode = RazerKnobProtocol.CreateSetModeRequest(
            0x08,
            0x03,
            0x24,
            new RazerKnobStage(1, 2, RazerKnobMode.SwitchApplication, 0x33, 0x44, 0x55));

        Assert.Equal((0x32, 0x03, 0x92), Command(getConfiguration));
        Assert.Equal((0x32, 0x03, 0x12), Command(setConfiguration));
        Assert.Equal((0x08, 0x03, 0x93), Command(getMode));
        Assert.Equal((0x08, 0x03, 0x13), Command(setMode));
        Assert.Equal(0x05, getConfiguration[2]);
        Assert.Equal(0x06, setConfiguration[2]);
        Assert.Equal(0x07, getMode[2]);
        Assert.Equal(0x08, setMode[2]);

        var getConfigurationPayload = getConfiguration[9..59];
        Assert.Equal(0x03, getConfigurationPayload[0]);
        Assert.Equal(0x24, getConfigurationPayload[1]);
        Assert.Equal(new byte[48], getConfigurationPayload[2..].ToArray());

        var setConfigurationPayload = setConfiguration[9..59];
        Assert.Equal(
            new byte[]
            {
                0x03, 0x24,
                0x00, 0x01, 0x00, 0x10, 0x20, 0x30,
                0x01, 0x02, 0x01, 0x11, 0x21, 0x31,
                0x02, 0x03, 0x02, 0x12, 0x22, 0x32,
                0x03, 0x04, 0x03, 0x13, 0x23, 0x33,
                0x04, 0x05, 0x04, 0x14, 0x24, 0x34,
                0x05, 0x06, 0x05, 0x15, 0x25, 0x35,
                0x06, 0x07, 0x06, 0x16, 0x26, 0x36,
                0x07, 0x08, 0x07, 0x17, 0x27, 0x37,
            },
            setConfigurationPayload.ToArray());

        Assert.Equal(new byte[] { 0x03, 0x24, 0, 0, 0, 0, 0, 0 }, getMode[9..17].ToArray());
        Assert.Equal(new byte[] { 0x03, 0x24, 1, 2, 7, 0x33, 0x44, 0x55 }, setMode[9..17].ToArray());

        foreach (var report in new[] { getConfiguration, setConfiguration, getMode, setMode })
        {
            Assert.Equal(RazerFeatureReport.Length, report.Length);
            Assert.Equal(0x00, report[0]);
            Assert.Equal(0x00, report[90]);
            Assert.Equal(RazerFeatureReport.CalculateCrc(report), report[89]);
        }
    }

    [Fact]
    public void ConfigurationParsesAllEightStages()
    {
        var request = RazerKnobProtocol.CreateGetConfigurationRequest(0x05, 0x03, 0x24);
        var arguments = new byte[50];
        arguments[0] = 0x03;
        arguments[1] = 0x24;
        for (var index = 0; index < RazerKnobProtocol.StageCount; index++)
        {
            var offset = 2 + (index * RazerKnobProtocol.StageSize);
            arguments[offset] = (byte)(index % 2);
            arguments[offset + 1] = (byte)index;
            arguments[offset + 2] = (byte)(index % 8);
            arguments[offset + 3] = (byte)(0x10 + index);
            arguments[offset + 4] = (byte)(0x20 + index);
            arguments[offset + 5] = (byte)(0x30 + index);
        }

        var configuration = RazerKnobProtocol.ParseConfiguration(
            Response(0x05, 0x32, 0x92, arguments),
            request);

        Assert.Equal(0x03, configuration.LedClass);
        Assert.Equal(0x24, configuration.LedId);
        Assert.Equal(RazerKnobProtocol.StageCount, configuration.Stages.Count);
        for (var index = 0; index < configuration.Stages.Count; index++)
        {
            var stage = configuration.Stages[index];
            Assert.Equal((byte)(index % 2), stage.Disabled);
            Assert.Equal((byte)index, stage.Stage);
            Assert.Equal((RazerKnobMode)(index % 8), stage.Mode);
            Assert.Equal((byte)(0x10 + index), stage.Red);
            Assert.Equal((byte)(0x20 + index), stage.Green);
            Assert.Equal((byte)(0x30 + index), stage.Blue);
        }
    }

    [Fact]
    public void CurrentModeParsesTheEightByteEcho()
    {
        var request = RazerKnobProtocol.CreateGetModeRequest(0x05, 0x03, 0x24);
        var mode = RazerKnobProtocol.ParseMode(
            Response(0x05, 0x08, 0x93, [0x03, 0x24, 0x01, 0x05, 0x07, 0x11, 0x22, 0x33]),
            request);

        Assert.Equal(0x03, mode.LedClass);
        Assert.Equal(0x24, mode.LedId);
        Assert.Equal(0x01, mode.Stage.Disabled);
        Assert.Equal(0x05, mode.Stage.Stage);
        Assert.Equal(RazerKnobMode.SwitchApplication, mode.Stage.Mode);
        Assert.Equal(0x11, mode.Stage.Red);
        Assert.Equal(0x22, mode.Stage.Green);
        Assert.Equal(0x33, mode.Stage.Blue);
    }

    [Fact]
    public void TruncatedResponsesAreRejected()
    {
        var configurationRequest = RazerKnobProtocol.CreateGetConfigurationRequest(0x05, 0x03, 0x24);
        var modeRequest = RazerKnobProtocol.CreateGetModeRequest(0x05, 0x03, 0x24);

        Assert.Throws<InvalidOperationException>(
            () => RazerKnobProtocol.ParseConfiguration(new byte[20], configurationRequest));
        Assert.Throws<InvalidOperationException>(
            () => RazerKnobProtocol.ParseConfiguration(
                Response(0x05, 0x31, 0x92, new byte[49]),
                configurationRequest));
        Assert.Throws<InvalidOperationException>(
            () => RazerKnobProtocol.ParseMode(new byte[20], modeRequest));
    }

    private static (byte Size, byte Class, byte Command) Command(byte[] request) =>
        (request[6], request[7], request[8]);

    private static byte[] Response(
        byte transaction,
        byte dataSize,
        byte commandId,
        byte[] arguments)
    {
        var response = new byte[RazerFeatureReport.Length];
        response[1] = 0x02;
        response[2] = transaction;
        response[6] = dataSize;
        response[7] = 0x03;
        response[8] = commandId;
        arguments.CopyTo(response.AsSpan(RazerFeatureReport.ArgumentsOffset));
        response[89] = RazerFeatureReport.CalculateCrc(response);
        return response;
    }
}
