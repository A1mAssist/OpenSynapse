using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using OpenSynapse.App.ViewModels;

namespace OpenSynapse.App;

public sealed partial class MainWindow
{
    private async void CreateMacroClick(object sender, RoutedEventArgs e) =>
        await _viewModel.CreateMacroAsync(_lifetime.Token);

    private async void DeleteMacroRowClick(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuFlyoutItem { DataContext: MacroRowViewModel row })
        {
            return;
        }

        if (!ReferenceEquals(_viewModel.SelectedMacro, row))
        {
            _viewModel.SelectedMacro = row;
        }

        await _viewModel.DeleteSelectedMacroAsync(_lifetime.Token);
    }

    private async void CaptureMacroTriggerClick(object sender, RoutedEventArgs e) =>
        await _viewModel.CaptureMacroTriggerAsync(_lifetime.Token);
}
