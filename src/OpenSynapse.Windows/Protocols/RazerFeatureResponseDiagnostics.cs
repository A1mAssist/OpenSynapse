namespace OpenSynapse.Windows.Protocols;

internal static class RazerFeatureResponseDiagnostics
{
    internal static string DescribeResponsePrefix(ReadOnlySpan<byte> response) =>
        $"status=0x{(response.Length > 1 ? response[1] : 0):X2}, dataSize={(response.Length > 6 ? response[6] : 0)}, prefix={Convert.ToHexString(response[..Math.Min(16, response.Length)])}";
}
