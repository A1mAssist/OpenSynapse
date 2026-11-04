using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.Windows.Lighting;

public sealed partial class BladeLightingController
{
    /// <summary>
    /// Raised for every decoded Blade input, whether or not a Reactive/Ripple
    /// adapter is active. Studio playback listens here for its own triggers.
    /// </summary>
    internal event Action<BladeMappingInputEvent>? MappingInputObserved;

    internal void ObserveMappingInput(BladeMappingInputEvent input)
    {
        _keyboardInput?.ObserveMappingInput(input);
        MappingInputObserved?.Invoke(input);
    }
}
