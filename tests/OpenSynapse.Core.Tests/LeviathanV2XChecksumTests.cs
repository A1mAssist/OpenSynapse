using OpenSynapse.Windows.Protocols;
using Xunit;

namespace OpenSynapse.Core.Tests;

/// <summary>
/// The Leviathan V2 X folds the transaction id into the checksum of its replies, so a
/// response that carries its transaction id in the checksum fails the shared range by
/// exactly that id. The device log shows it: the firmware answered every probe with the
/// right status, transaction, class, command and length, and only the checksum differed.
/// </summary>
public sealed class LeviathanV2XChecksumTests
{
    [Theory]
    [InlineData(0x08, 0x8D, 0x85)]
    [InlineData(0x09, 0xD5, 0xDC)]
    [InlineData(0x0A, 0x85, 0x8F)]
    public void LeviathanResponseChecksumIncludesTheTransactionId(
        byte transactionId,
        byte deviceChecksum,
        byte sharedChecksum)
    {
        Assert.Equal(deviceChecksum, (byte)(sharedChecksum ^ transactionId));
    }

    [Fact]
    public void LeviathanResponseMatchesWithTheTransactionChecksum()
    {
        var request = RazerFeatureReport.CreateRequest(0x08, 0x02, 0x00, 0x81, []);
        var response = RazerFeatureReport.CreateRequest(0x08, 0x02, 0x00, 0x81, []);
        response[0] = LeviathanV2XProtocol.ReportId;
        response[1] = 0x02;
        response[9] = 0x01;
        response[10] = 0x06;
        response[89] = ChecksumIncludingTransaction(response);

        Assert.False(RazerFeatureReport.Matches(request, response));
        Assert.True(RazerFeatureReport.MatchesWithTransactionChecksum(request, response));
    }

    [Fact]
    public void TheSharedChecksumStillDrivesEveryOtherDevice()
    {
        var request = RazerFeatureReport.CreateRequest(0x1F, 0x02, 0x00, 0x81, []);
        var response = RazerFeatureReport.CreateRequest(0x1F, 0x02, 0x00, 0x81, []);
        response[1] = 0x02;
        response[9] = 0x01;
        response[10] = 0x06;
        response[89] = RazerFeatureReport.CalculateCrc(response);

        Assert.True(RazerFeatureReport.Matches(request, response));
        Assert.False(RazerFeatureReport.MatchesWithTransactionChecksum(request, response));
    }

    /// <summary>
    /// The transport change alone was not enough: the parse helpers re-validate the
    /// checksum, so a folded reply still threw InvalidDataException and every read that
    /// parsed a value showed up as unsupported.
    /// </summary>
    [Fact]
    public void TheParserAcceptsTheSoundbarsFoldedChecksum()
    {
        var response = RazerFeatureReport.CreateRequest(0x08, 0x02, 0x00, 0x81, []);
        response[0] = LeviathanV2XProtocol.ReportId;
        response[1] = 0x02;
        response[9] = 0x01;
        response[10] = 0x06;
        response[89] = ChecksumIncludingTransaction(response);

        Assert.Equal([0x01, 0x06], OpenRazerStandardProtocol.Arguments(response, 2).ToArray());
    }

    private static byte ChecksumIncludingTransaction(byte[] report)
    {
        byte crc = 0;
        for (var index = 2; index <= 88; index++)
        {
            crc ^= report[index];
        }

        return crc;
    }
}
