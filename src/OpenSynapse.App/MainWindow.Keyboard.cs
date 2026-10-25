using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using OpenSynapse.App.ViewModels;

namespace OpenSynapse.App;

public sealed partial class MainWindow
{
    private async void KeyboardSelectorClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: RazerKeyboardDeviceRowViewModel row })
        {
            return;
        }

        SelectDevice("keyboard");
        await _viewModel.SelectKeyboardAsync(row, _lifetime.Token);
        UpdateDeviceSelector("keyboard");
    }

    private async void KeyboardRefreshClick(object sender, RoutedEventArgs e) =>
        await (_viewModel.SelectedKeyboard?.LoadAsync(_lifetime.Token) ?? Task.CompletedTask);

    private async void KeyboardApplyGameModeClick(object sender, RoutedEventArgs e) =>
        await (_viewModel.SelectedKeyboard?.ApplyGameModeAsync(_lifetime.Token) ?? Task.CompletedTask);

    private async void KeyboardApplyPollingClick(object sender, RoutedEventArgs e) =>
        await (_viewModel.SelectedKeyboard?.ApplyPollingAsync(_lifetime.Token) ?? Task.CompletedTask);
}
