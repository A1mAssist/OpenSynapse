using OpenSynapse.Core.Macros;

namespace OpenSynapse.App.ViewModels;

public sealed partial class MacroWorkspace
{
    public Task SetMacroMouseSideButtonTriggerAsync(
        int mouseButton,
        CancellationToken cancellationToken = default) =>
        SetMacroButtonTriggerAsync(
            mouseButton is 4 or 5
                ? new MacroInput
                {
                    Kind = MacroInputKind.MouseButton,
                    MouseButton = mouseButton,
                }
                : null,
            cancellationToken);

    public Task SetMacroNagaSideButtonTriggerAsync(
        int sideButton,
        CancellationToken cancellationToken = default) =>
        SetMacroButtonTriggerAsync(
            sideButton is >= 1 and <= 12
                ? new MacroInput
                {
                    Kind = MacroInputKind.NagaSideButton,
                    SideButton = sideButton,
                }
                : null,
            cancellationToken);

    private async Task SetMacroButtonTriggerAsync(
        MacroInput? input,
        CancellationToken cancellationToken)
    {
        if (SelectedMacro is not { } selected || !CanEditMacro || input is null)
        {
            return;
        }

        CancelMacroCapture();
        CancelMacroStepCapture();
        CancelMacroPlaybackShortcutCapture();
        await ApplyMacroTriggerAsync(
            selected,
            new MacroTrigger { Input = input },
            cancellationToken);
    }
}
