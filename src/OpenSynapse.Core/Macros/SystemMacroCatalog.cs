namespace OpenSynapse.Core.Macros;

/// <summary>
/// The built-in macros a profile can carry: Windows shortcuts and the host-side
/// native mouse buttons, each rebuilt with the triggers the user saved for it.
/// </summary>
public static class SystemMacroCatalog
{
    public static IReadOnlyList<MacroDefinition> Bindings(IEnumerable<MacroDefinition> stored) =>
        SoftwareShortcutCatalog.Bindings(stored)
            .Concat(NativeMouseButtonCatalog.Bindings(stored))
            .ToArray();
}
