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
        await ViewModel.Accessories.SelectGamepadAsync(row, Lifetime);
        UpdateDeviceSelector("gamepad");
    }

    private async void GamepadRefreshClick(object sender, RoutedEventArgs e)
    {
        await (ViewModel.Accessories.SelectedGamepad?.LoadAsync(Lifetime) ?? Task.CompletedTask);
    }

    private async void GamepadRefreshAssignmentsClick(object sender, RoutedEventArgs e)
    {
        await (ViewModel.Accessories.SelectedGamepad?.RefreshButtonAssignmentsAsync(Lifetime) ?? Task.CompletedTask);
    }

    private async void GamepadApplyPollingProfileClick(object sender, RoutedEventArgs e)
    {
        await (ViewModel.Accessories.SelectedGamepad?.ApplyPollingProfileAsync(Lifetime) ?? Task.CompletedTask);
    }

    private async void GamepadProfileSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        await (ViewModel.Accessories.SelectedGamepad?.LoadPollingForSelectedProfileAsync(Lifetime) ?? Task.CompletedTask);
    }

    private async void GamepadApplyLightingClick(object sender, RoutedEventArgs e)
    {
        await (ViewModel.Accessories.SelectedGamepad?.ApplyLightingAsync(Lifetime) ?? Task.CompletedTask);
    }

    private async void GamepadApplyButtonAssignmentClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: RazerGamepadButtonAssignmentRowViewModel row })
        {
            await (ViewModel.Accessories.SelectedGamepad?.ApplyButtonAssignmentAsync(row, Lifetime) ?? Task.CompletedTask);
        }
    }

    private async void GamepadApplyStickClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: RazerGamepadStickRowViewModel row })
        {
            await (ViewModel.Accessories.SelectedGamepad?.ApplyStickAsync(row, Lifetime) ?? Task.CompletedTask);
        }
    }

    private async void GamepadApplyTriggerClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: RazerGamepadTriggerRowViewModel row })
        {
            await (ViewModel.Accessories.SelectedGamepad?.ApplyTriggerAsync(row, Lifetime) ?? Task.CompletedTask);
        }
    }

    private async void GamepadApplyDpadClick(object sender, RoutedEventArgs e)
    {
        await (ViewModel.Accessories.SelectedGamepad?.ApplyDpadAsync(Lifetime) ?? Task.CompletedTask);
    }

    private async void GamepadApplyVibrationClick(object sender, RoutedEventArgs e)
    {
        await (ViewModel.Accessories.SelectedGamepad?.ApplyVibrationAsync(Lifetime) ?? Task.CompletedTask);
    }
}
