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
    private async void RefreshClick(object sender, RoutedEventArgs e) =>
        await _viewModel.RefreshDevicesAsync(_lifetime.Token);

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
        if (device == "viper" && _viewModel.ViperDeviceVisibility != Visibility.Visible)
        {
            device = "blade";
        }
        BladeDevicePanel.Visibility = device == "blade" ? Visibility.Visible : Visibility.Collapsed;
        ViperDevicePanel.Visibility = device == "viper" ? Visibility.Visible : Visibility.Collapsed;
        OpenRazerDevicePanel.Visibility = device == "openrazer" ? Visibility.Visible : Visibility.Collapsed;
        OpenRazerKrakenPanel.Visibility = device == "kraken" ? Visibility.Visible : Visibility.Collapsed;
        UpdateDeviceSelector(device);
    }

    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(MainViewModel.InternalDisplayRefreshRates))
        {
            _dispatcherQueue.TryEnqueue(() =>
            {
                _behaviorUiReady = false;
                RebuildRefreshRateCycleOptions();
                _behaviorUiReady = true;
            });
        }
        if (args.PropertyName == nameof(MainViewModel.BladePerformanceCycleModes))
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
        if (args.PropertyName == nameof(MainViewModel.SelectedOpenRazerDevice) &&
            _viewModel.SelectedOpenRazerDevice is null &&
            OpenRazerDevicePanel.Visibility == Visibility.Visible)
        {
            _dispatcherQueue.TryEnqueue(() => SelectDevice("blade"));
        }
        if (args.PropertyName == nameof(MainViewModel.SelectedOpenRazerKraken) &&
            _viewModel.SelectedOpenRazerKraken is null &&
            OpenRazerKrakenPanel.Visibility == Visibility.Visible)
        {
            _dispatcherQueue.TryEnqueue(() => SelectDevice("blade"));
        }
    }

    private void UpdateDeviceSelector(string device)
    {
        var selected = (Brush)Application.Current.Resources["SurfaceRaisedBrush"];
        var transparent = new SolidColorBrush(Color.FromArgb(0, 0, 0, 0));
        BladeDeviceButton.Background = device == "blade" ? selected : transparent;
        ViperDeviceButton.Background = device == "viper" ? selected : transparent;
    }

    private void NavigationChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        var page = (args.SelectedItemContainer as NavigationViewItem)?.Tag as string;
        OverviewPage.Visibility = page == "overview" ? Visibility.Visible : Visibility.Collapsed;
        DevicesPage.Visibility = page == "devices" ? Visibility.Visible : Visibility.Collapsed;
        ProfilesPage.Visibility = page == "profiles" ? Visibility.Visible : Visibility.Collapsed;
        SettingsPage.Visibility = page == "settings" ? Visibility.Visible : Visibility.Collapsed;
        DiagnosticsPage.Visibility = page == "diagnostics" ? Visibility.Visible : Visibility.Collapsed;
        UpdatePerformanceSamplingState();
        UpdateChromaRestStatusTimer();
    }

    private void UpdatePerformanceSamplingState() =>
        _viewModel.SetPerformanceSamplingEnabled(
            AppWindow.IsVisible && OverviewPage.Visibility == Visibility.Visible);

    private void ApplyDarkTheme()
    {
        RootNavigationView.RequestedTheme = ElementTheme.Dark;
        ApplyTitleBarColors();
        UpdateDeviceSelector(OpenRazerKrakenPanel.Visibility == Visibility.Visible
            ? "kraken"
            : OpenRazerDevicePanel.Visibility == Visibility.Visible
                ? "openrazer"
                : BladeDevicePanel.Visibility == Visibility.Visible ? "blade" : "viper");
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
        var width = Math.Min((int)Math.Round(1180 * scale), Math.Max(800, workArea.Width - 48));
        var height = Math.Min((int)Math.Round(800 * scale), Math.Max(600, workArea.Height - 48));
        var bounds = new RectInt32(
            workArea.X + Math.Max(0, (workArea.Width - width) / 2),
            workArea.Y + Math.Max(0, (workArea.Height - height) / 2),
            width,
            height);
        AppWindow.MoveAndResize(bounds);
    }
}
