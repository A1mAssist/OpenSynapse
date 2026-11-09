using System.ComponentModel;
using Microsoft.UI.Xaml.Media;
using OpenSynapse.Core.Devices;
using OpenSynapse.Windows.Devices;
using Windows.UI;

namespace OpenSynapse.App.ViewModels;

public sealed class BlackSharkDeviceRowViewModel : INotifyPropertyChanged
{
    public BlackSharkDeviceRowViewModel(BlackSharkHeadsetConnection connection)
    {
        Connection = connection;
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public BlackSharkHeadsetConnection Connection { get; }
    public DeviceCategory HardwareCategory => DeviceCategory.Headset;
    public string Name => Connection.Name;
    public string Category => AppStrings.Text("BlackSharkHeadsetCategory");
    public string Identity => $"VID_1532 / PID_{Connection.ProductId:X4}";
    public string IconGlyph => "\uE7F6";
    public string Status => AppStrings.Text("BlackSharkStatusConnected");
    public string CapabilityText { get; private set; } = AppStrings.Text("BlackSharkStatusConnected");
    public Brush StatusBrush { get; } = new SolidColorBrush(Color.FromArgb(255, 68, 214, 44));
    public string Error => string.Empty;

    internal void SetCapabilityText(string text)
    {
        CapabilityText = text;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CapabilityText)));
    }

    public void RefreshLocalization() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));
}
