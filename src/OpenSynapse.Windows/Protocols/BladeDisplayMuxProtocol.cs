using OpenSynapse.Core.Displays;

namespace OpenSynapse.Windows.Protocols;

public static class BladeDisplayMuxProtocol
{
    public const byte TransactionId = 0x1F;

    public static byte[] CreateGetModeRequest() =>
        RazerFeatureReport.CreateRequest(
            TransactionId, 0x02, 0x0D, 0x89, new byte[] { 0x00, 0x00 });

    public static byte[] CreateSetModeRequest(BladeDisplayMuxMode mode)
    {
        if (!Enum.IsDefined(mode))
        {
            throw new ArgumentOutOfRangeException(nameof(mode));
        }

        return RazerFeatureReport.CreateRequest(
            TransactionId, 0x02, 0x0D, 0x09, new byte[] { 0x00, (byte)mode });
    }

    public static byte[] CreateGetCapabilitiesRequest() =>
        RazerFeatureReport.CreateRequest(
            TransactionId, 0x02, 0x0D, 0x8A, new byte[] { 0x00, 0x00 });

    public static BladeDisplayMuxMode ParseMode(
        ReadOnlySpan<byte> response,
        ReadOnlySpan<byte> request)
    {
        Validate(response, request);
        var raw = response[RazerFeatureReport.ArgumentsOffset + 1];
        return raw switch
        {
            0x00 => BladeDisplayMuxMode.Hybrid,
            0x01 => BladeDisplayMuxMode.Discrete,
            0x02 => BladeDisplayMuxMode.Dds,
            _ => throw new InvalidOperationException(
                $"Blade returned an unknown display MUX mode: 0x{raw:X2}."),
        };
    }

    public static BladeDisplayMuxCapabilities ParseCapabilities(
        ReadOnlySpan<byte> response,
        ReadOnlySpan<byte> request)
    {
        Validate(response, request);
        return (BladeDisplayMuxCapabilities)response[RazerFeatureReport.ArgumentsOffset + 1];
    }

    private static void Validate(ReadOnlySpan<byte> response, ReadOnlySpan<byte> request)
    {
        if (!RazerFeatureReport.IsSuccessfulResponse(request, response, 2))
        {
            throw new InvalidOperationException(
                "Blade display MUX returned an invalid or out-of-order feature report.");
        }
    }
}
