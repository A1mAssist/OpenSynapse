using OpenSynapse.Windows.Protocols;
using Xunit;

namespace OpenSynapse.Core.Tests;

/// <summary>
/// The transport rotates the transaction id before every query, so a parser that only
/// holds a request rebuilt from the manifest compares a transaction id the device never
/// saw. These tests pin that a reply the transport already accepted still parses.
/// </summary>
public sealed class RazerTransactionIdRotationTests
{
    [Fact]
    public void AStartupAnimationReplyWithARotatedTransactionIdStillParses()
    {
        var request = BladeSynapsePolicyProtocol.CreateGetStartupAnimationRequest();
        var response = Respond(request, rotatedTransactionId: 0x0C, disableAnimation: 0x00);

        var parsed = BladeSynapsePolicyProtocol.ParseStartupAnimation(response, request);

        Assert.True(parsed.Enabled);
    }

    [Fact]
    public void AStartupAnimationReplyThatEchoesTheManifestTransactionIdStillParses()
    {
        var request = BladeSynapsePolicyProtocol.CreateGetStartupAnimationRequest();
        var response = Respond(request, rotatedTransactionId: request[2], disableAnimation: 0x01);

        var parsed = BladeSynapsePolicyProtocol.ParseStartupAnimation(response, request);

        Assert.False(parsed.Enabled);
    }

    [Fact]
    public void AStartupAnimationReplyForAnotherCommandIsStillRejected()
    {
        var request = BladeSynapsePolicyProtocol.CreateGetStartupAnimationRequest();
        var response = Respond(request, rotatedTransactionId: 0x0C, disableAnimation: 0x00);
        response[8] = 0x18;
        response[89] = RazerFeatureReport.CalculateCrc(response);

        Assert.Throws<InvalidOperationException>(
            () => BladeSynapsePolicyProtocol.ParseStartupAnimation(response, request));
    }

    [Fact]
    public void AStartupAnimationReplyWithABrokenChecksumIsStillRejected()
    {
        var request = BladeSynapsePolicyProtocol.CreateGetStartupAnimationRequest();
        var response = Respond(request, rotatedTransactionId: 0x0C, disableAnimation: 0x00);
        response[89] = 0x00;

        Assert.Throws<InvalidOperationException>(
            () => BladeSynapsePolicyProtocol.ParseStartupAnimation(response, request));
    }

    [Fact]
    public void AMatchesCommandGateIgnoresOnlyTheTransactionId()
    {
        var request = BladeSynapsePolicyProtocol.CreateGetStartupAnimationRequest();
        var response = Respond(request, rotatedTransactionId: 0x0C, disableAnimation: 0x00);

        Assert.False(RazerFeatureReport.Matches(request, response));
        Assert.True(RazerFeatureReport.MatchesCommand(request, response));
    }

    private static byte[] Respond(byte[] request, byte rotatedTransactionId, byte disableAnimation)
    {
        var response = (byte[])request.Clone();
        response[1] = 0x02;
        response[2] = rotatedTransactionId;
        response[RazerFeatureReport.ArgumentsOffset] = disableAnimation;
        response[89] = RazerFeatureReport.CalculateCrc(response);
        return response;
    }
}
