using System.ComponentModel;
using Microsoft.UI.Xaml.Media;
using OpenSynapse.Windows.Devices;
using OpenSynapse.Core.Devices;
using Windows.UI;

namespace OpenSynapse.App.ViewModels;

public sealed class OpenRazerDeviceRowViewModel : INotifyPropertyChanged
{
    private readonly OpenRazerDeviceConnection _connection;

    public OpenRazerDeviceRowViewModel(OpenRazerDeviceConnection connection)
    {
        _connection = connection;
        StatusBrush = new SolidColorBrush(connection.EndpointState == OpenRazerEndpointState.Resolved
            ? Color.FromArgb(255, 153, 221, 114)
            : Color.FromArgb(255, 240, 185, 90));
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public OpenRazerDeviceConnection Connection => _connection;
    public string Name => _connection.Definition.DisplayName;
    public string Category => _connection.Definition.Category switch
    {
        DeviceCategory.Mouse => AppStrings.Get("鼠标"),
        DeviceCategory.Laptop => AppStrings.Get("笔记本"),
        DeviceCategory.Keyboard => AppStrings.Get("键盘"),
        DeviceCategory.MouseMat => AppStrings.Get("鼠标垫"),
        DeviceCategory.Monitor => AppStrings.Get("显示器"),
        DeviceCategory.Accessory => AppStrings.Get("配件"),
        _ => AppStrings.Get("设备"),
    };
    public string Identity => $"VID_1532 / PID_{_connection.Definition.ProductId:X4}";
    public string IconGlyph => _connection.Definition.Category switch
    {
        DeviceCategory.Laptop => "\uE7F8",
        DeviceCategory.Mouse => "\uE962",
        DeviceCategory.Keyboard => "\uE9D3",
        DeviceCategory.MouseMat => "\uE7F4",
        DeviceCategory.Monitor => "\uE7F4",
        _ => "\uE772",
    };
    public string Status => _connection.EndpointState switch
    {
        OpenRazerEndpointState.Resolved => AppStrings.Get("已解析"),
        OpenRazerEndpointState.RecognizedButUnresolved => AppStrings.Get("控制通道未解析"),
        _ => AppStrings.Get("忙或不可用"),
    };
    public Brush StatusBrush { get; }
    public string Error => _connection.Error ?? string.Empty;
    public bool HasError => !string.IsNullOrWhiteSpace(Error);

    public void RefreshLocalization() => PropertyChanged?.Invoke(this, new(string.Empty));
}
