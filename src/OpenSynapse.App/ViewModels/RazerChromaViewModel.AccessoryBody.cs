namespace OpenSynapse.App.ViewModels;

public sealed partial class RazerChromaViewModel
{
    /// <summary>
    /// Device-specific controls for accessories that also expose a Chroma endpoint.
    /// Null for lighting-only devices such as lamps and stands.
    /// </summary>
    public RazerAccessoryBodyViewModel? Body { get; private set; }
}
