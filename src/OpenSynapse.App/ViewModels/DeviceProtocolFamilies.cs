namespace OpenSynapse.App.ViewModels;

public static class DeviceProtocolFamilies
{
    /// <summary>Both admitted Blade protocol families.</summary>
    public static bool IsBlade(string protocolFamily) =>
        protocolFamily is Blade or Blade2023;

    public const string Blade = "blade-710";
    public const string Blade2023 = "blade-671";
    public const string Viper = "viper-184";
}
