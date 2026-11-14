using OpenSynapse.Core.Macros;

namespace OpenSynapse.App.ViewModels;

public sealed partial class MacroWorkspace
{
    public Task AddMacroClickAtAsync(
        int x,
        int y,
        int mouseButton,
        CancellationToken cancellationToken = default) =>
        AddMacroStepsAsync(
            [MacroStepFactory.CreateClickAt(x, y, mouseButton)],
            cancellationToken);
}
