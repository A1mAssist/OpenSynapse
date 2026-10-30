using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using OpenSynapse.App.ViewModels;

namespace OpenSynapse.App.Views;

public sealed partial class DevicesPage : UserControl
{
    private async void GamepadSelectorClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: RazerGamepadDeviceRowViewModel row }) return;
        SelectDevice("gamepad");
        await ViewModel.SelectGamepadAsync(row, Lifetime);
        UpdateDeviceSelector("gamepad");
    }

    private async void GamepadRefreshClick(object sender, RoutedEventArgs e)
    {
        await (ViewModel.SelectedGamepad?.LoadAsync(Lifetime) ?? Task.CompletedTask);
    }

    private async void GamepadRefreshAssignmentsClick(object sender, RoutedEventArgs e)
    {
        await (ViewModel.SelectedGamepad?.RefreshButtonAssignmentsAsync(Lifetime) ?? Task.CompletedTask);
    }

    private async void GamepadApplyPollingProfileClick(object sender, RoutedEventArgs e)
    {
        await (ViewModel.SelectedGamepad?.ApplyPollingProfileAsync(Lifetime) ?? Task.CompletedTask);
    }

    private async void GamepadApplyButtonAssignmentClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: RazerGamepadButtonAssignmentRowViewModel row })
        {
            await (ViewModel.SelectedGamepad?.ApplyButtonAssignmentAsync(row, Lifetime) ?? Task.CompletedTask);
        }
    }

    private async void GamepadApplyStickClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: RazerGamepadStickRowViewModel row })
        {
            await (ViewModel.SelectedGamepad?.ApplyStickAsync(row, Lifetime) ?? Task.CompletedTask);
        }
    }

    private async void GamepadApplyTriggerClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: RazerGamepadTriggerRowViewModel row })
        {
            await (ViewModel.SelectedGamepad?.ApplyTriggerAsync(row, Lifetime) ?? Task.CompletedTask);
        }
    }

    private async void GamepadApplyDpadClick(object sender, RoutedEventArgs e)
    {
        await (ViewModel.SelectedGamepad?.ApplyDpadAsync(Lifetime) ?? Task.CompletedTask);
    }

    private async void GamepadApplyVibrationClick(object sender, RoutedEventArgs e)
    {
        await (ViewModel.SelectedGamepad?.ApplyVibrationAsync(Lifetime) ?? Task.CompletedTask);
    }
}
