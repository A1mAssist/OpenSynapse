namespace OpenSynapse.Windows.Protocols;

public enum ViperBatteryChemistry : byte
{
    Alkaline = 0x00,
    RechargeableNiMh = 0x01,
    Lithium = 0x02,
}

/// <summary>
/// Source-backed battery discharge-curve commands from the Product 184
/// rzDevice25 dependency. Product 184 calls SET; GET must still pass a real
/// device probe before it can be used as a production readback gate.
/// </summary>
public static class ViperBatteryChemistryProtocol
{
    public static byte[] CreateGetRequest() =>
        RazerFeatureReport.CreateRequest(0x1F, 0x01, 0x07, 0x94, []);

    public static byte[] CreateSetRequest(ViperBatteryChemistry chemistry)
    {
        if (!Enum.IsDefined(chemistry))
        {
            throw new ArgumentOutOfRangeException(nameof(chemistry));
        }

        return RazerFeatureReport.CreateRequest(
            0x1F, 0x01, 0x07, 0x14, new[] { (byte)chemistry });
    }

    public static ViperBatteryChemistry ParseGetResponse(ReadOnlySpan<byte> response)
    {
        var request = CreateGetRequest();
        if (!RazerFeatureReport.IsSuccessfulResponse(request, response, 1))
        {
            throw new InvalidOperationException("Viper 电池类型返回了无效或错序的 feature report。");
        }

        var chemistry = (ViperBatteryChemistry)response[RazerFeatureReport.ArgumentsOffset];
        return Enum.IsDefined(chemistry)
            ? chemistry
            : throw new InvalidOperationException($"Viper 返回了未知电池类型 0x{(byte)chemistry:X2}。");
    }
}
