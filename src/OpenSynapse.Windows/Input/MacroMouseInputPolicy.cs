namespace OpenSynapse.Windows.Input;

internal static class MacroMouseInputPolicy
{
    // Razer's driver re-injects the buttons it rewrites, so an injected button
    // is usually still a physical one. Only a running playback can produce
    // untagged output of our own, so that is the window where injected input
    // has to stay ignored.
    internal static bool AcceptsMouseButton(
        uint flags, nuint extraInfo, int button, bool playbackActive) =>
        extraInfo != WindowsMacroInputSink.InjectionTag &&
        (!playbackActive || (flags & 0x01) == 0 || button is 4 or 5);
}
