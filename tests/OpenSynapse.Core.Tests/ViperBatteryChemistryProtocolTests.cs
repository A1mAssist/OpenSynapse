using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.Core.Tests;

public sealed class ViperBatteryChemistryProtocolTests
{
    [Fact]
    public void BuildsOfficialGetDischargeCurveReport()
    {
        var report = ViperBatteryChemistryProtocol.CreateGetRequest();

        Assert.Equal(0x1F, report[2]);
        Assert.Equal(0x01, report[6]);
        Assert.Equal(0x07, report[7]);
        Assert.Equal(0x94, report[8]);
        Assert.Equal(RazerFeatureReport.CalculateCrc(report), report[89]);
    }

    [Theory]
    [InlineData(ViperBatteryChemistry.Alkaline, 0x00, 0x12)]
    [InlineData(ViperBatteryChemistry.RechargeableNiMh, 0x01, 0x13)]
    [InlineData(ViperBatteryChemistry.Lithium, 0x02, 0x10)]
    public void BuildsExactCapturedSetReport(
        ViperBatteryChemistry chemistry,
        byte raw,
        byte crc)
    {
        var report = ViperBatteryChemistryProtocol.CreateSetRequest(chemistry);

        Assert.Equal(RazerFeatureReport.Length, report.Length);
        Assert.Equal(0x1F, report[2]);
        Assert.Equal(0x01, report[6]);
        Assert.Equal(0x07, report[7]);
        Assert.Equal(0x14, report[8]);
        Assert.Equal(raw, report[RazerFeatureReport.ArgumentsOffset]);
        Assert.Equal(crc, report[89]);
        Assert.Equal(0x00, report[90]);
    }

    [Fact]
    public void RejectsUnknownChemistry()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            ViperBatteryChemistryProtocol.CreateSetRequest((ViperBatteryChemistry)0x03));
    }

    [Theory]
    [InlineData(ViperBatteryChemistry.Alkaline)]
    [InlineData(ViperBatteryChemistry.RechargeableNiMh)]
    [InlineData(ViperBatteryChemistry.Lithium)]
    public void ParsesOfficialGetDischargeCurveResponse(ViperBatteryChemistry expected)
    {
        var response = ViperBatteryChemistryProtocol.CreateGetRequest();
        response[1] = 0x02;
        response[RazerFeatureReport.ArgumentsOffset] = (byte)expected;
        response[89] = RazerFeatureReport.CalculateCrc(response);

        Assert.Equal(expected, ViperBatteryChemistryProtocol.ParseGetResponse(response));
    }

    [Fact]
    public void RejectsUnknownGetDischargeCurveValue()
    {
        var response = ViperBatteryChemistryProtocol.CreateGetRequest();
        response[1] = 0x02;
        response[RazerFeatureReport.ArgumentsOffset] = 0x03;
        response[89] = RazerFeatureReport.CalculateCrc(response);

        Assert.Throws<InvalidOperationException>(() =>
            ViperBatteryChemistryProtocol.ParseGetResponse(response));
    }
}
