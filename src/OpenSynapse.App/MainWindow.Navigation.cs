using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Graphics;
using Windows.UI;

namespace OpenSynapse.App;

public sealed partial class MainWindow
{
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

    internal void RequestDeviceNavigation(object source)
    {
        RequestNavigation("devices");
        _dispatcherQueue.TryEnqueue(async () => await DevicesPage.ShowDeviceAsync(source));
    }

    private void OnOverviewDeviceRequested(object source) => RequestDeviceNavigation(source);

    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs args)
    {
        ProfilesPage.OnViewModelPropertyChanged(args.PropertyName);
        DevicesPage.OnViewModelPropertyChanged(args.PropertyName);
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
        SettingsPage.UpdateChromaRestStatusTimer(AppWindow.IsVisible);
        if (DevicesPage.Visibility == Visibility.Visible && DevicesPage.IsBladePanelVisible)
        {
            DevicesPage.RefreshDedicatedGpuMode();
        }
        if (DiagnosticsPage.Visibility == Visibility.Visible)
        {
            // The rebuild is skipped while this page is hidden, so entering it must refresh
            // once or the list would show whatever was captured before the user left.
            _viewModel.Refresh.RequestDeviceRefresh();
        }
    }

    private void UpdatePerformanceSamplingState()
    {
        _viewModel.Telemetry.SetPerformanceSamplingEnabled(
            AppWindow.IsVisible && OverviewPage.Visibility == Visibility.Visible);
        _viewModel.Refresh.SetDeviceReadoutSamplingEnabled(
            AppWindow.IsVisible && DevicesPage.Visibility == Visibility.Visible);
        _viewModel.Refresh.SetDiagnosticsRebuildEnabled(
            AppWindow.IsVisible && DiagnosticsPage.Visibility == Visibility.Visible);
    }

    private void ApplyDarkTheme()
    {
        RootNavigationView.RequestedTheme = ElementTheme.Dark;
        ApplyTitleBarColors();
        DevicesPage.RefreshSelectorHighlight();
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
