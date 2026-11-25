namespace OpenSynapse.Core.Macros;

/// <summary>How a mapping whose target key is already owned by the host resolves.</summary>
public enum HostRemapOutcome
{
    /// <summary>Nothing changes: the firmware writes the target button id.</summary>
    FirmwareChain,
    /// <summary>The source key reports itself and the host injects the target button.</summary>
    HostInject,
    /// <summary>The source key takes the target's original firmware assignment.</summary>
    RestoreNativeAssignment,
    /// <summary>No faithful single-level result exists; the row must not be written.</summary>
    Unsupported,
}

public static class HostRemapPolicy
{
    /// <summary>
    /// Windows only has an injection path for the five standard buttons; nothing
    /// above XBUTTON2 can be synthesized, so those targets stay unresolved.
    /// </summary>
    public const int MaxInjectableMouseButton = 5;

    /// <summary>
    /// Razer Synapse resolves one level: a key mapped onto a target runs the target's
    /// native function, never the macro the target itself is bound to. Writing the
    /// target's button id into the firmware instead hands the press to that macro,
    /// which is the nested behaviour users report.
    /// </summary>
    public static HostRemapOutcome Decide(
        byte sourceButtonId,
        byte targetButtonId,
        MacroInput? targetInput,
        bool targetOccupied)
    {
        if (sourceButtonId == targetButtonId || targetInput is null || !targetOccupied)
        {
            return HostRemapOutcome.FirmwareChain;
        }

        return targetInput.Kind switch
        {
            MacroInputKind.MouseButton when
                targetInput.MouseButton is >= 1 and <= MaxInjectableMouseButton
                => HostRemapOutcome.HostInject,
            MacroInputKind.MouseButton => HostRemapOutcome.Unsupported,
            MacroInputKind.RazerKey or MacroInputKind.NagaSideButton or MacroInputKind.DeviceButton
                => HostRemapOutcome.RestoreNativeAssignment,
            _ => HostRemapOutcome.Unsupported,
        };
    }
}
