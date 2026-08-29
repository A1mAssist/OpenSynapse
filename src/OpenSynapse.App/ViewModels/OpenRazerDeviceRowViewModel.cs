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
        DeviceCategory.Mouse => AppStrings.Text("Text_4B32CEE8"),
        DeviceCategory.Laptop => AppStrings.Text("Text_66E7127F"),
        DeviceCategory.Keyboard => AppStrings.Text("Text_7D4E2D8B"),
        DeviceCategory.MouseMat => AppStrings.Text("Text_A3A74479"),
        DeviceCategory.Monitor => AppStrings.Text("Text_2E486BCB"),
        DeviceCategory.Accessory => AppStrings.Text("Text_71E692AA"),
        _ => AppStrings.Text("Text_CAF15352"),
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
        OpenRazerEndpointState.Resolved => AppStrings.Text("Text_C097B416"),
        OpenRazerEndpointState.RecognizedButUnresolved => AppStrings.Text("Text_242E08F4"),
        _ => AppStrings.Text("Text_D3632B96"),
    };
    public Brush StatusBrush { get; }
    public string Error => _connection.Error ?? string.Empty;
    public bool HasError => !string.IsNullOrWhiteSpace(Error);

    public void RefreshLocalization() => PropertyChanged?.Invoke(this, new(string.Empty));
}
