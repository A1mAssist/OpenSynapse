using System.ComponentModel;
using Microsoft.UI.Xaml.Media;
using OpenSynapse.Core.Devices;
using OpenSynapse.Windows.Devices;
using Windows.UI;

namespace OpenSynapse.App.ViewModels;

public sealed class RazerGamepadDeviceRowViewModel : INotifyPropertyChanged
{
    public RazerGamepadDeviceRowViewModel(RazerGamepadConnection connection)
    {
        Connection = connection;
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public RazerGamepadConnection Connection { get; }
    public DeviceCategory HardwareCategory => DeviceCategory.Gamepad;
    public string Name => Connection.Name;
    public string Category => AppStrings.Text("RazerGamepadCategory");
    public string Identity => $"VID_1532 / PID_{Connection.ProductId:X4}";
    public string IconGlyph => "\uE7FC";
    public string Status => AppStrings.Text("BlackSharkStatusConnected");
    public string CapabilityText { get; private set; } = AppStrings.Text("BlackSharkStatusConnected");
    public Brush StatusBrush { get; } = new SolidColorBrush(Color.FromArgb(255, 68, 214, 44));
    public string Error => string.Empty;

    internal void SetCapabilityText(string text)
    {
        CapabilityText = text;
        PropertyChanged?.Invoke(this, new(nameof(CapabilityText)));
    }

    public void RefreshLocalization() => PropertyChanged?.Invoke(this, new(string.Empty));
}
