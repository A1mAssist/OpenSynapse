using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.Core.Tests;

public sealed class BladeFanProtocolTests
{
    [Theory]
    [InlineData(0x01)]
    [InlineData(0x02)]
    public void BuildsExactGetRequest(byte zone)
    {
        var request = BladeFanProtocol.CreateGetTargetRequest(zone);

        Assert.Equal(0x1F, request[2]);
        Assert.Equal(0x03, request[6]);
        Assert.Equal(0x0D, request[7]);
        Assert.Equal(0x81, request[8]);
        Assert.Equal(new byte[] { 0x00, zone, 0x00 }, request[9..12]);
        Assert.Equal(RazerFeatureReport.CalculateCrc(request), request[89]);
    }

    [Theory]
    [InlineData(0x01)]
    [InlineData(0x02)]
    public void BuildsExactSetRequestForAllowedRpm(byte zone)
    {
        var request = BladeFanProtocol.CreateSetTargetRequest(zone, 3400);

        Assert.Equal(0x1F, request[2]);
        Assert.Equal(0x03, request[6]);
        Assert.Equal(0x0D, request[7]);
        Assert.Equal(0x01, request[8]);
        Assert.Equal(new byte[] { 0x00, zone, 0x22 }, request[9..12]);
        Assert.Equal(RazerFeatureReport.CalculateCrc(request), request[89]);
    }

    [Theory]
    [InlineData(2000)]
    [InlineData(5000)]
    [InlineData(3400)]
    public void ParsesAllowedTarget(int rpm)
    {
        var response = CreateResponse(BladeFanProtocol.ZoneCpu, (byte)(rpm / 100));

        Assert.Equal(rpm, BladeFanProtocol.ParseTarget(response, BladeFanProtocol.ZoneCpu));
    }

    [Theory]
    [InlineData(1900)]
    [InlineData(5100)]
    [InlineData(3350)]
    public void RejectsSetRpmOutsideExactRange(int rpm)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            BladeFanProtocol.CreateSetTargetRequest(BladeFanProtocol.ZoneCpu, rpm));
    }

    [Theory]
    [InlineData(19)]
    [InlineData(51)]
    public void RejectsParsedRawTargetOutsideExactRange(byte raw)
    {
        Assert.Throws<InvalidOperationException>(() =>
            BladeFanProtocol.ParseTarget(
                CreateResponse(BladeFanProtocol.ZoneCpu, raw), BladeFanProtocol.ZoneCpu));
    }

    [Theory]
    [InlineData(1900)]
    [InlineData(2000)]
    [InlineData(5000)]
    public void CurveParserAndBuilderAllowSourceBackedRange(int rpm)
    {
        var request = BladeFanProtocol.CreateSetCurveTargetRequest(
            BladeFanProtocol.ZoneCpu, rpm);
        Assert.Equal((byte)(rpm / 100), request[11]);

        var response = CreateResponse(BladeFanProtocol.ZoneCpu, (byte)(rpm / 100));
        Assert.Equal(
            rpm,
            BladeFanProtocol.ParseCurveTarget(
                response,
                BladeFanProtocol.ZoneCpu,
                BladeFanProtocol.CreateGetTargetRequest(BladeFanProtocol.ZoneCpu)));
    }

    [Fact]
    public void CurveParserRejectsFixedFanOutOfRangeRawValue()
    {
        Assert.Throws<InvalidOperationException>(() =>
            BladeFanProtocol.ParseCurveTarget(
                CreateResponse(BladeFanProtocol.ZoneCpu, 51),
                BladeFanProtocol.ZoneCpu,
                BladeFanProtocol.CreateGetTargetRequest(BladeFanProtocol.ZoneCpu)));
    }

    [Fact]
    public void RejectsUnknownZoneAndCrossZoneResponse()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => BladeFanProtocol.CreateGetTargetRequest(0x03));
        Assert.Throws<InvalidOperationException>(() =>
            BladeFanProtocol.ParseTarget(
                CreateResponse(BladeFanProtocol.ZoneGpu, 34), BladeFanProtocol.ZoneCpu));
    }

    [Fact]
    public void RejectsCorruptOrWrongSizedResponse()
    {
        var response = CreateResponse(BladeFanProtocol.ZoneCpu, 34);
        response[89] ^= 0xFF;
        Assert.Throws<InvalidOperationException>(() =>
            BladeFanProtocol.ParseTarget(response, BladeFanProtocol.ZoneCpu));

        response = CreateResponse(BladeFanProtocol.ZoneCpu, 34);
        response[6] = 0x04;
        response[89] = RazerFeatureReport.CalculateCrc(response);
        Assert.Throws<InvalidOperationException>(() =>
            BladeFanProtocol.ParseTarget(response, BladeFanProtocol.ZoneCpu));
    }

    private static byte[] CreateResponse(byte zone, byte raw)
    {
        var response = RazerFeatureReport.CreateRequest(
            0x1F,
            0x03,
            0x0D,
            0x81,
            new byte[] { 0x00, zone, raw });
        response[1] = 0x02;
        return response;
    }
}
