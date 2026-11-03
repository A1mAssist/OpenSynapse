namespace OpenSynapse.Windows.Input;

internal static class MacroMouseInputPolicy
{
    // Razer's driver can inject side-button events. Keep ignoring injected
    // clicks and wheels so another tool cannot fire every macro.
    internal static bool AcceptsMouseButton(uint flags, nuint extraInfo, int button) =>
        extraInfo != WindowsMacroInputSink.InjectionTag &&
        ((flags & 0x01) == 0 || button is 4 or 5);
}
