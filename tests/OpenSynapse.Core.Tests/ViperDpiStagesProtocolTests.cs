using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.Core.Tests;

public sealed class ViperDpiStagesProtocolTests
{
    [Fact]
    public void ParsesPersistentDpiStagesAndFormatsProbeValue()
    {
        var response = CreateResponse(
            activeStage: 2,
            (1, 400, 400),
            (2, 800, 850),
            (3, 1600, 1600),
            (4, 3200, 3200),
            (5, 30000, 30000));

        var state = ViperDpiStagesProtocol.Parse(response);

        Assert.Equal((byte)2, state.ActiveStage);
        Assert.Equal(
            new[]
            {
                new ViperDpiStage(1, 400, 400),
                new ViperDpiStage(2, 800, 850),
                new ViperDpiStage(3, 1600, 1600),
                new ViperDpiStage(4, 3200, 3200),
                new ViperDpiStage(5, 30000, 30000),
            },
            state.Stages);
        Assert.Equal(
            "Active 2/5: 1=400x400, 2=800x850, 3=1600x1600, 4=3200x3200, 5=30000x30000",
            ViperDpiStagesProtocol.Format(state));
    }

    [Theory]
    [InlineData(0x00, 0x01, 0x01, "存储区")]
    [InlineData(0x01, 0x00, 0x01, "当前 DPI 档位")]
    [InlineData(0x01, 0x02, 0x01, "当前 DPI 档位")]
    [InlineData(0x01, 0x01, 0x00, "档位数量")]
    [InlineData(0x01, 0x01, 0x06, "档位数量")]
    public void RejectsInvalidHeader(byte storage, byte active, byte count, string message)
    {
        var response = CreateResponse(activeStage: 1, (1, 800, 800));
        response[RazerFeatureReport.ArgumentsOffset] = storage;
        response[RazerFeatureReport.ArgumentsOffset + 1] = active;
        response[RazerFeatureReport.ArgumentsOffset + 2] = count;
        response[89] = RazerFeatureReport.CalculateCrc(response);

        var error = Assert.Throws<InvalidOperationException>(() => ViperDpiStagesProtocol.Parse(response));

        Assert.Contains(message, error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RejectsWrongStageNumberReservedBytesAndDpiRange()
    {
        var response = CreateResponse(activeStage: 1, (1, 800, 800));
        var offset = RazerFeatureReport.ArgumentsOffset + 3;

        response[offset] = 0x02;
        response[89] = RazerFeatureReport.CalculateCrc(response);
        Assert.Throws<InvalidOperationException>(() => ViperDpiStagesProtocol.Parse(response));

        response = CreateResponse(activeStage: 1, (1, 800, 800));
        response[offset + 5] = 0x01;
        response[89] = RazerFeatureReport.CalculateCrc(response);
        Assert.Throws<InvalidOperationException>(() => ViperDpiStagesProtocol.Parse(response));

        Assert.Throws<InvalidOperationException>(() =>
            ViperDpiStagesProtocol.Parse(CreateResponse(activeStage: 1, (1, 0, 800))));
        Assert.Throws<InvalidOperationException>(() =>
            ViperDpiStagesProtocol.Parse(CreateResponse(activeStage: 1, (1, 30001, 800))));
        Assert.Throws<InvalidOperationException>(() =>
            ViperDpiStagesProtocol.Parse(CreateResponse(activeStage: 1, (1, 50, 800))));
        Assert.Throws<InvalidOperationException>(() =>
            ViperDpiStagesProtocol.Parse(CreateResponse(activeStage: 1, (1, 125, 800))));
    }

    [Fact]
    public void AcceptsOnlyCompleteContiguousZeroOrOneBasedRawStageIds()
    {
        var zeroBased = ViperDpiStagesProtocol.Parse(
            CreateResponse(activeStage: 1, (0, 800, 800), (1, 1600, 1600)));

        Assert.Equal(new byte[] { 1, 2 }, zeroBased.Stages.Select(stage => stage.Number));

        var mixed = CreateResponse(activeStage: 1, (0, 800, 800), (2, 1600, 1600));
        Assert.Throws<InvalidOperationException>(() => ViperDpiStagesProtocol.Parse(mixed));
    }

    [Theory]
    [InlineData(1, 0x04)]
    [InlineData(2, 0x20)]
    [InlineData(6, 0x25)]
    [InlineData(7, 0x05)]
    [InlineData(8, 0x85)]
    public void RejectsInvalidResponseEnvelope(int index, byte value)
    {
        var response = CreateResponse(activeStage: 1, (1, 800, 800));
        response[index] = value;
        response[89] = RazerFeatureReport.CalculateCrc(response);

        Assert.Throws<InvalidOperationException>(() => ViperDpiStagesProtocol.Parse(response));
    }

    [Fact]
    public void RejectsCorruptCrc()
    {
        var response = CreateResponse(activeStage: 1, (1, 800, 800));
        response[89] ^= 0xFF;

        Assert.Throws<InvalidOperationException>(() => ViperDpiStagesProtocol.Parse(response));
    }

    private static byte[] CreateResponse(
        byte activeStage,
        params (byte Number, int X, int Y)[] stages)
    {
        var arguments = new byte[0x26];
        arguments[0] = 0x01;
        arguments[1] = activeStage;
        arguments[2] = (byte)stages.Length;
        for (var index = 0; index < stages.Length; index++)
        {
            var stage = stages[index];
            var offset = 3 + (index * 7);
            arguments[offset] = stage.Number;
            arguments[offset + 1] = (byte)(stage.X >> 8);
            arguments[offset + 2] = (byte)stage.X;
            arguments[offset + 3] = (byte)(stage.Y >> 8);
            arguments[offset + 4] = (byte)stage.Y;
        }

        var response = RazerFeatureReport.CreateRequest(0x1F, 0x26, 0x04, 0x86, arguments);
        response[1] = 0x02;
        return response;
    }
}
