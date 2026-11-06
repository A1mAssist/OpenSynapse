using Microsoft.UI.Xaml.Media;
using OpenSynapse.Core.Devices;
using OpenSynapse.Windows.Devices;
using Windows.UI;

namespace OpenSynapse.App.ViewModels;

public sealed class ConnectedDeviceRowViewModel
{
    private static readonly Brush ConnectedBrush =
        new SolidColorBrush(Color.FromArgb(255, 153, 221, 114));
    private static readonly Brush NeutralBrush =
        new SolidColorBrush(Color.FromArgb(255, 255, 181, 71));

    public ConnectedDeviceRowViewModel(DeviceRowViewModel source)
    {
        Source = source;
        Name = source.Name;
        Identity = source.Identity;
        Access = source.Access;
        IconGlyph = source.IconGlyph;
        Category = source.HardwareCategory;
        IsReady = source.IsAvailable ||
            (source.IsPresent && source.ProtocolFamily == DeviceProtocolFamilies.Viper);
        // Presence is enough to list the device. A failed telemetry probe changes its
        // status; it must not make the laptop or a sleeping wireless mouse disappear.
        IsVisible = source.IsPresent || DeviceProtocolFamilies.IsBlade(source.ProtocolFamily);
    }

    public ConnectedDeviceRowViewModel(RazerHidDeviceRowViewModel source)
    {
        Source = source;
        Name = source.Name;
        Identity = source.Identity;
        IsReady = source.Connection.IsReady;
        Access = IsReady ? AppStrings.Text("Text_5965520A")
            : source.Connection.EndpointState == RazerHidEndpointState.RecognizedButUnresolved
                ? AppStrings.FormatText("RazerHidDeviceNotReady", source.Category)
                : AppStrings.Text("Text_40985721");
        IconGlyph = source.IconGlyph;
        Category = source.HardwareCategory;
        // Discovery only returns a row for a Razer device that is present in Windows.
        // A failed control-channel probe must keep the card visible with its error,
        // not make the keyboard/headset disappear from the device page.
        IsVisible = true;
    }

    public ConnectedDeviceRowViewModel(RazerHidKrakenDeviceRowViewModel source)
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
        IsVisible = source.Connection.IsReady;
    }

    public ConnectedDeviceRowViewModel(RazerChromaDeviceRowViewModel source)
    {
        Source = source;
        Name = source.Name;
        Identity = source.Identity;
        Access = AppStrings.Text("Text_5965520A");
        IconGlyph = source.IconGlyph;
        Category = source.HardwareCategory;
        IsReady = true;
        IsVisible = true;
    }

    public ConnectedDeviceRowViewModel(RazerBleDeviceRowViewModel source)
    {
        Source = source;
        Name = source.Name;
        Identity = source.Identity;
        Access = AppStrings.Text("Text_5965520A");
        IconGlyph = source.IconGlyph;
        Category = source.HardwareCategory;
        IsReady = true;
        IsVisible = true;
    }
    public ConnectedDeviceRowViewModel(RazerAwBleHeadsetRowViewModel source)
    {
        Source = source;
        Name = source.Name;
        Identity = source.Identity;
        Access = AppStrings.Text("Text_5965520A");
        IconGlyph = source.IconGlyph;
        Category = source.HardwareCategory;
        IsReady = true;
        IsVisible = true;
    }

    public ConnectedDeviceRowViewModel(RazerKeyboardDeviceRowViewModel source)
    {
        Source = source;
        Name = source.Name;
        Identity = source.Identity;
        Access = AppStrings.Text("Text_5965520A");
        IconGlyph = source.IconGlyph;
        Category = source.HardwareCategory;
        IsReady = true;
        IsVisible = true;
    }

    public ConnectedDeviceRowViewModel(RazerGamepadDeviceRowViewModel source)
    {
        Source = source;
        Name = source.Name;
        Identity = source.Identity;
        Access = AppStrings.Text("Text_5965520A");
        IconGlyph = source.IconGlyph;
        Category = source.HardwareCategory;
        IsReady = true;
        IsVisible = true;
    }

    public ConnectedDeviceRowViewModel(RazerCoolingPadViewModel source)
    {
        Source = source;
        Name = source.Name;
        Identity = source.Identity;
        Access = AppStrings.Text("Text_5965520A");
        IconGlyph = source.IconGlyph;
        Category = DeviceCategory.Accessory;
        IsReady = source.IsConnected;
        IsVisible = source.IsConnected;
    }

    public ConnectedDeviceRowViewModel(RazerKiyoViewModel source)
    {
        Source = source;
        Name = source.Name;
        Identity = source.Identity;
        Access = AppStrings.Text("Text_5965520A");
        IconGlyph = source.IconGlyph;
        Category = source.HardwareCategory;
        IsReady = source.IsConnected;
        IsVisible = source.IsConnected;
    }

    public ConnectedDeviceRowViewModel(RazerHeadsetAudioDeviceRowViewModel source)
    {
        Source = source;
        Name = source.Name;
        Identity = source.Identity;
        Access = AppStrings.Text("Text_5965520A");
        IconGlyph = source.IconGlyph;
        Category = source.HardwareCategory;
        IsReady = true;
        IsVisible = true;
    }

    public ConnectedDeviceRowViewModel(BlackSharkDeviceRowViewModel source)
    {
        Source = source;
        Name = source.Name;
        Identity = source.Identity;
        Access = AppStrings.Text("Text_5965520A");
        IconGlyph = source.IconGlyph;
        Category = source.HardwareCategory;
        IsReady = true;
        IsVisible = true;
    }

    public ConnectedDeviceRowViewModel(PhilipsHueViewModel source)
    {
        Source = source;
        Name = AppStrings.Text("HueTitle");
        Identity = source.DeviceIdentity;
        Access = source.DeviceStatusText;
        IconGlyph = "\uE706";
        Category = DeviceCategory.Accessory;
        IsReady = source.IsReady;
        // An unpaired bridge is an onboarding candidate, not a device in the list.
        IsVisible = source.IsConfigured;
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
        RazerHidDeviceRowViewModel row => row.Status,
        RazerHidKrakenDeviceRowViewModel row => row.Status,
        RazerBleDeviceRowViewModel row => row.Status,
        BlackSharkDeviceRowViewModel row => row.Status,
        RazerGamepadDeviceRowViewModel row => row.Status,
        RazerCoolingPadViewModel row => row.CapabilityText,
        PhilipsHueViewModel row => row.IsConfigured ? AppStrings.Text("HuePaired") : string.Empty,
        _ => string.Empty,
    };
    public string Error => Source switch
    {
        RazerHidDeviceRowViewModel row => row.Error,
        RazerHidKrakenDeviceRowViewModel row => row.Error,
        BlackSharkDeviceRowViewModel row => row.Error,
        RazerCoolingPadViewModel row => row.ErrorText,
        PhilipsHueViewModel row => row.ErrorText,
        _ => string.Empty,
    };
    public Brush StatusBrush => Source switch
    {
        DeviceRowViewModel row => row.StatusBrush,
        RazerHidDeviceRowViewModel row => row.StatusBrush,
        RazerHidKrakenDeviceRowViewModel row => row.StatusBrush,
        RazerBleDeviceRowViewModel row => row.StatusBrush,
        RazerAwBleHeadsetRowViewModel => ConnectedBrush,
        BlackSharkDeviceRowViewModel row => row.StatusBrush,
        RazerKeyboardDeviceRowViewModel row => row.StatusBrush,
        RazerGamepadDeviceRowViewModel row => row.StatusBrush,
        RazerHeadsetAudioDeviceRowViewModel row => row.StatusBrush,
        RazerKiyoViewModel => ConnectedBrush,
        RazerChromaDeviceRowViewModel row => row.StatusBrush,
        RazerCoolingPadViewModel row => row.StatusBrush,
        PhilipsHueViewModel row => row.StatusBrush,
        _ => NeutralBrush,
    };
    public int SortOrder => Category switch
    {
        DeviceCategory.Laptop => 0,
        DeviceCategory.Keyboard => 1,
        DeviceCategory.Mouse => 2,
        _ => 3,
    };
}
