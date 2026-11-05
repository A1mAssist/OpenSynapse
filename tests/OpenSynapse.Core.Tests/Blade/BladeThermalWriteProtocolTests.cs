using OpenSynapse.Windows.Protocols;
using Xunit;

namespace OpenSynapse.Core.Tests;

public sealed class BladeThermalWriteProtocolTests
{
    private const string FanTableFrame =
        "00001F000000500D040101003208FC410AF0FF00000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000DB00";

    private const string DutyCycleFrame =
        "00001F000000030D0D01015000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000005300";

    private const string FanModeFrame =
        "00001F000000040D0201010400000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000F00";

    private const string AdvanceFanModeFrame =
        "00001F000000030D0701010400000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000D00";

    [Fact]
    public void FanTableRequestIsByteExact()
    {
        var request = BladeThermalWriteProtocol.CreateSetFanTableRequest(
            BladeThermalProtocol.ProfileId,
            BladeThermalProtocol.CpuFanId,
            0x00,
            new[]
            {
                new BladeThermalFanTableStage(50, 2300),
                new BladeThermalFanTableStage(65, 2800),
            });

        Assert.Equal(FanTableFrame, Convert.ToHexString(request));
    }

    [Fact]
    public void FanTableUsesTheFullEightyBytePayloadForTwentyFiveStages()
    {
        var stages = Enumerable.Range(0, BladeThermalWriteProtocol.MaximumFanTableStages)
            .Select(index => new BladeThermalFanTableStage(
                (byte)(40 + index),
                (ushort)(2000 + (index * 100))))
            .ToArray();

        var request = BladeThermalWriteProtocol.CreateSetFanTableRequest(
            BladeThermalProtocol.ProfileId,
            BladeThermalProtocol.CpuFanId,
            0x00,
            stages);

        Assert.Equal(0x50, request[RazerFeatureReport.DataSizeOffset]);
        Assert.Equal(0xFF, request[RazerFeatureReport.ArgumentsOffset + 3 + (3 * stages.Length)]);
    }

    [Fact]
    public void FanTableRejectsLessThanOneOrMoreThanTwentyFiveStages()
    {
        Assert.Throws<ArgumentException>(() =>
            BladeThermalWriteProtocol.CreateSetFanTableRequest(
                BladeThermalProtocol.ProfileId,
                BladeThermalProtocol.CpuFanId,
                0x00,
                Array.Empty<BladeThermalFanTableStage>()));

        var tooMany = Enumerable.Range(0, BladeThermalWriteProtocol.MaximumFanTableStages + 1)
            .Select(index => new BladeThermalFanTableStage((byte)(40 + index), 2000))
            .ToArray();

        Assert.Throws<ArgumentException>(() =>
            BladeThermalWriteProtocol.CreateSetFanTableRequest(
                BladeThermalProtocol.ProfileId,
                BladeThermalProtocol.CpuFanId,
                0x00,
                tooMany));
    }

    [Fact]
    public void DutyCycleRequestIsByteExact()
    {
        var request = BladeThermalWriteProtocol.CreateSetFanDutyCycleRequest(
            BladeThermalProtocol.ProfileId,
            BladeThermalProtocol.CpuFanId,
            80);

        Assert.Equal(DutyCycleFrame, Convert.ToHexString(request));
    }

    [Fact]
    public void FanModeRequestIsByteExact()
    {
        var request = BladeThermalWriteProtocol.CreateSetFanModeRequest(
            BladeThermalProtocol.ProfileId,
            BladeThermalProtocol.CpuFanId,
            0x04);

        Assert.Equal(FanModeFrame, Convert.ToHexString(request));
    }

    [Fact]
    public void AdvanceFanModeRequestIsByteExact()
    {
        var request = BladeThermalWriteProtocol.CreateSetAdvanceFanModeRequest(
            BladeThermalProtocol.ProfileId,
            BladeThermalProtocol.CpuFanId,
            0x04);

        Assert.Equal(AdvanceFanModeFrame, Convert.ToHexString(request));
    }

    [Fact]
    public void DutyCycleRejectsAValueAboveOneHundred()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            BladeThermalWriteProtocol.CreateSetFanDutyCycleRequest(
                BladeThermalProtocol.ProfileId,
                BladeThermalProtocol.CpuFanId,
                101));
    }

    [Fact]
    public void FanModeRejectsAValueOutsideTheJsEnum()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            BladeThermalWriteProtocol.CreateSetFanModeRequest(
                BladeThermalProtocol.ProfileId,
                BladeThermalProtocol.CpuFanId,
                0x08));
    }

    [Fact]
    public void ReadBackComparatorAcceptsMatchingFanSpeedFrame()
    {
        var expected = new byte[] { 0x01, 0x01, 0x2A };
        var response = Response(
            RazerFeatureReport.CreateRequest(
                BladeThermalProtocol.TransactionId,
                0x03,
                0x0D,
                BladeThermalWriteProtocol.GetFanSpeedCommandId,
                new byte[] { 0x01, 0x01, 0x00 }),
            expected);

        Assert.True(BladeThermalWriteProtocol.MatchesReadBack(
            response,
            BladeThermalWriteProtocol.GetFanSpeedCommandId,
            expected));
    }

    [Fact]
    public void ReadBackComparatorAcceptsMatchingFanInformationFrame()
    {
        var expected = new byte[]
        {
            0x00, 0x12, 0x34, 0x56, 0x78, 0x01,
            0x02, 0x03, 0x04, 0x05, 0x06, 0x07,
        };
        var response = Response(BladeThermalProtocol.CreateGetFanInformationRequest(), expected);

        Assert.True(BladeThermalWriteProtocol.MatchesReadBack(
            response,
            BladeThermalWriteProtocol.GetFanInformationCommandId,
            expected));
    }

    [Fact]
    public void ReadBackComparatorAcceptsMatchingDutyCycleFrame()
    {
        var expected = new byte[] { 0x01, 0x01, 0x50 };
        var response = Response(
            BladeThermalProtocol.CreateGetFanDutyCycleRequest(
                BladeThermalProtocol.ProfileId,
                BladeThermalProtocol.CpuFanId),
            expected);

        Assert.True(BladeThermalWriteProtocol.MatchesReadBack(
            response,
            BladeThermalWriteProtocol.GetFanDutyCycleCommandId,
            expected));
    }

    [Fact]
    public void ReadBackComparatorRejectsAChangedValue()
    {
        var expected = new byte[] { 0x01, 0x01, 0x50 };
        var response = Response(
            BladeThermalProtocol.CreateGetFanDutyCycleRequest(
                BladeThermalProtocol.ProfileId,
                BladeThermalProtocol.CpuFanId),
            expected);
        expected[2] = 0x4F;

        Assert.False(BladeThermalWriteProtocol.MatchesReadBack(
            response,
            BladeThermalWriteProtocol.GetFanDutyCycleCommandId,
            expected));
    }

    [Fact]
    public void ReadBackComparatorRejectsWrongCommandOrCorruptFrame()
    {
        var expected = new byte[] { 0x01, 0x01, 0x2A };
        var response = Response(
            RazerFeatureReport.CreateRequest(
                BladeThermalProtocol.TransactionId,
                0x03,
                0x0D,
                BladeThermalWriteProtocol.GetFanSpeedCommandId,
                new byte[] { 0x01, 0x01, 0x00 }),
            expected);

        var wrongCommand = response.ToArray();
        wrongCommand[RazerFeatureReport.CommandIdOffset] = BladeThermalWriteProtocol.GetFanDutyCycleCommandId;
        wrongCommand[89] = RazerFeatureReport.CalculateCrc(wrongCommand);
        Assert.False(BladeThermalWriteProtocol.MatchesReadBack(
            wrongCommand,
            BladeThermalWriteProtocol.GetFanSpeedCommandId,
            expected));

        var badCrc = response.ToArray();
        badCrc[89] ^= 0xFF;
        Assert.False(BladeThermalWriteProtocol.MatchesReadBack(
            badCrc,
            BladeThermalWriteProtocol.GetFanSpeedCommandId,
            expected));
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
