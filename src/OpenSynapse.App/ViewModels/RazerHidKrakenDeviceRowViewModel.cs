using System.ComponentModel;
using Microsoft.UI.Xaml.Media;
using OpenSynapse.Core.Devices;
using OpenSynapse.Windows.Devices;
using Windows.UI;

namespace OpenSynapse.App.ViewModels;

public sealed class RazerHidKrakenDeviceRowViewModel : INotifyPropertyChanged
{
    public RazerHidKrakenDeviceRowViewModel(RazerHidSpecialLightingConnection connection)
    {
        Connection = connection;
        StatusBrush = new SolidColorBrush(connection.IsReady
            ? Color.FromArgb(255, 68, 214, 44)
            : Color.FromArgb(255, 240, 185, 90));
    }

    public RazerHidSpecialLightingConnection Connection { get; }
    public DeviceCategory HardwareCategory => DeviceCategory.Headset;
    public event PropertyChangedEventHandler? PropertyChanged;
    public Brush SelectorBackground { get; private set; } = new SolidColorBrush(Color.FromArgb(0, 0, 0, 0));
    public string Name => Connection.DisplayName;
    public string Category => AppStrings.Text("RazerHidHeadsetCategory");
    public string Identity => $"VID_1532 / PID_{Connection.ProductId:X4}";
    public string IconGlyph => "\uE95B";
    public string Status => Connection.IsReady
        ? AppStrings.Text("Text_C097B416")
        : AppStrings.Text("Text_242E08F4");
    public int ProtocolTotalCount => 2;
    public int ProtocolAvailableCount =>
        (Connection.SupportedEffects.Count > 0 ? 1 : 0) +
        (Connection.SupportsStaticColor ? 1 : 0);
    public string CapabilityText =>
        AppStrings.FormatText("ProtocolAvailableCount", ProtocolAvailableCount, ProtocolTotalCount);
    public Brush StatusBrush { get; }
    public string Error => string.IsNullOrWhiteSpace(Connection.Error)
        ? string.Empty
        : AppStrings.Text("RazerHidProtocolRescanRequired");
    public void SetSelectorBackground(Brush background)
    {
        SelectorBackground = background;
        PropertyChanged?.Invoke(this, new(nameof(SelectorBackground)));
    }

    public void RefreshLocalization() => PropertyChanged?.Invoke(this, new(string.Empty));
}
