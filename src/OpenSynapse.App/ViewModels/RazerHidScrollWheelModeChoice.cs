namespace OpenSynapse.App.ViewModels;

public enum RazerHidScrollWheelModeKind
{
    Tactile,
    FreeSpin,
    MicroTactile,
    SmartReel,
}

public sealed record RazerHidScrollWheelModeChoice(
    RazerHidScrollWheelModeKind Kind,
    string Title);
