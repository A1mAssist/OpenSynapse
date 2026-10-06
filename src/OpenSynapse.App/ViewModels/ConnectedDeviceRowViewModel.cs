using Microsoft.UI.Xaml.Media;
using OpenSynapse.Core.Devices;
using OpenSynapse.Windows.Devices;

namespace OpenSynapse.App.ViewModels;

public sealed class ConnectedDeviceRowViewModel
{
    public ConnectedDeviceRowViewModel(DeviceRowViewModel source)
    {
        Source = source;
        Name = source.Name;
        Identity = source.Identity;
        Access = source.Access;
        IconGlyph = source.IconGlyph;
        Category = source.HardwareCategory;
        IsReady = source.IsAvailable;
        IsVisible = true;
    }

    public ConnectedDeviceRowViewModel(OpenRazerDeviceRowViewModel source)
    {
        Source = source;
        Name = source.Name;
        Identity = source.Identity;
        IsReady = source.Connection.IsReady;
        Access = IsReady ? AppStrings.Text("Text_5965520A")
            : source.Connection.EndpointState == OpenRazerEndpointState.RecognizedButUnresolved
                ? AppStrings.FormatText("OpenRazerDeviceNotReady", source.Category)
                : AppStrings.Text("Text_40985721");
        IconGlyph = source.IconGlyph;
        Category = source.HardwareCategory;
        IsVisible = source.Connection.EndpointState != OpenRazerEndpointState.RecognizedButUnresolved;
    }

    public ConnectedDeviceRowViewModel(OpenRazerKrakenDeviceRowViewModel source)
    {
        Source = source;
        Name = source.Name;
        Identity = source.Identity;
        Access = source.Connection.IsReady
            ? AppStrings.Text("Text_5965520A")
            : AppStrings.Text("Text_40985721");
        IconGlyph = source.IconGlyph;
        Category = source.HardwareCategory;
        IsReady = source.Connection.IsReady;
        IsVisible = true;
    }

    public object Source { get; }
    public string Name { get; }
    public string Identity { get; }
    public string Access { get; }
    public string IconGlyph { get; }
    public DeviceCategory Category { get; }
    public bool IsReady { get; }
    public bool IsVisible { get; }
    public string Capability => Source switch
    {
        DeviceRowViewModel row => row.Capability,
        OpenRazerDeviceRowViewModel row => row.Status,
        OpenRazerKrakenDeviceRowViewModel row => row.Status,
        _ => string.Empty,
    };
    public string Error => Source switch
    {
        OpenRazerDeviceRowViewModel row => row.Error,
        OpenRazerKrakenDeviceRowViewModel row => row.Error,
        _ => string.Empty,
    };
    public Brush StatusBrush => Source switch
    {
        DeviceRowViewModel row => row.StatusBrush,
        OpenRazerDeviceRowViewModel row => row.StatusBrush,
        OpenRazerKrakenDeviceRowViewModel row => row.StatusBrush,
        _ => throw new InvalidOperationException(),
    };
    public int SortOrder => Category switch
    {
        DeviceCategory.Laptop => 0,
        DeviceCategory.Keyboard => 1,
        DeviceCategory.Mouse => 2,
        _ => 3,
    };
}
