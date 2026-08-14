namespace OpenSynapse.Windows.Protocols;

public enum ViperBatteryChemistry : byte
{
    Alkaline = 0x00,
    RechargeableNiMh = 0x01,
    Lithium = 0x02,
}

/// <summary>
/// Source-backed SET builder from Viper 00B8 Synapse USB captures.
/// There is no accepted GET/readback command, so production sending is intentionally absent.
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
