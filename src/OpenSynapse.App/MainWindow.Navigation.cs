using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using OpenSynapse.App.ViewModels;
using Windows.Graphics;
using Windows.UI;

namespace OpenSynapse.App;

public sealed partial class MainWindow
{
    private async void RefreshClick(object sender, RoutedEventArgs e)
    {
        await _viewModel.RefreshDevicesAsync(_lifetime.Token);
        _ = _viewModel.RefreshHueAsync(_lifetime.Token);
    }

    internal void RequestNavigation(string page) => _dispatcherQueue.TryEnqueue(() =>
    {
        if (StringComparer.Ordinal.Equals(page, "about"))
        {
            RestoreAndActivate();
            ShowAboutWindow();
            return;
        }

        var item = RootNavigationView.MenuItems
            .Concat(RootNavigationView.FooterMenuItems)
            .OfType<NavigationViewItem>()
            .FirstOrDefault(candidate => StringComparer.Ordinal.Equals(candidate.Tag as string, page));
        if (item is not null)
        {
            RootNavigationView.SelectedItem = item;
        }

        RestoreAndActivate();
    });

    private void DeviceSelectorClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string device })
        {
            SelectDevice(device);
        }
    }

    private void SelectDevice(string device)
    {
        if (device == "blade" &&
            !_viewModel.DeviceSelectorItems.Any(item => item.Kind == DeviceSelectorItemKind.Blade))
        {
            // Without a Blade entry, stay on the overview instead of jumping to another device.
            device = "none";
        }
        if (device == "viper" && _viewModel.ViperDeviceVisibility != Visibility.Visible)
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
        ChromaPanel.Visibility = device == "chroma" ? Visibility.Visible : Visibility.Collapsed;
        BlePanel.Visibility = device == "ble" ? Visibility.Visible : Visibility.Collapsed;
        HuePanel.Visibility = device == "hue" ? Visibility.Visible : Visibility.Collapsed;
        UpdateDeviceSelector(device);
        if (device == "blade")
        {
            RefreshDedicatedGpuMode();
        }
    }

    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(DisplayWorkspace.InternalDisplayRefreshRates))
        {
            _dispatcherQueue.TryEnqueue(() =>
            {
                _behaviorUiReady = false;
                RebuildRefreshRateCycleOptions();
                _behaviorUiReady = true;
            });
        }
        if (args.PropertyName == nameof(BladeWorkspace.PerformanceCycleModes))
        {
            _dispatcherQueue.TryEnqueue(() =>
            {
                _behaviorUiReady = false;
                InitializeBehaviorSettingsUi();
            });
        }
        if (args.PropertyName == nameof(MainViewModel.InternalDisplayRefreshRateCycleHertz))
        {
            _dispatcherQueue.TryEnqueue(() =>
            {
                _behaviorUiReady = false;
                RebuildRefreshRateCycleOptions(force: true);
                _behaviorUiReady = true;
            });
        }
        if (args.PropertyName == nameof(MainViewModel.ViperDeviceVisibility) &&
            _viewModel.ViperDeviceVisibility != Visibility.Visible)
        {
            _dispatcherQueue.TryEnqueue(() => SelectDevice("blade"));
        }
        if (args.PropertyName == nameof(MainViewModel.DeviceSelectorItems))
        {
            // Rebuilding the list only refreshes the highlight; never change the active page here.
            _dispatcherQueue.TryEnqueue(() => UpdateDeviceSelector(CurrentDeviceRoute()));
        }
        if (args.PropertyName == nameof(OpenRazerWorkspace.SelectedDevice) &&
            _viewModel.OpenRazer.SelectedDevice is null &&
            OpenRazerDevicePanel.Visibility == Visibility.Visible)
        {
            _dispatcherQueue.TryEnqueue(() => SelectDevice("blade"));
        }
        else if (args.PropertyName == nameof(OpenRazerWorkspace.SelectedDevice) &&
            OpenRazerDevicePanel.Visibility == Visibility.Visible)
        {
            _dispatcherQueue.TryEnqueue(() => UpdateDeviceSelector("openrazer"));
        }
        if (args.PropertyName == nameof(OpenRazerWorkspace.SelectedKraken) &&
            _viewModel.OpenRazer.SelectedKraken is null &&
            OpenRazerKrakenPanel.Visibility == Visibility.Visible)
        {
            _dispatcherQueue.TryEnqueue(() => SelectDevice("blade"));
        }
        else if (args.PropertyName == nameof(OpenRazerWorkspace.SelectedKraken) &&
            OpenRazerKrakenPanel.Visibility == Visibility.Visible)
        {
            _dispatcherQueue.TryEnqueue(() => UpdateDeviceSelector("kraken"));
        }
        if (args.PropertyName == nameof(MainViewModel.SelectedBlackShark) &&
            _viewModel.SelectedBlackShark is null &&
            BlackSharkPanel.Visibility == Visibility.Visible)
        {
            _dispatcherQueue.TryEnqueue(() => SelectDevice("blade"));
        }
        else if (args.PropertyName == nameof(MainViewModel.SelectedBlackShark) &&
            BlackSharkPanel.Visibility == Visibility.Visible)
        {
            _dispatcherQueue.TryEnqueue(() => UpdateDeviceSelector("blackshark"));
        }
    }

    private string CurrentDeviceRoute() =>
        OpenRazerKrakenPanel.Visibility == Visibility.Visible ? "kraken" :
        BlackSharkPanel.Visibility == Visibility.Visible ? "blackshark" :
        OpenRazerDevicePanel.Visibility == Visibility.Visible ? "openrazer" :
        HeadsetAudioPanel.Visibility == Visibility.Visible ? "headsetaudio" :
        KeyboardPanel.Visibility == Visibility.Visible ? "keyboard" :
        GamepadPanel.Visibility == Visibility.Visible ? "gamepad" :
        ChromaPanel.Visibility == Visibility.Visible ? "chroma" :
        BlePanel.Visibility == Visibility.Visible ? "ble" :
        HuePanel.Visibility == Visibility.Visible ? "hue" :
        ViperDevicePanel.Visibility == Visibility.Visible ? "viper" :
        BladeDevicePanel.Visibility == Visibility.Visible ? "blade" :
        string.Empty;

    private void UpdateDeviceSelector(string device)
    {
        var selected = (Brush)Application.Current.Resources["SurfaceRaisedBrush"];
        var transparent = new SolidColorBrush(Color.FromArgb(0, 0, 0, 0));
        foreach (var item in _viewModel.DeviceSelectorItems)
        {
            var isSelected = item.Kind switch
            {
                DeviceSelectorItemKind.Blade => device == "blade",
                DeviceSelectorItemKind.Viper => device == "viper",
                DeviceSelectorItemKind.OpenRazer => device == "openrazer" &&
                    item.Source is OpenRazerDeviceRowViewModel row &&
                    StringComparer.OrdinalIgnoreCase.Equals(row.Connection.InstanceId, _viewModel.OpenRazer.SelectedDevice?.InstanceId),
                DeviceSelectorItemKind.Kraken => device == "kraken" &&
                    item.Source is OpenRazerKrakenDeviceRowViewModel row &&
                    StringComparer.OrdinalIgnoreCase.Equals(row.Connection.InstanceId, _viewModel.OpenRazer.SelectedKraken?.InstanceId),
                DeviceSelectorItemKind.BlackShark => device == "blackshark" &&
                    item.Source is BlackSharkDeviceRowViewModel blackSharkRow &&
                    StringComparer.OrdinalIgnoreCase.Equals(
                        blackSharkRow.Connection.DevicePath, _viewModel.SelectedBlackShark?.DevicePath),
                DeviceSelectorItemKind.HeadsetAudio => device == "headsetaudio" &&
                    item.Source is RazerHeadsetAudioDeviceRowViewModel headsetAudioRow &&
                    StringComparer.OrdinalIgnoreCase.Equals(
                        headsetAudioRow.Connection.DevicePath, _viewModel.SelectedHeadsetAudio?.DevicePath),
                DeviceSelectorItemKind.Keyboard => device == "keyboard" &&
                    item.Source is RazerKeyboardDeviceRowViewModel keyboardRow &&
                    StringComparer.OrdinalIgnoreCase.Equals(
                        keyboardRow.Connection.DevicePath, _viewModel.SelectedKeyboard?.DevicePath),
                DeviceSelectorItemKind.Gamepad => device == "gamepad" &&
                    item.Source is RazerGamepadDeviceRowViewModel gamepadRow &&
                    StringComparer.OrdinalIgnoreCase.Equals(
                        gamepadRow.Connection.DevicePath, _viewModel.SelectedGamepad?.DevicePath),
                DeviceSelectorItemKind.Ble => device == "ble" &&
                    item.Source is RazerBleDeviceRowViewModel bleRow &&
                    StringComparer.OrdinalIgnoreCase.Equals(
                        bleRow.Device.Id, _viewModel.SelectedBle?.DeviceId),
                DeviceSelectorItemKind.Chroma => device == "chroma" &&
                    item.Source is RazerChromaDeviceRowViewModel chromaRow &&
                    StringComparer.OrdinalIgnoreCase.Equals(
                        chromaRow.Connection.DevicePath, _viewModel.SelectedChroma?.DevicePath),
                _ => false,
            };
            item.SetSelectorBackground(isSelected ? selected : transparent);
        }
    }

    private void NavigationChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        var page = (args.SelectedItemContainer as NavigationViewItem)?.Tag as string;
        OverviewPage.Visibility = page == "overview" ? Visibility.Visible : Visibility.Collapsed;
        DevicesPage.Visibility = page == "devices" ? Visibility.Visible : Visibility.Collapsed;
        MacrosPage.Visibility = page == "macros" ? Visibility.Visible : Visibility.Collapsed;
        ProfilesPage.Visibility = page == "profiles" ? Visibility.Visible : Visibility.Collapsed;
        SettingsPage.Visibility = page == "settings" ? Visibility.Visible : Visibility.Collapsed;
        DiagnosticsPage.Visibility = page == "diagnostics" ? Visibility.Visible : Visibility.Collapsed;
        if (page == "devices")
        {
            _ = _viewModel.RefreshHueAsync(_lifetime.Token);
        }
        UpdatePerformanceSamplingState();
        UpdateChromaRestStatusTimer();
        if (DevicesPage.Visibility == Visibility.Visible &&
            BladeDevicePanel.Visibility == Visibility.Visible)
        {
            RefreshDedicatedGpuMode();
        }
        if (DiagnosticsPage.Visibility == Visibility.Visible)
        {
            // The rebuild is skipped while this page is hidden, so entering it must refresh
            // once or the list would show whatever was captured before the user left.
            _viewModel.RequestDeviceRefresh();
        }
    }

    private void UpdatePerformanceSamplingState()
    {
        _viewModel.Telemetry.SetPerformanceSamplingEnabled(
            AppWindow.IsVisible && OverviewPage.Visibility == Visibility.Visible);
        _viewModel.SetDeviceReadoutSamplingEnabled(
            AppWindow.IsVisible && DevicesPage.Visibility == Visibility.Visible);
        _viewModel.SetDiagnosticsRebuildEnabled(
            AppWindow.IsVisible && DiagnosticsPage.Visibility == Visibility.Visible);
    }

    private void ApplyDarkTheme()
    {
        RootNavigationView.RequestedTheme = ElementTheme.Dark;
        ApplyTitleBarColors();
        UpdateDeviceSelector(CurrentDeviceRoute());
    }

    private void ApplyTitleBarColors(bool launchOverlayActive = false)
    {
        var titleBar = AppWindow.TitleBar;
        if (!AppWindowTitleBar.IsCustomizationSupported())
        {
            return;
        }

        var background = launchOverlayActive
            ? Color.FromArgb(255, 0x17, 0x17, 0x17)
            : Color.FromArgb(255, 0x20, 0x20, 0x20);
        var foreground = Color.FromArgb(255, 0xF4, 0xF4, 0xF4);
        var hoverBackground = Color.FromArgb(255, 0x38, 0x38, 0x38);
        var pressedBackground = Color.FromArgb(255, 0x4C, 0x4C, 0x4C);

        AppTitleBar.Background = (Brush)Application.Current.Resources[
            launchOverlayActive ? "CanvasBrush" : "SurfaceBrush"];
        titleBar.BackgroundColor = background;
        titleBar.ForegroundColor = foreground;
        titleBar.InactiveBackgroundColor = background;
        titleBar.InactiveForegroundColor = foreground;
        titleBar.ButtonBackgroundColor = background;
        titleBar.ButtonForegroundColor = foreground;
        titleBar.ButtonHoverBackgroundColor = hoverBackground;
        titleBar.ButtonHoverForegroundColor = foreground;
        titleBar.ButtonPressedBackgroundColor = pressedBackground;
        titleBar.ButtonPressedForegroundColor = foreground;
    }

    private void ResizeForCurrentDisplay()
    {
        var displayArea = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary);
        var workArea = displayArea.WorkArea;
        var scale = (Content as FrameworkElement)?.XamlRoot?.RasterizationScale ?? 1d;
        var maxWidth = Math.Max(800, workArea.Width - 48);
        var maxHeight = Math.Max(600, workArea.Height - 48);
        // Saved geometry is physical pixels. Restoring a size captured on a 100% display
        // would be too narrow on a 200% one, so it is clamped to the same logical minimum
        // the resize handler enforces.
        var minimumWidth = Math.Min((int)Math.Round(MinimumWindowWidth * scale), maxWidth);
        var minimumHeight = Math.Min((int)Math.Round(MinimumWindowHeight * scale), maxHeight);
        var defaultWidth = Math.Min((int)Math.Round(1180 * scale), maxWidth);
        var defaultHeight = Math.Min((int)Math.Round(800 * scale), maxHeight);
        var width = Math.Clamp(
            _behaviorSettings.WindowWidth > 0
                ? Math.Max(_behaviorSettings.WindowWidth, defaultWidth)
                : defaultWidth,
            minimumWidth,
            maxWidth);
        var height = Math.Clamp(
            _behaviorSettings.WindowHeight > 0
                ? Math.Max(_behaviorSettings.WindowHeight, defaultHeight)
                : defaultHeight,
            minimumHeight,
            maxHeight);
        var bounds = new RectInt32(
            workArea.X + Math.Max(0, (workArea.Width - width) / 2),
            workArea.Y + Math.Max(0, (workArea.Height - height) / 2),
            width,
            height);
        AppWindow.MoveAndResize(bounds);
    }
}

