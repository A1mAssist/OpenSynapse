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

    private async void ChromaApplyEffectClick(object sender, RoutedEventArgs e)
    {
        await SuspendStudioLightingAsync();
        await (ViewModel.Accessories.SelectedChroma?.ApplyEffectAsync(Lifetime) ?? Task.CompletedTask);
    }

    /// <summary>
    /// A class-0x10 device (Basilisk V4 Pro and friends) owns no native lighting
    /// zone, so a Chroma card would be a second entry for the same mouse. Its
    /// lighting card rides under the native page instead.
    /// </summary>
    private async Task SyncChromaLightingAsync()
    {
        var device = ViewModel.RazerHid.SelectedDevice;
        var twin = device is { LightingZones.Count: 0 }
            ? ViewModel.Accessories.ChromaDevices.FirstOrDefault(row =>
                row.Connection.ProductId == device.Connection.Definition.ProductId)
            : null;
        if (twin is null) return;

        if (!StringComparer.OrdinalIgnoreCase.Equals(
                ViewModel.Accessories.SelectedChroma?.DevicePath, twin.Connection.DevicePath))
        {
            await ViewModel.Accessories.SelectChromaAsync(twin, Lifetime);
        }

        ChromaHeaderGrid.Visibility = Visibility.Collapsed;
        ChromaPanel.Visibility = Visibility.Visible;
    }


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

    private async void WirelessArgbApplyClick(object sender, RoutedEventArgs e) =>
        await (ViewModel.Accessories.SelectedChroma?.ApplyWirelessArgbAsync(Lifetime) ?? Task.CompletedTask);

    private async void ArgbInstallDriverClick(object sender, RoutedEventArgs e)
    {
        if (ViewModel.Accessories.SelectedChroma is { ArgbDriverUri: { Length: > 0 } uri })
        {
            await Launcher.LaunchUriAsync(new Uri(uri));
        }
    }
}

