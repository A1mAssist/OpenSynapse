using OpenSynapse.Core.Macros;

namespace OpenSynapse.App.ViewModels;

public sealed partial class MacroWorkspace
{
    /// <summary>
    /// Built-in actions with their saved bindings. Not part of <see cref="Items"/>:
    /// the macro editor only ever shows the user's own library.
    /// </summary>
    public IReadOnlyList<MacroDefinition> SystemShortcuts =>
        SoftwareShortcutCatalog.Bindings(_getActiveProfile().SystemMacros);

    /// <summary>
    /// Binds a private Razer key through the device workspace, which owns the
    /// assignment backup and the Off write. Null keeps the local trigger path.
    /// </summary>
    public Func<string, MacroInput, MacroTriggerMode, MacroPlaybackMode, int, bool,
        MacroSoftwareBinding?, CancellationToken, Task<string?>>? SoftwareTriggerBinder { get; set; }

    public event EventHandler? TriggerChanged;

    internal void NotifyTriggerChanged() => TriggerChanged?.Invoke(this, EventArgs.Empty);
}
