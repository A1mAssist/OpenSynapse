using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace OpenSynapse.App;

public sealed partial class MacroEditorWindow
{
    private void MacroTriggerFlyoutOpening(object sender, object e)
    {
        MacroClearTriggerItem.IsEnabled =
            _viewModel.Macros.CanEditMacro &&
            _viewModel.Macros.SelectedMacro?.Definition.Trigger is not null;
        MacroMouseSideTriggerMenu.IsEnabled = _viewModel.Macros.CanEditMacro;
        if (MacroMouseSideTriggerMenu.Items.Count == 0)
        {
            for (var mouseButton = 4; mouseButton <= 5; mouseButton++)
            {
                var item = new MenuFlyoutItem
                {
                    Text = AppStrings.Text($"MacroMouseButton{mouseButton}"),
                    Tag = mouseButton,
                };
                item.Click += MouseSideButtonTriggerClick;
                MacroMouseSideTriggerMenu.Items.Add(item);
            }
        }

        MacroNagaTriggerMenu.IsEnabled = _viewModel.Macros.CanEditMacro;
        if (MacroNagaTriggerMenu.Items.Count == 0)
        {
            for (var sideButton = 1; sideButton <= 12; sideButton++)
            {
                var item = new MenuFlyoutItem
                {
                    Text = AppStrings.FormatText("MacroNagaSideButton", sideButton),
                    Tag = sideButton,
                };
                item.Click += NagaSideButtonTriggerClick;
                MacroNagaTriggerMenu.Items.Add(item);
            }
        }
    }

    private async void MouseSideButtonTriggerClick(object sender, RoutedEventArgs e)
    {
        if (sender is MenuFlyoutItem { Tag: int mouseButton })
        {
            await _viewModel.Macros.SetMacroMouseSideButtonTriggerAsync(
                mouseButton,
                _lifetime.Token);
        }
    }

    private async void NagaSideButtonTriggerClick(object sender, RoutedEventArgs e)
    {
        if (sender is MenuFlyoutItem { Tag: int sideButton })
        {
            await _viewModel.Macros.SetMacroNagaSideButtonTriggerAsync(
                sideButton,
                _lifetime.Token);
        }
    }
}
