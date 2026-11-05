using Microsoft.UI.Xaml;

namespace OpenSynapse.App.Views;

public sealed partial class SettingsPage
{
    private void HueSettingsClick(object sender, RoutedEventArgs e) =>
        Host.RequestDeviceNavigation(ViewModel.Hue);
}
