namespace OpenSynapse.Windows.Macros;

/// <summary>
/// One macro stored in the keyboard's onboard memory, with its decoded item stream
/// and the raw bytes needed to write it back unchanged.
/// </summary>
public sealed record RazerOnboardMacro(
    byte Id,
    int DataSize,
    byte[] Raw,
    IReadOnlyList<OnboardMacroItem> Items);
