using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using OpenSynapse.App.ViewModels;

namespace OpenSynapse.App.Views;

public sealed partial class GamepadRemapEditor : UserControl
{
    public GamepadRemapEditor() => InitializeComponent();

    private void WasdPresetClick(object sender, RoutedEventArgs e) =>
        (DataContext as RazerHidDeviceViewModel)?.ApplyAnalogJoystickWasdPreset();

    private void TriggerPresetClick(object sender, RoutedEventArgs e) =>
        (DataContext as RazerHidDeviceViewModel)?.ApplyAnalogJoystickTriggerPreset();

    private void ResetPresetClick(object sender, RoutedEventArgs e) =>
        (DataContext as RazerHidDeviceViewModel)?.ResetAnalogJoystickEdits();

    private async void ApplyClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is not RazerHidDeviceViewModel viewModel)
        {
            return;
        }

        await viewModel.ApplyAnalogJoystickAsync();
    }
}
