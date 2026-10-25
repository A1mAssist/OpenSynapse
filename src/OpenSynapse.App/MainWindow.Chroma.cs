using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using OpenSynapse.App.ViewModels;

namespace OpenSynapse.App;

public sealed partial class MainWindow
{
    private async void ChromaSelectorClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: RazerChromaDeviceRowViewModel row })
        {
            return;
        }

        SelectDevice("chroma");
        await _viewModel.SelectChromaAsync(row, _lifetime.Token);
        UpdateDeviceSelector("chroma");
    }

    private async void ChromaRefreshClick(object sender, RoutedEventArgs e) =>
        await (_viewModel.SelectedChroma?.LoadAsync(_lifetime.Token) ?? Task.CompletedTask);

    private async void ChromaApplyBrightnessClick(object sender, RoutedEventArgs e) =>
        await (_viewModel.SelectedChroma?.ApplyBrightnessAsync(_lifetime.Token) ?? Task.CompletedTask);

    private async void ChromaApplyEffectClick(object sender, RoutedEventArgs e) =>
        await (_viewModel.SelectedChroma?.ApplyEffectAsync(_lifetime.Token) ?? Task.CompletedTask);
}
