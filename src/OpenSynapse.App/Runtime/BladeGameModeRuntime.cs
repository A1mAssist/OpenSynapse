using OpenSynapse.Core.Diagnostics;
using OpenSynapse.Windows.Input;

namespace OpenSynapse.App.Runtime;

internal static class BladeGameModeRuntime
{
    public static void Apply(BladeFnRuntime? filterRuntime, bool enabled, LocalDiagnosticLog diagnostic)
    {
        filterRuntime?.SetGameModeEnabled(enabled);
        try
        {
            GameModeKeyboardSuppressor.SetEnabled(enabled);
        }
        catch (Exception exception)
        {
            diagnostic.TryWrite("blade-game-mode",
                $"Windows-key suppression unavailable: {exception.GetType().Name}: {exception.Message}");
        }
    }
}
