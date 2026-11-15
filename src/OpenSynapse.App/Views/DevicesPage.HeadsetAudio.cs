using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using OpenSynapse.App.ViewModels;

namespace OpenSynapse.App.Views;

public sealed partial class DevicesPage : UserControl
{
    private async void HeadsetAudioSelectorClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: RazerHeadsetAudioDeviceRowViewModel row })
        {
            return;
        }

        SelectDevice("headsetaudio");
        await ViewModel.Accessories.SelectHeadsetAudioAsync(row, Lifetime);
        UpdateDeviceSelector("headsetaudio");
    }
}
