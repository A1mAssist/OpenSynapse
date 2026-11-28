using OpenSynapse.Core.Macros;

namespace OpenSynapse.Windows.Macros;

/// <summary>Restores binding edits without replacing recorded steps or the macro object.</summary>
public sealed class MacroBindingSnapshot(MacroDefinition macro)
{
    private readonly MacroTrigger? _primary = macro.Trigger?.Clone();
    private readonly List<MacroTrigger> _additional = macro.AdditionalTriggers.Select(trigger => trigger.Clone()).ToList();
    private readonly MacroSoftwareBinding? _binding = macro.SoftwareBinding?.Clone();
    private readonly MacroPlaybackMode _playback = macro.PlaybackMode;
    private readonly int _repeat = macro.RepeatCount;

    public void Restore()
    {
        macro.Trigger = _primary?.Clone();
        macro.AdditionalTriggers = _additional.Select(trigger => trigger.Clone()).ToList();
        macro.SoftwareBinding = _binding?.Clone();
        macro.PlaybackMode = _playback;
        macro.RepeatCount = _repeat;
    }
}
