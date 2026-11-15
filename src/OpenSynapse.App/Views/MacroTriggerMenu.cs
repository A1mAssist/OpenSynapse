using Microsoft.UI.Xaml.Controls;
using OpenSynapse.App.ViewModels;

namespace OpenSynapse.App.Views;

internal static class MacroTriggerMenu
{
    public static void Populate(
        MenuFlyoutSubItem mouseSide,
        MenuFlyoutSubItem naga,
        MacroWorkspace macros,
        CancellationToken cancellationToken)
    {
        mouseSide.Items.Clear();
        foreach (var button in new[] { 4, 5 })
        {
            var item = new MenuFlyoutItem
            {
                Text = AppStrings.Text($"MacroMouseButton{button}"),
            };
            item.Click += async (_, _) =>
                await macros.SetMacroMouseSideButtonTriggerAsync(button, cancellationToken);
            mouseSide.Items.Add(item);
        }

        mouseSide.Items.Add(new MenuFlyoutSeparator());
        // A paddle may report a private key instead of XBUTTON1/2; capture the event.
        var other = new MenuFlyoutItem
        {
            Text = AppStrings.Text("MacroMouseSideCapture"),
        };
        other.Click += async (_, _) =>
            await macros.CaptureMacroDeviceTriggerAsync(cancellationToken);
        mouseSide.Items.Add(other);

        naga.Items.Clear();
        for (var side = 1; side <= 12; side++)
        {
            var sideButton = side;
            var item = new MenuFlyoutItem
            {
                Text = AppStrings.FormatText("MacroNagaSideButton", sideButton),
            };
            item.Click += async (_, _) =>
                await macros.SetMacroNagaSideButtonTriggerAsync(sideButton, cancellationToken);
            naga.Items.Add(item);
        }

        var canEdit = macros.CanEditMacro;
        mouseSide.IsEnabled = canEdit;
        naga.IsEnabled = canEdit;
    }
}
