using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.Core.Tests;

public sealed class BladeLightingProtocolTests
{
    [Fact]
    public void BuildsPid710LightingEngineGateFromSynapseProductModule()
    {
        var request = BladeLightingProtocol.CreateLightingEngineGateRequest(1);

        Assert.Equal(1, request[2]);
        Assert.Equal(0x06, request[6]);
        Assert.Equal(0x0F, request[7]);
        Assert.Equal(0x02, request[8]);
        Assert.Equal(new byte[] { 0x00, 0x00, 0x08, 0x00, 0x00, 0x00 }, request[9..15]);
        Assert.All(request[15..89], value => Assert.Equal(0, value));
        Assert.Equal(0x03, request[89]);
        Assert.Equal(0, request[90]);
    }

    [Fact]
    public void StarlightQuirkDoesNotRelaxOrdinaryReportValidation()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            RazerFeatureReport.CreateRequest(0xFF, 0x01, 0x03, 0x0A, new byte[9]));
    }

    [Fact]
    public void BuildsExactFirmwareEffectRequests()
    {
        AssertRequest(BladeLightingProtocol.CreateOffRequest(), 0x01, 0x0A, 0x00);
        AssertRequest(BladeLightingProtocol.CreateWaveRequest(BladeWaveDirection.Right), 0x02, 0x0A, 0x01, 0x02);
        AssertRequest(BladeLightingProtocol.CreateSpectrumRequest(), 0x01, 0x0A, 0x04);
        AssertRequest(
            BladeLightingProtocol.CreateReactiveRequest(0x04, new RazerRgb(0x11, 0x22, 0x33)),
            0x05, 0x0A, 0x02, 0x04, 0x11, 0x22, 0x33);
        AssertRequest(
            BladeLightingProtocol.CreateStaticRequest(new RazerRgb(0x44, 0x55, 0x66)),
            0x04, 0x0A, 0x06, 0x44, 0x55, 0x66);
    }

    [Fact]
    public void BuildsExactBreathingVariants()
    {
        AssertRequest(
            BladeLightingProtocol.CreateBreathingRandomRequest(),
            0x08, 0x0A, 0x03, 0x03, 0, 0, 0, 0, 0, 0);
        AssertRequest(
            BladeLightingProtocol.CreateBreathingSingleRequest(new RazerRgb(1, 2, 3)),
            0x08, 0x0A, 0x03, 0x01, 1, 2, 3, 0, 0, 0);
        AssertRequest(
            BladeLightingProtocol.CreateBreathingDualRequest(
                new RazerRgb(1, 2, 3),
                new RazerRgb(4, 5, 6)),
            0x08, 0x0A, 0x03, 0x02, 1, 2, 3, 4, 5, 6);
    }

    [Fact]
    public void BuildsExactSourceBackedStarlightVariants()
    {
        AssertRequest(
            BladeLightingProtocol.CreateStarlightRandomRequest(1),
            0x01, 0x0A, 0x19, 0x03, 0x01, 0, 0, 0, 0, 0, 0);
        AssertRequest(
            BladeLightingProtocol.CreateStarlightSingleRequest(2, new RazerRgb(1, 2, 3)),
            0x01, 0x0A, 0x19, 0x01, 0x02, 1, 2, 3, 0, 0, 0);
        AssertRequest(
            BladeLightingProtocol.CreateStarlightDualRequest(
                3,
                new RazerRgb(1, 2, 3),
                new RazerRgb(4, 5, 6)),
            0x01, 0x0A, 0x19, 0x02, 0x03, 1, 2, 3, 4, 5, 6);
    }

    [Fact]
    public void ValidatesOnlyThePreparedStarlightWireQuirk()
    {
        var request = BladeLightingProtocol.CreateStarlightSingleRequest(2, new RazerRgb(1, 2, 3));

        RazerFeatureReport.ValidatePreparedStarlightRequest(request);

        request[9] = 0x18;
        request[89] = RazerFeatureReport.CalculateCrc(request);
        Assert.Throws<ArgumentException>(() => RazerFeatureReport.ValidatePreparedStarlightRequest(request));
    }

    [Fact]
    public void RejectsPreparedStarlightColorsOutsideTheDeclaredMode()
    {
        var random = BladeLightingProtocol.CreateStarlightRandomRequest(1);
        random[12] = 1;
        random[89] = RazerFeatureReport.CalculateCrc(random);
        Assert.Throws<ArgumentException>(() => RazerFeatureReport.ValidatePreparedStarlightRequest(random));

        var single = BladeLightingProtocol.CreateStarlightSingleRequest(1, new RazerRgb(1, 2, 3));
        single[15] = 1;
        single[89] = RazerFeatureReport.CalculateCrc(single);
        Assert.Throws<ArgumentException>(() => RazerFeatureReport.ValidatePreparedStarlightRequest(single));
    }

    [Fact]
    public void BuildsSixBySeventeenMatrixRow()
    {
        var colors = Enumerable.Range(0, BladeLightingProtocol.Columns)
            .Select(value => new RazerRgb((byte)value, (byte)(value + 1), (byte)(value + 2)))
            .ToArray();

        var request = BladeLightingProtocol.CreateMatrixRowRequest(0, 5, 0, colors);

        Assert.Equal(0, request[2]);
        Assert.Equal(0x37, request[6]);
        Assert.Equal(0x03, request[7]);
        Assert.Equal(0x0B, request[8]);
        Assert.Equal(new byte[] { 0xFF, 5, 0, 16 }, request[9..13]);
        Assert.Equal(0, request[13]);
        Assert.Equal(1, request[14]);
        Assert.Equal(2, request[15]);
        Assert.Equal(16, request[61]);
        Assert.Equal(17, request[62]);
        Assert.Equal(18, request[63]);
        Assert.All(request[64..89], value => Assert.Equal(0, value));
        Assert.Equal(RazerFeatureReport.CalculateCrc(request), request[89]);
    }

    [Fact]
    public void MatchesPid710NativeLightingDriverReport()
    {
        var expected = Convert.FromHexString(
            "00000100000037030BFF00001000000001010000020000020000030000030000020000020000010100010100000200000200000300000300000200000201000200000000000000000000000000000000000000000000000000D300");
        var colors = expected[13..64]
            .Chunk(3)
            .Select(rgb => new RazerRgb(rgb[0], rgb[1], rgb[2]))
            .ToArray();

        var actual = BladeLightingProtocol.CreateMatrixRowRequest(1, 0, 0, colors);

        Assert.Equal(expected, actual);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(5, 17)]
    [InlineData(6, 0)]
    public void RejectsMatrixRangesOutsideSixBySeventeen(byte row, byte startColumn)
    {
        var colors = row == 0 && startColumn == 0
            ? Array.Empty<RazerRgb>()
            : new[] { new RazerRgb(1, 2, 3) };

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            BladeLightingProtocol.CreateMatrixRowRequest(0, row, startColumn, colors));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(30)]
    public void AcceptsOfficialProtocol25TransactionRange(byte transactionId)
    {
        var request = BladeLightingProtocol.CreateMatrixRowRequest(
            transactionId,
            0,
            0,
            new[] { new RazerRgb(1, 2, 3) });

        Assert.Equal(transactionId, request[2]);
        Assert.Equal(RazerFeatureReport.CalculateCrc(request), request[89]);
    }

    [Fact]
    public void RejectsTransactionIdOutsideOfficialProtocol25Range()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            BladeLightingProtocol.CreateMatrixRowRequest(
                31,
                0,
                0,
                new[] { new RazerRgb(1, 2, 3) }));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(5)]
    public void RejectsReactiveSpeedOutsideSourceBackedRange(byte speed)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            BladeLightingProtocol.CreateReactiveRequest(speed, new RazerRgb(1, 2, 3)));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(4)]
    public void RejectsStarlightSpeedOutsideSourceBackedRange(byte speed)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            BladeLightingProtocol.CreateStarlightRandomRequest(speed));
    }

    private static void AssertRequest(
        byte[] request,
        byte dataSize,
        byte commandId,
        params byte[] arguments)
    {
        Assert.Equal(0xFF, request[2]);
        Assert.Equal(dataSize, request[6]);
        Assert.Equal(0x03, request[7]);
        Assert.Equal(commandId, request[8]);
        Assert.Equal(arguments, request[9..(9 + arguments.Length)]);
        Assert.Equal(RazerFeatureReport.CalculateCrc(request), request[89]);
    }
}
