namespace OpenSynapse.App.ViewModels;

public static class DeviceProtocolFamilies
{
    /// <summary>Every admitted Blade protocol family.</summary>
    public static bool IsBlade(string protocolFamily) =>
        protocolFamily is Blade or BladeChroma or Blade2023 or Blade2020;

    /// <summary>Families whose 03:0B keyboard matrix accepts software frames.</summary>
    public static bool UsesBladeMatrix(string protocolFamily) =>
        protocolFamily is Blade or BladeChroma;

    public const string Blade = "blade-710";
    public const string BladeChroma = "blade-710-chroma";
    public const string Blade2023 = "blade-671";
    public const string Blade2020 = "blade-597";
    public const string Viper = "viper-184";
}
