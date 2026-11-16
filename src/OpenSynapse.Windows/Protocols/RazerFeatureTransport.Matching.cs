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
        if (candidate[1] != 0x01)
        {
            return RazerResponseMatch.Mismatched;
        }

        // A same-exchange BUSY that failed validation (CRC or report id) is corruption,
        // not a receipt left by an earlier command; keep its mismatch reason.
        var sameExchange =
            candidate.Length == request.Length &&
            (candidate[2] == request[2] || request[2] == 0xFF) &&
            (allowRemainingPacketsMismatch ||
             (candidate[3] == request[3] && candidate[4] == request[4])) &&
            candidate[7] == request[7] &&
            candidate[8] == request[8];
        return sameExchange ? RazerResponseMatch.Mismatched : RazerResponseMatch.BusyReceipt;
    }
}
