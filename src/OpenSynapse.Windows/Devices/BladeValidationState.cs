namespace OpenSynapse.Windows.Devices;

internal sealed class BladeValidationState
{
    public string? BrightnessPath { get; set; }
    public string? PerformancePath { get; set; }
    public string? BoostPath { get; set; }
    public string? ChargeLimitPath { get; set; }
    public string? PowerModePath { get; set; }
    public string? KeyboardEffectPath { get; set; }
    public string? LocalDimmingPath { get; set; }
    public string? LogoPath { get; set; }
    public string? StartupAnimationPath { get; set; }
    public string? NativeDisplayModePath { get; set; }

    public void Reset()
    {
        BrightnessPath = null;
        PerformancePath = null;
        BoostPath = null;
        ChargeLimitPath = null;
        PowerModePath = null;
        KeyboardEffectPath = null;
        LocalDimmingPath = null;
        LogoPath = null;
        StartupAnimationPath = null;
        NativeDisplayModePath = null;
    }
}
