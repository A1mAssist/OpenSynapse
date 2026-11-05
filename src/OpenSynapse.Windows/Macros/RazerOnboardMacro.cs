namespace OpenSynapse.Windows.Macros;

/// <summary>
/// One macro stored in the keyboard's onboard memory, with its decoded item stream,
/// the raw bytes needed to write it back unchanged, and the optional name block.
/// </summary>
public sealed record RazerOnboardMacro(
    byte Id,
    int DataSize,
    byte[] Raw,
    IReadOnlyList<OnboardMacroItem> Items,
    RazerOnboardMacroName? NameInfo = null)
{
    public Guid Guid => NameInfo?.Guid ?? Guid.Empty;
    public string Name => NameInfo?.Name ?? string.Empty;
    public int NumItems => NameInfo?.NumItems ?? Items.Count;
    public string Timestamp => NameInfo?.Timestamp ?? string.Empty;
    public string Hash => NameInfo?.Hash ?? string.Empty;
}

/// <summary>Slot identity used by mapping pages that do not need the macro stream.</summary>
public sealed record RazerOnboardMacroSlotInfo(byte Id, RazerOnboardMacroName? NameInfo);
