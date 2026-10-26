using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using OpenSynapse.App.ViewModels;
using Windows.System;

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


    private void ArgbLedClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: ArgbLedViewModel led }) return;
        _viewModel.SelectedChroma?.PaintArgbLed(led);
    }

    private void ArgbFillClick(object sender, RoutedEventArgs e) =>
        _viewModel.SelectedChroma?.FillArgbLeds();

    private void ArgbClearClick(object sender, RoutedEventArgs e) =>
        _viewModel.SelectedChroma?.ClearArgbLeds();

    private async void ArgbApplyClick(object sender, RoutedEventArgs e) =>
        await (_viewModel.SelectedChroma?.ApplyArgbFrameAsync(_lifetime.Token) ?? Task.CompletedTask);

    private async void ArgbInstallDriverClick(object sender, RoutedEventArgs e)
    {
        if (_viewModel.SelectedChroma is { ArgbDriverUri: { Length: > 0 } uri })
        {
            await Launcher.LaunchUriAsync(new Uri(uri));
        }
    }
}

