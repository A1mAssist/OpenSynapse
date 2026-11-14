namespace OpenSynapse.Core.Macros;

/// <summary>
/// Playback modes a trigger can actually drive. Continuous playback is only
/// meaningful when the trigger can start it and later stop it.
/// </summary>
public static class MacroPlaybackChoices
{
    public static bool AllowsContinuous(MacroTriggerMode triggerMode) =>
        triggerMode is MacroTriggerMode.Toggle or MacroTriggerMode.HoldRepeat;

    public static MacroPlaybackMode Normalize(
        MacroTriggerMode triggerMode,
        MacroPlaybackMode playbackMode) =>
        !AllowsContinuous(triggerMode) && playbackMode == MacroPlaybackMode.RepeatUntilStopped
            ? MacroPlaybackMode.Once
            : playbackMode;
}
