using OpenSynapse.Windows.Devices;
using OpenSynapse.Windows.Protocols;
using Xunit;

namespace OpenSynapse.Core.Tests;

public sealed class OpenRazerPollingRateTests
{
    /// <summary>
    /// OpenRazer sends get_polling_rate2 with data_size 1 and documents the rate as
    /// argument 1, so the firmware answers with a one-byte payload whose second byte
    /// carries the rate. Upstream reads that byte without checking the declared length.
    /// </summary>
    [Theory]
    [InlineData(0x01, 8000)]
    [InlineData(0x02, 4000)]
    [InlineData(0x04, 2000)]
    [InlineData(0x08, 1000)]
    [InlineData(0x10, 500)]
    [InlineData(0x40, 125)]
    public void HighPollingRateReadsArgumentOneOfAOneByteResponse(byte code, int expected)
    {
        var response = RazerFeatureReport.CreateRequest(0x1F, 0x01, 0x00, 0xC0, [0x00]);
        response[1] = 0x02;
        response[RazerFeatureReport.ArgumentsOffset + 1] = code;
        response[89] = RazerFeatureReport.CalculateCrc(response);

        Assert.Equal(expected, OpenRazerMouseProtocol.ParsePollingRate(response, highRate: true));
    }

    [Fact]
    public void HighPollingRateStillRejectsAnUnknownCode()
    {
        var response = RazerFeatureReport.CreateRequest(0x1F, 0x01, 0x00, 0xC0, [0x00]);
        response[1] = 0x02;
        response[RazerFeatureReport.ArgumentsOffset + 1] = 0x7F;
        response[89] = RazerFeatureReport.CalculateCrc(response);

        Assert.Throws<InvalidDataException>(() =>
            OpenRazerMouseProtocol.ParsePollingRate(response, highRate: true));
    }

    [Fact]
    public void LegacyPollingRateStillReadsArgumentZero()
    {
        var response = RazerFeatureReport.CreateRequest(0x1F, 0x01, 0x00, 0x85, [0x02]);
        response[1] = 0x02;
        response[89] = RazerFeatureReport.CalculateCrc(response);

        Assert.Equal(500, OpenRazerMouseProtocol.ParsePollingRate(response, highRate: false));
    }
}

