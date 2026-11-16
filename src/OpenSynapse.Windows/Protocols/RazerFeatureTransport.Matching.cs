namespace OpenSynapse.Windows.Protocols;

public sealed partial class RazerFeatureTransport
{
    internal static RazerResponseMatch ClassifyResponse(
        ReadOnlySpan<byte> request,
        byte[] candidate,
        byte responseReportId,
        bool usesLeviathanReportId,
        bool allowRemainingPacketsMismatch,
        bool usesMouseDockLinker = false)
    {
        var matches = usesLeviathanReportId
            ? RazerFeatureReport.MatchesLeviathanResponse(request, candidate, allowRemainingPacketsMismatch)
            : usesMouseDockLinker
                ? RazerFeatureReport.MatchesMouseDockLinkerResponse(request, candidate, allowRemainingPacketsMismatch)
                : RazerFeatureReport.Matches(request, candidate, allowRemainingPacketsMismatch);
        // The 1354 middleware never checks the reply's report id; the feature collection
        // only has one feature report and the transaction plus command identify it.
        if (!usesLeviathanReportId && !RazerFeatureReport.MatchesReportId(candidate, responseReportId))
        {
            matches = false;
        }
        if (matches)
        {
            if (usesMouseDockLinker)
            {
                // Downstream parsers still enforce a checksum. Normalize the dock reply
                // after the middleware's transaction/command gate accepted it.
                candidate[89] = RazerFeatureReport.CalculateCrc(candidate);
            }
            return RazerResponseMatch.Matched;
        }
        return candidate[1] == 0x01 ? RazerResponseMatch.BusyReceipt : RazerResponseMatch.Mismatched;
    }
}
