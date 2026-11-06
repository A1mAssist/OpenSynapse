namespace OpenSynapse.Windows.Macros;

/// <summary>
/// The device accepted a write but read back different bytes. Treat this as a
/// device-session write failure, not as unsupported macro content.
/// </summary>
public sealed class OnboardMacroVerificationException : InvalidOperationException
{
    public OnboardMacroVerificationException(byte slot, bool nameBlock)
        : base(nameBlock
            ? $"Onboard macro {slot} did not read back the name block that was written."
            : $"Onboard macro {slot} did not read back the bytes that were written.")
    {
        Slot = slot;
        NameBlock = nameBlock;
    }

    public byte Slot { get; }
    public bool NameBlock { get; }
}
