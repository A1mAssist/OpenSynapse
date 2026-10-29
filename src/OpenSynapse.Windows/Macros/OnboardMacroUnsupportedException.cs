namespace OpenSynapse.Windows.Macros;

/// <summary>
/// One local macro step cannot be expressed in the onboard stream. StepIndex is 1-based
/// so the UI can point at the step the user has to change.
/// </summary>
public sealed class OnboardMacroUnsupportedException : InvalidOperationException
{
    public OnboardMacroUnsupportedException(int stepIndex, string reason)
        : base(reason)
    {
        StepIndex = stepIndex;
    }

    public int StepIndex { get; }

    public string Reason => Message;
}
