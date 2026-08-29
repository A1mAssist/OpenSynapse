using System.ComponentModel;
using Microsoft.UI.Xaml.Media;
using OpenSynapse.Windows.Devices;
using Windows.UI;

namespace OpenSynapse.App.ViewModels;

public sealed class OpenRazerKrakenDeviceRowViewModel : INotifyPropertyChanged
{
    public OpenRazerKrakenDeviceRowViewModel(OpenRazerSpecialLightingConnection connection)
    {
        Connection = connection;
        StatusBrush = new SolidColorBrush(connection.IsReady
            ? Color.FromArgb(255, 153, 221, 114)
            : Color.FromArgb(255, 240, 185, 90));
    }

    public OpenRazerSpecialLightingConnection Connection { get; }
    public event PropertyChangedEventHandler? PropertyChanged;
    public string Name => Connection.DisplayName;
    public string Category => AppStrings.Text("OpenRazerHeadsetCategory");
    public string Identity => $"VID_1532 / PID_{Connection.ProductId:X4}";
    public string IconGlyph => "\uE95B";
    public string Status => Connection.IsReady
        ? AppStrings.Text("Text_C097B416")
        : AppStrings.Text("Text_242E08F4");
    public Brush StatusBrush { get; }
    public string Error => Connection.Error ?? string.Empty;
    public void RefreshLocalization() => PropertyChanged?.Invoke(this, new(string.Empty));
}
