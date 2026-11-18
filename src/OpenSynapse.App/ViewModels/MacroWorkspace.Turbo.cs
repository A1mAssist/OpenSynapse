using OpenSynapse.Core.Macros;
using OpenSynapse.Windows.Input;

namespace OpenSynapse.App.ViewModels;

public sealed partial class MacroWorkspace
{
    internal void SetTurboResolver(Func<MacroInput, bool, RazerTurboBinding?>? resolver) =>
        _macroRuntime.TurboResolver = resolver;

    internal void StopAllTurbo() => _macroRuntime.StopAllTurbo();
}
