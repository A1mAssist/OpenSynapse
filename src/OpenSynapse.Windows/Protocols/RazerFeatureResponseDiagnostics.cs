namespace OpenSynapse.Windows.Protocols;

internal static class RazerFeatureResponseDiagnostics
{
    internal static string DescribeResponsePrefix(ReadOnlySpan<byte> response) =>
        $"status=0x{(response.Length > 1 ? response[1] : 0):X2}, dataSize={(response.Length > 6 ? response[6] : 0)}, prefix={Prefix(response)}";

    internal static string DescribeCapabilityProbeFailure(
        ushort productId,
        string capability,
        byte[]? response,
        Exception? error)
    {
        var detail = response is null
            ? "status=-- size=-- data=--"
            : $"status={(response.Length > 1 ? response[1] : 0):X2} size={(response.Length > 6 ? response[6] : 0)} data={Prefix(response)}";
        var suffix = error is null ? string.Empty : $" error={error.GetType().Name}: {error.Message}";
        return $"capability-probe {capability} pid={productId:X4} {detail}{suffix}";
    }

    private static string Prefix(ReadOnlySpan<byte> response) =>
        Convert.ToHexString(response[..Math.Min(16, response.Length)]);
}
