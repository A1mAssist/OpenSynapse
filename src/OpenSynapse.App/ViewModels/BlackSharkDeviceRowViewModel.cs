using System.ComponentModel;
using Microsoft.UI.Xaml.Media;
using OpenSynapse.Core.Devices;
using OpenSynapse.Windows.Devices;
using Windows.UI;

namespace OpenSynapse.App.ViewModels;

public sealed class BlackSharkDeviceRowViewModel : INotifyPropertyChanged, IDeviceBatteryRow
{
    private int? _batteryPercent;
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
    public bool IsBatteryAvailable => _batteryPercent is not null;
    public string BatteryText => _batteryPercent is { } value ? $"{value}%" : string.Empty;
    public int? BatteryPercent => _batteryPercent;

    internal void SetCapabilityText(string text, int? batteryPercent = null)
    {
        CapabilityText = text;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CapabilityText)));
        if (batteryPercent is null)
        {
            return;
        }

        _batteryPercent = batteryPercent;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsBatteryAvailable)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(BatteryText)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(BatteryPercent)));
    }

    public void RefreshLocalization() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));
}
