namespace OpenSynapse.Windows.Input;

/// <summary>
/// Opt-in tracing for the macro trigger path. Set OPENSYNAPSE_MACRO_TRACE=1
/// to log what the hooks received and why a trigger did or did not match.
/// </summary>
internal static class MacroInputTrace
{
    internal static bool Enabled { get; } =
        string.Equals(
            Environment.GetEnvironmentVariable("OPENSYNAPSE_MACRO_TRACE"),
            "1",
            StringComparison.Ordinal);
}
