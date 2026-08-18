namespace OpenSynapse.Windows.Protocols;

public enum ViperBatteryChemistry : byte
{
    Alkaline = 0x00,
    RechargeableNiMh = 0x01,
    Lithium = 0x02,
}

/// <summary>
/// Source-backed SET builder from Viper 00B8 Synapse USB captures.
/// Product 184 only calls SET. A shared rzDevice25 dependency defines GET 07/94,
/// but that is not Product 184 evidence, so production sending remains absent.
/// </summary>
public static class ViperBatteryChemistryProtocol
{
    public static byte[] CreateSetRequest(ViperBatteryChemistry chemistry)
    {
        if (!Enum.IsDefined(chemistry))
        {
            throw new ArgumentOutOfRangeException(nameof(chemistry));
        }

        return RazerFeatureReport.CreateRequest(
            0x1F, 0x01, 0x07, 0x14, new[] { (byte)chemistry });
    }
}
