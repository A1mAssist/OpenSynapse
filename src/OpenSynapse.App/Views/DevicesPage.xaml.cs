using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using OpenSynapse.App.ViewModels;
using Windows.UI;

namespace OpenSynapse.App.Views;

public sealed partial class DevicesPage : UserControl
{
    internal MainWindow Host { get; set; } = null!;

    internal bool IsBladePanelVisible => BladeDevicePanel.Visibility == Visibility.Visible;
    internal ItemsRepeater DeviceCardsRepeaterElement => DeviceCardsRepeater;
    internal FrameworkElement ViperAnchor => ViperPollingRateSaveButton;

    private MainViewModel ViewModel => (MainViewModel)DataContext;
    private CancellationToken Lifetime => Host.LifetimeToken;

    public DevicesPage()
    {
        InitializeComponent();
        BladeBrightnessSlider.AddHandler(
            UIElement.PointerPressedEvent,
            new PointerEventHandler(BladeBrightnessPointerPressed),
            handledEventsToo: true);
        BladeBrightnessSlider.AddHandler(
            UIElement.PointerReleasedEvent,
            new PointerEventHandler(BladeBrightnessPointerReleased),
            handledEventsToo: true);
        BladeBrightnessSlider.AddHandler(
            UIElement.PointerCaptureLostEvent,
            new PointerEventHandler(BladeBrightnessPointerCaptureLost),
            handledEventsToo: true);
        Microsoft.Win32.SystemEvents.PowerModeChanged += SystemPowerModeChanged;
    }

    private async void RefreshClick(object sender, RoutedEventArgs e)
    {
        await ViewModel.Refresh.RefreshDevicesAsync(Lifetime);
        _ = ViewModel.RefreshHueAsync(Lifetime);
    }

    internal void SelectDevice(string device)
    {
        if (device == "blade" &&
            !ViewModel.DeviceSelectorItems.Any(item => item.Kind == DeviceSelectorItemKind.Blade))
        {
            // Without a Blade entry, stay on the overview instead of jumping to another device.
            device = "none";
        }
        if (device == "viper" && ViewModel.Viper.DeviceVisibility != Visibility.Visible)
        {
            device = "none";
        }
        if (device == "coolingpad" && !ViewModel.CoolingPad.IsConnected)
        {
            device = "none";
        }
        if (device == "kiyo" && !ViewModel.Kiyo.IsConnected)
        {
            device = "none";
        }
        BladeDevicePanel.Visibility = device == "blade" ? Visibility.Visible : Visibility.Collapsed;
        ViperDevicePanel.Visibility = device == "viper" ? Visibility.Visible : Visibility.Collapsed;
        OpenRazerDevicePanel.Visibility = device == "openrazer" ? Visibility.Visible : Visibility.Collapsed;
        OpenRazerKrakenPanel.Visibility = device == "kraken" ? Visibility.Visible : Visibility.Collapsed;
        BlackSharkPanel.Visibility = device == "blackshark" ? Visibility.Visible : Visibility.Collapsed;
        HeadsetAudioPanel.Visibility = device == "headsetaudio" ? Visibility.Visible : Visibility.Collapsed;
        KeyboardPanel.Visibility = device == "keyboard" ? Visibility.Visible : Visibility.Collapsed;
        GamepadPanel.Visibility = device == "gamepad" ? Visibility.Visible : Visibility.Collapsed;
        CoolingPadPanel.Visibility = device == "coolingpad" ? Visibility.Visible : Visibility.Collapsed;
        ChromaPanel.Visibility = device == "chroma" ? Visibility.Visible : Visibility.Collapsed;
        BlePanel.Visibility = device == "ble" ? Visibility.Visible : Visibility.Collapsed;
        AwBleHeadsetPanel.Visibility = device == "awbleheadset" ? Visibility.Visible : Visibility.Collapsed;
        KiyoPanel.Visibility = device == "kiyo" ? Visibility.Visible : Visibility.Collapsed;
        HuePanel.Visibility = device == "hue" ? Visibility.Visible : Visibility.Collapsed;
        UpdateDeviceSelector(device);
        if (device == "blade")
        {
            RefreshDedicatedGpuMode();
        }
    }

    internal void UpdateDeviceSelector(string device)
    {
        var selected = (Brush)Application.Current.Resources["SurfaceRaisedBrush"];
        var transparent = new SolidColorBrush(Color.FromArgb(0, 0, 0, 0));
        foreach (var item in ViewModel.DeviceSelectorItems)
        {
            var isSelected = item.Kind switch
            {
                DeviceSelectorItemKind.Blade => device == "blade",
                DeviceSelectorItemKind.Viper => device == "viper",
                DeviceSelectorItemKind.OpenRazer => device == "openrazer" &&
                    item.Source is OpenRazerDeviceRowViewModel row &&
                    StringComparer.OrdinalIgnoreCase.Equals(row.Connection.InstanceId, ViewModel.OpenRazer.SelectedDevice?.InstanceId),
                DeviceSelectorItemKind.Kraken => device == "kraken" &&
                    item.Source is OpenRazerKrakenDeviceRowViewModel row &&
                    StringComparer.OrdinalIgnoreCase.Equals(row.Connection.InstanceId, ViewModel.OpenRazer.SelectedKraken?.InstanceId),
                DeviceSelectorItemKind.BlackShark => device == "blackshark" &&
                    item.Source is BlackSharkDeviceRowViewModel blackSharkRow &&
                    StringComparer.OrdinalIgnoreCase.Equals(
                        blackSharkRow.Connection.DevicePath, ViewModel.SelectedBlackShark?.DevicePath),
                DeviceSelectorItemKind.HeadsetAudio => device == "headsetaudio" &&
                    item.Source is RazerHeadsetAudioDeviceRowViewModel headsetAudioRow &&
                    StringComparer.OrdinalIgnoreCase.Equals(
                        headsetAudioRow.Connection.DevicePath, ViewModel.SelectedHeadsetAudio?.DevicePath),
                DeviceSelectorItemKind.Keyboard => device == "keyboard" &&
                    item.Source is RazerKeyboardDeviceRowViewModel keyboardRow &&
                    StringComparer.OrdinalIgnoreCase.Equals(
                        keyboardRow.Connection.DevicePath, ViewModel.SelectedKeyboard?.DevicePath),
                DeviceSelectorItemKind.Gamepad => device == "gamepad" &&
                    item.Source is RazerGamepadDeviceRowViewModel gamepadRow &&
                    StringComparer.OrdinalIgnoreCase.Equals(
                        gamepadRow.Connection.DevicePath, ViewModel.SelectedGamepad?.DevicePath),
                DeviceSelectorItemKind.CoolingPad => device == "coolingpad",
                DeviceSelectorItemKind.Kiyo => device == "kiyo",
                DeviceSelectorItemKind.Ble => device == "ble" &&
                    item.Source is RazerBleDeviceRowViewModel bleRow &&
                    StringComparer.OrdinalIgnoreCase.Equals(
                        bleRow.Device.Id, ViewModel.SelectedBle?.DeviceId),
                DeviceSelectorItemKind.AwBleHeadset => device == "awbleheadset" &&
                    item.Source is RazerAwBleHeadsetRowViewModel awRow &&
                    StringComparer.OrdinalIgnoreCase.Equals(
                        awRow.Device.Id, ViewModel.SelectedAwBleHeadset?.Device.Id),
                DeviceSelectorItemKind.Chroma => device == "chroma" &&
                    item.Source is RazerChromaDeviceRowViewModel chromaRow &&
                    StringComparer.OrdinalIgnoreCase.Equals(
                        chromaRow.Connection.DevicePath, ViewModel.SelectedChroma?.DevicePath),
                _ => false,
            };
            item.SetSelectorBackground(isSelected ? selected : transparent);
        }
    }

    internal void RefreshSelectorHighlight() => UpdateDeviceSelector(CurrentDeviceRoute());

    internal string CurrentDeviceRoute() =>
        OpenRazerKrakenPanel.Visibility == Visibility.Visible ? "kraken" :
        BlackSharkPanel.Visibility == Visibility.Visible ? "blackshark" :
        OpenRazerDevicePanel.Visibility == Visibility.Visible ? "openrazer" :
        HeadsetAudioPanel.Visibility == Visibility.Visible ? "headsetaudio" :
        KeyboardPanel.Visibility == Visibility.Visible ? "keyboard" :
        GamepadPanel.Visibility == Visibility.Visible ? "gamepad" :
        CoolingPadPanel.Visibility == Visibility.Visible ? "coolingpad" :
        KiyoPanel.Visibility == Visibility.Visible ? "kiyo" :
        ChromaPanel.Visibility == Visibility.Visible ? "chroma" :
        BlePanel.Visibility == Visibility.Visible ? "ble" :
        AwBleHeadsetPanel.Visibility == Visibility.Visible ? "awbleheadset" :
        HuePanel.Visibility == Visibility.Visible ? "hue" :
        ViperDevicePanel.Visibility == Visibility.Visible ? "viper" :
        BladeDevicePanel.Visibility == Visibility.Visible ? "blade" :
        string.Empty;

    internal void OnViewModelPropertyChanged(string? propertyName)
    {
        if (propertyName == nameof(ViperWorkspace.DeviceVisibility) &&
            ViewModel.Viper.DeviceVisibility != Visibility.Visible)
        {
            DispatcherQueue.TryEnqueue(() => SelectDevice("blade"));
        }
        if (propertyName == nameof(MainViewModel.DeviceSelectorItems))
        {
            // Rebuilding the list only refreshes the highlight; never change the active page here.
            DispatcherQueue.TryEnqueue(RefreshSelectorHighlight);
        }
        if (propertyName == nameof(MainViewModel.ConnectedDevices) &&
            !ViewModel.CoolingPad.IsConnected &&
            CoolingPadPanel.Visibility == Visibility.Visible)
        {
            DispatcherQueue.TryEnqueue(() => SelectDevice("none"));
        }
        if (propertyName == nameof(MainViewModel.ConnectedDevices) &&
            !ViewModel.Kiyo.IsConnected &&
            KiyoPanel.Visibility == Visibility.Visible)
        {
            DispatcherQueue.TryEnqueue(() => SelectDevice("none"));
        }
        if (propertyName == nameof(OpenRazerWorkspace.SelectedDevice) &&
            ViewModel.OpenRazer.SelectedDevice is null &&
            OpenRazerDevicePanel.Visibility == Visibility.Visible)
        {
            DispatcherQueue.TryEnqueue(() => SelectDevice("blade"));
        }
        else if (propertyName == nameof(OpenRazerWorkspace.SelectedDevice) &&
            OpenRazerDevicePanel.Visibility == Visibility.Visible)
        {
            DispatcherQueue.TryEnqueue(() => UpdateDeviceSelector("openrazer"));
        }
        if (propertyName == nameof(OpenRazerWorkspace.SelectedKraken) &&
            ViewModel.OpenRazer.SelectedKraken is null &&
            OpenRazerKrakenPanel.Visibility == Visibility.Visible)
        {
            DispatcherQueue.TryEnqueue(() => SelectDevice("blade"));
        }
        else if (propertyName == nameof(OpenRazerWorkspace.SelectedKraken) &&
            OpenRazerKrakenPanel.Visibility == Visibility.Visible)
        {
            DispatcherQueue.TryEnqueue(() => UpdateDeviceSelector("kraken"));
        }
        if (propertyName == nameof(MainViewModel.SelectedBlackShark) &&
            ViewModel.SelectedBlackShark is null &&
            BlackSharkPanel.Visibility == Visibility.Visible)
        {
            DispatcherQueue.TryEnqueue(() => SelectDevice("blade"));
        }
        else if (propertyName == nameof(MainViewModel.SelectedBlackShark) &&
            BlackSharkPanel.Visibility == Visibility.Visible)
        {
            DispatcherQueue.TryEnqueue(() => UpdateDeviceSelector("blackshark"));
        }
    }

    internal async Task ShowDeviceAsync(object? source) => await SelectDeviceSourceAsync(source);

    internal void ScrollToTop() => PageScrollViewer.ChangeView(null, 0, null, disableAnimation: true);

    internal void CenterOn(FrameworkElement target)
    {
        var targetTop = target.TransformToVisual(PageScrollViewer)
            .TransformPoint(new global::Windows.Foundation.Point(0, 0)).Y +
            PageScrollViewer.VerticalOffset;
        var centeredOffset = targetTop -
            Math.Max(0, (PageScrollViewer.ViewportHeight - target.ActualHeight) / 2);
        PageScrollViewer.ChangeView(
            null,
            Math.Clamp(centeredOffset, 0, PageScrollViewer.ScrollableHeight),
            null,
            disableAnimation: true);
    }
}
