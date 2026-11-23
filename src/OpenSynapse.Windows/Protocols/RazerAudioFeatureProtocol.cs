namespace OpenSynapse.Windows.Protocols;

internal static class RazerAudioFeatureProtocol
{
    internal static bool IsNommoV2(ushort productId) => productId is 0x055A or 0x055C or 0x055E;
    internal static bool HasProductCapabilities(ushort productId) => IsNommoV2(productId) || productId == 0x0F66;
    internal static bool SupportsHidEqBands(ushort productId) => productId == 0x0F66;

    internal static bool IsSupportedPreset(ushort productId, byte preset) =>
        !HasProductCapabilities(productId) ||
        (IsNommoV2(productId) ? preset <= 4 : preset is 1 or 2 or 3 or 16);

    internal static void ValidatePreset(ushort productId, byte preset)
    {
        if (!IsSupportedPreset(productId, preset))
            throw new ArgumentOutOfRangeException(nameof(preset), "The EQ preset is not supported by this product.");
    }

    internal static void ValidateEqBands(ushort productId, int count)
    {
        if (!HasProductCapabilities(productId)) return;
        if (!SupportsHidEqBands(productId))
            throw new NotSupportedException("Nommo V2 equalizer bands are controlled by THX, not HID audio.");
        if (count != 10)
            throw new ArgumentException("Marci requires ten EQ bands.", nameof(count));
    }

    internal static void ValidateBass(ushort productId, byte level)
    {
        if (!HasProductCapabilities(productId)) return;
        if (!IsNommoV2(productId))
            throw new NotSupportedException("Marci does not expose this HID bass control.");
        if (level is < 1 or > 7)
            throw new ArgumentOutOfRangeException(nameof(level), "Nommo V2 bass must be between 1 and 7.");
    }

    // Persisted/UI bass is a percentage; the Nommo firmware accepts seven discrete levels.
    internal static byte EncodeNommoBass(double percent) =>
        (byte)Math.Clamp(1 + (int)Math.Round(percent * 6 / 100), 1, 7);
    internal static double DecodeNommoBass(byte level) => Math.Clamp((level - 1) * 100d / 6, 0, 100);

    internal static byte EncodeMarciEqBand(double decibels)
    {
        if (!double.IsFinite(decibels) || decibels < -12.8 || decibels > 12.7)
            throw new ArgumentOutOfRangeException(nameof(decibels));
        // Match Math.round in rzDevice25MarciT1, including negative half steps.
        return unchecked((byte)(sbyte)Math.Floor(decibels * 10 + 0.5));
    }

    internal static double DecodeMarciEqBand(byte value) => unchecked((sbyte)value) / 10d;

    internal static bool SupportsCommand(ushort productId, byte commandClass, byte commandId) =>
        !HasProductCapabilities(productId) || ((commandClass, commandId) switch
        {
            (0x00, 0x81 or 0x82 or 0x86) => true,
            (0x08, 0x82 or 0x02) => true,
            (0x08, 0x87 or 0x07) => IsNommoV2(productId),
            (0x08, 0x84 or 0x04) => SupportsHidEqBands(productId),
            _ => false,
        });

    internal static void PrepareAudioCommand(ushort productId, Span<byte> report)
    {
        if (!SupportsCommand(productId, report[7], report[8]))
            throw new NotSupportedException($"PID {productId:X4} does not expose HID audio command {report[7]:X2}:{report[8]:X2}.");
        if (!HasProductCapabilities(productId)) return;

        if (report[7] == 0x08 && report[8] is 0x82 or 0x02)
        {
            report[9] = IsNommoV2(productId) ? (byte)1 : (byte)0;
            if (report[8] == 0x02) ValidatePreset(productId, report[10]);
        }
        if (report[7] == 0x08 && report[8] == 0x07) ValidateBass(productId, report[10]);
        if (report[7] == 0x08 && report[8] is 0x84 or 0x04) ValidateEqBands(productId, report[6] - 1);
        report[89] = RazerFeatureReport.CalculateCrc(report);
    }

    // rzDevice25MarciT1 uses the consumer-control feature collection; FF14 is Chroma.
    internal static bool MatchesInterface(
        ushort productId, ushort featureReportLength, ushort usagePage, ushort usage,
        IReadOnlySet<byte>? featureReportIds = null) =>
        featureReportLength == RazerFeatureReport.Length &&
        (productId != 0x0F66 || (usagePage == 0x000C && usage == 0x0001));

    // Parser metadata is positive evidence, not a prerequisite for the established JS route.
    internal static int InterfaceEvidenceRank(ushort productId, IReadOnlySet<byte>? featureReportIds) =>
        !HasProductCapabilities(productId) ? 0 :
        featureReportIds?.Contains(IsNommoV2(productId) ? (byte)7 : (byte)16) == true ? 2 :
        featureReportIds is null ? 1 : 0;

    internal static void PrepareRequest(ushort productId, Span<byte> report)
    {
        RazerFeatureReport.ValidatePreparedRequest(report);
        switch (productId)
        {
            case 0x055A:
            case 0x055C:
            case 0x055E:
                // All three main bundles select rzDevice25NommoV2Pro (reportId=7).
                report[0] = 7;
                break;
            case 0x0F66:
                report[0] = 16;
                report[89] = CalculateMarciChecksum(report);
                break;
        }
    }

    internal static void ValidatePreparedRequest(ushort productId, ReadOnlySpan<byte> report)
    {
        if (productId != 0x0F66)
        {
            RazerFeatureReport.ValidatePreparedRequest(report);
            return;
        }

        if (report.Length != RazerFeatureReport.Length || report[0] != 16 ||
            report[89] != CalculateMarciChecksum(report))
        {
            throw new ArgumentException("Prepared Marci feature request is invalid.", nameof(report));
        }

        // Reuse the standard header/length checks after validating Marci's wire checksum.
        Span<byte> standard = stackalloc byte[RazerFeatureReport.Length];
        report.CopyTo(standard);
        standard[89] = RazerFeatureReport.CalculateCrc(standard);
        RazerFeatureReport.ValidatePreparedRequest(standard);
    }

    private static byte CalculateMarciChecksum(ReadOnlySpan<byte> report)
    {
        byte checksum = 0;
        // JS XORs logical bytes 2..58 inclusive, before the Windows report-id prefix.
        for (var index = 3; index <= 59; index++)
        {
            checksum ^= report[index];
        }
        return checksum;
    }

    internal static bool MatchesMarciResponse(ReadOnlySpan<byte> request, ReadOnlySpan<byte> response) =>
        request.Length == RazerFeatureReport.Length &&
        response.Length == RazerFeatureReport.Length &&
        request[0] == 16 && response[0] == 16 &&
        response[2] == request[2] && response[6] <= 80 &&
        response[7] == request[7] && response[8] == request[8];
}
