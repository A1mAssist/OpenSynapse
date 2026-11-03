using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using OpenSynapse.App.ViewModels;
using Windows.System;

namespace OpenSynapse.App.Views;

public sealed partial class DevicesPage : UserControl
{
    private async void ChromaSelectorClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: RazerChromaDeviceRowViewModel row })
        {
            return;
        }

        SelectDevice("chroma");
        await ViewModel.Accessories.SelectChromaAsync(row, Lifetime);
        UpdateDeviceSelector("chroma");
    }

    private async void ChromaRefreshClick(object sender, RoutedEventArgs e) =>
        await (ViewModel.Accessories.SelectedChroma?.LoadAsync(Lifetime) ?? Task.CompletedTask);

    private async void ChromaApplyBrightnessClick(object sender, RoutedEventArgs e) =>
        await (ViewModel.Accessories.SelectedChroma?.ApplyBrightnessAsync(Lifetime) ?? Task.CompletedTask);

    private async void ChromaApplyEffectClick(object sender, RoutedEventArgs e) =>
        await (ViewModel.Accessories.SelectedChroma?.ApplyEffectAsync(Lifetime) ?? Task.CompletedTask);


    private void ArgbLedClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: ArgbLedViewModel led }) return;
        ViewModel.Accessories.SelectedChroma?.PaintArgbLed(led);
    }

    private void ArgbFillClick(object sender, RoutedEventArgs e) =>
        ViewModel.Accessories.SelectedChroma?.FillArgbLeds();

    private void ArgbClearClick(object sender, RoutedEventArgs e) =>
        ViewModel.Accessories.SelectedChroma?.ClearArgbLeds();

    private async void ArgbApplyClick(object sender, RoutedEventArgs e) =>
        await (ViewModel.Accessories.SelectedChroma?.ApplyArgbFrameAsync(Lifetime) ?? Task.CompletedTask);

    private async void ArgbInstallDriverClick(object sender, RoutedEventArgs e)
    {
        if (ViewModel.Accessories.SelectedChroma is { ArgbDriverUri: { Length: > 0 } uri })
        {
            await Launcher.LaunchUriAsync(new Uri(uri));
        }
    }
}

