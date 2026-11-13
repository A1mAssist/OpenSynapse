using System.ComponentModel;
using System.Globalization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using OpenSynapse.Core.Devices;
using OpenSynapse.Windows.Devices;
using Windows.UI;

namespace OpenSynapse.App.ViewModels;

public sealed class ConnectedDeviceRowViewModel : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    private static readonly Brush ConnectedBrush =
        new SolidColorBrush(Color.FromArgb(255, 68, 214, 44));
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
        IsReady = string.IsNullOrEmpty(source.Error);
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
    /// <summary>
    /// The live row behind this snapshot. Cards bind through it so a poll result repaints
    /// the battery without rebuilding the list.
    /// </summary>
    public IDeviceBatteryRow BatteryRow => Source as IDeviceBatteryRow ?? NoBatteryRow.Instance;
    public string Name { get; }
    public string Identity { get; }
    public string Access { get; }
    public string IconGlyph { get; }
    /// <summary>Chroma product art reuses the Studio image cache, so no extra download.</summary>
    public ushort ProductId => ParseProductId(Identity);
    public ImageSource? ProductImage { get; private set; }
    public Visibility IconVisibility => ProductImage is null ? Visibility.Visible : Visibility.Collapsed;

    internal void SetProductImage(ImageSource image)
    {
        ProductImage = image;
        PropertyChanged?.Invoke(this, new(nameof(ProductImage)));
        PropertyChanged?.Invoke(this, new(nameof(IconVisibility)));
    }

    private static ushort ParseProductId(string identity)
    {
        var marker = identity.LastIndexOf("PID_", StringComparison.OrdinalIgnoreCase);
        return marker >= 0 && marker + 8 <= identity.Length &&
            ushort.TryParse(
                identity.AsSpan(marker + 4, 4),
                NumberStyles.HexNumber,
                CultureInfo.InvariantCulture,
                out var productId)
            ? productId
            : (ushort)0;
    }

    public DeviceCategory Category { get; }
    public bool IsReady { get; }
    public bool IsVisible { get; }
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
    public bool HasError => !string.IsNullOrWhiteSpace(Error);
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

    private sealed class NoBatteryRow : IDeviceBatteryRow
    {
        public static NoBatteryRow Instance { get; } = new();
        public bool IsBatteryAvailable => false;
        public string BatteryText => string.Empty;
        public int? BatteryPercent => null;
    }
}
