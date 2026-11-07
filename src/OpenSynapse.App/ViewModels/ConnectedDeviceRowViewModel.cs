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
        // A keyboard is wired and still present when its control channel fails, so keep
        // the card and its error. A mouse may only be asleep behind its receiver, which
        // is the case the protocol-response gate is meant to hide.
        IsVisible = source.Connection.IsReady
            ? source.Connection.Capabilities.Count > 0
            : source.HardwareCategory == DeviceCategory.Keyboard;
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
        RazerHidKrakenDeviceRowViewModel row => row.CapabilityText,
        RazerBleDeviceRowViewModel row => row.Status,
        RazerChromaDeviceRowViewModel row => row.Status,
        RazerAwBleHeadsetRowViewModel row => row.Status,
        BlackSharkDeviceRowViewModel row => row.CapabilityText,
        RazerKeyboardDeviceRowViewModel row => row.Capability,
        RazerGamepadDeviceRowViewModel row => row.CapabilityText,
        RazerHeadsetAudioDeviceRowViewModel row => row.CapabilityText,
        RazerKiyoViewModel row => row.CapabilityText,
        RazerCoolingPadViewModel row => row.CapabilityText,
        PhilipsHueViewModel row => row.IsConfigured ? AppStrings.Text("HuePaired") : string.Empty,
        _ => string.Empty,
    };
    public string Error => Source switch
    {
        RazerHidDeviceRowViewModel row => row.Error,
        RazerHidKrakenDeviceRowViewModel row => row.Error,
        RazerKeyboardDeviceRowViewModel row => row.Error,
        RazerGamepadDeviceRowViewModel row => row.Error,
        RazerChromaDeviceRowViewModel row => row.Error,
        RazerHeadsetAudioDeviceRowViewModel row => row.Error,
        RazerKiyoViewModel row => row.ErrorText,
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
