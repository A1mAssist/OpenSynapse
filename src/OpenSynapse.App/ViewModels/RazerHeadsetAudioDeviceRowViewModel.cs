using System.ComponentModel;
using Microsoft.UI.Xaml.Media;
using OpenSynapse.Core.Devices;
using OpenSynapse.Windows.Devices;
using Windows.UI;

namespace OpenSynapse.App.ViewModels;

public sealed class RazerHeadsetAudioDeviceRowViewModel : INotifyPropertyChanged
{
    public RazerHeadsetAudioDeviceRowViewModel(RazerHeadsetAudioConnection connection)
    {
        Connection = connection;
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public RazerHeadsetAudioConnection Connection { get; }
    public DeviceCategory HardwareCategory => RazerHeadsetAudioService.GetCategory(Connection.ProductId);
    public string Name => Connection.Name;
    public string Category => AppStrings.Text(HardwareCategory switch
    {
        DeviceCategory.Speaker => "RazerSpeakerCategory",
        DeviceCategory.Microphone => "RazerMicrophoneCategory",
        _ => "BlackSharkHeadsetCategory",
    });
    public string Identity => $"VID_1532 / PID_{Connection.ProductId:X4}";
    public string IconGlyph => "\uE7F6";
    public string Status => AppStrings.Text("BlackSharkStatusConnected");
    public Brush StatusBrush { get; } = new SolidColorBrush(Color.FromArgb(255, 153, 221, 114));
    public string Error => string.Empty;

    public void RefreshLocalization() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));
}
