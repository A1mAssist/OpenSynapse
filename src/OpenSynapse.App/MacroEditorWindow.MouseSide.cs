using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using OpenSynapse.Core.Devices;
using OpenSynapse.Windows.Input;

namespace OpenSynapse.App;

public sealed partial class MacroEditorWindow
{
    private void MacroTriggerFlyoutOpening(object sender, object e)
    {
        MacroClearTriggerItem.IsEnabled =
            _viewModel.Macros.CanEditMacro &&
            _viewModel.Macros.SelectedMacro?.Definition.Trigger is not null;
        MacroMouseSideTriggerMenu.IsEnabled = _viewModel.Macros.CanEditMacro;
        RebuildMouseSideTriggerMenu();

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

    private void RebuildMouseSideTriggerMenu()
    {
        MacroMouseSideTriggerMenu.Items.Clear();
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

        var supportsThirdSideButton = false;
        foreach (var device in _viewModel.Devices)
        {
            if (device.HardwareCategory == DeviceCategory.Mouse &&
                RazerPrivateKeyCatalog.SupportsThirdSideButton(device.ProductId))
            {
                supportsThirdSideButton = true;
                break;
            }
        }

        if (!supportsThirdSideButton)
        {
            return;
        }

        // The third side button is a Razer private key (DKM_FN), not Mouse 6.
        // Capture it so the saved trigger stays device-scoped.
        MacroMouseSideTriggerMenu.Items.Add(new MenuFlyoutSeparator());
        var thirdSideButton = new MenuFlyoutItem
        {
            Text = AppStrings.Text("MacroMouseSide3Capture"),
        };
        thirdSideButton.Click += CaptureMacroMouseSide3TriggerClick;
        MacroMouseSideTriggerMenu.Items.Add(thirdSideButton);
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

    private async void CaptureMacroMouseSide3TriggerClick(object sender, RoutedEventArgs e) =>
        await _viewModel.Macros.CaptureMacroDeviceTriggerAsync(_lifetime.Token);

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
