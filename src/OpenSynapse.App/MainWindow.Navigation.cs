using System.Collections.Specialized;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using OpenSynapse.App.ViewModels;
using OpenSynapse.Core.Devices;
using OpenSynapse.Core.Storage;
using Windows.Graphics;
using Windows.UI;

namespace OpenSynapse.App;

public sealed partial class MainWindow
{
    private int _studioRefreshQueued;
    private bool _studioDevicesDirty;
    private string _studioDeviceSignature = string.Empty;

    internal void RequestNavigation(string page) => _dispatcherQueue.TryEnqueue(() =>
    {
        if (_viewModel.IsLiteMode && page is "overview" or "studio")
        {
            page = "devices";
        }

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

    private void ApplyLiteMode()
    {
        var liteMode = _viewModel.IsLiteMode;
        var visiblePages = LiteModePolicy.VisibleNavigationPages(liteMode);
        OverviewNavigationItem.Visibility = visiblePages.Contains("overview")
            ? Visibility.Visible
            : Visibility.Collapsed;
        LightingStudioNavigationItem.Visibility = visiblePages.Contains("studio")
            ? Visibility.Visible
            : Visibility.Collapsed;
        LiteModeBadge.Visibility = liteMode ? Visibility.Visible : Visibility.Collapsed;
        DevicesPage.SetLiteMode(liteMode);
        SettingsPage.SetLiteMode(liteMode);
        if (!liteMode)
        {
            return;
        }

        RootNavigationView.SelectedItem = DevicesNavigationItem;
        OverviewPage.Visibility = Visibility.Collapsed;
        LightingStudioPage.Visibility = Visibility.Collapsed;
        DevicesPage.Visibility = Visibility.Visible;
        DevicesPage.RefreshAdvancedFeaturesVisibility();
    }

    internal void RequestDeviceNavigation(object source)
    {
        RequestNavigation("devices");
        _dispatcherQueue.TryEnqueue(async () => await DevicesPage.ShowDeviceAsync(source));
    }

    private void OnOverviewDeviceRequested(object source) => RequestDeviceNavigation(source);

    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(BladeWorkspace.BladeProductId))
        {
            UpdatePerformanceSamplingState();
        }
        if (args.PropertyName == nameof(RefreshWorkspace.LastRefreshText))
        {
            RequestStudioDeviceRefresh();
        }
        ProfilesPage.OnViewModelPropertyChanged(args.PropertyName);
        DevicesPage.OnViewModelPropertyChanged(args.PropertyName);
    }

    private void OnRazerHidDevicesChanged(object? sender, NotifyCollectionChangedEventArgs args) =>
        RequestStudioDeviceRefresh();

    /// <summary>Coalesces device events into one Studio refresh on the dispatcher.</summary>
    private void RequestStudioDeviceRefresh()
    {
        if (Interlocked.Exchange(ref _studioRefreshQueued, 1) != 0) return;
        _dispatcherQueue.TryEnqueue(RefreshStudioDevicesIfCurrent);
    }

    private void RefreshStudioDevicesIfCurrent()
    {
        Interlocked.Exchange(ref _studioRefreshQueued, 0);
        if (_lifetime.IsCancellationRequested) return;
        if (LightingStudioPage.Visibility != Visibility.Visible || !AppWindow.IsVisible)
        {
            // A hidden page only records the change; navigation refreshes once on entry.
            _studioDevicesDirty = true;
            return;
        }

        var signature = StudioDeviceSignature();
        if (!_studioDevicesDirty &&
            StringComparer.Ordinal.Equals(signature, _studioDeviceSignature)) return;
        _studioDeviceSignature = signature;
        _studioDevicesDirty = false;
        _ = LightingStudioPage.RefreshDevicesFromHostAsync();
    }

    /// <summary>The Studio list is built from these three live sources.</summary>
    private string StudioDeviceSignature() =>
        string.Join(
            "|",
            _viewModel.CurrentDeviceDescriptors
                .Where(device => device.Access == DeviceAccessState.Available)
                .OrderBy(device => device.ProductId)
                .ThenBy(device => device.Id, StringComparer.OrdinalIgnoreCase)
                .Select(device => $"{device.ProductId:X4}:{device.Id}:{device.Access}")) +
        "#" + string.Join(
            "|",
            _viewModel.RazerHid.Devices
                .OrderBy(row => row.Connection.InstanceId, StringComparer.OrdinalIgnoreCase)
                .Select(row =>
                    $"{row.Connection.InstanceId}:{row.Connection.Definition.ProductId}:" +
                    $"{row.Connection.Definition.MatrixDimensions?.Rows}x" +
                    $"{row.Connection.Definition.MatrixDimensions?.Columns}:{row.Connection.IsReady}")) +
        "#" + (_viewModel.RazerHid.ArgbConnection?.InstanceId ?? string.Empty);

#if DEBUG
    /// <summary>Lets background UI QA open a page without synthesizing pointer input.</summary>
    private void StartPageIfRequested()
    {
        var page = Environment.GetEnvironmentVariable("OPENSSYNAPSE_START_PAGE");
        if (string.IsNullOrWhiteSpace(page)) return;
        if (int.TryParse(Environment.GetEnvironmentVariable("OPENSSYNAPSE_START_PAGE_DELAY_MS"), out var delay) &&
            delay > 0)
        {
            var timer = _dispatcherQueue.CreateTimer();
            timer.Interval = TimeSpan.FromMilliseconds(delay);
            timer.IsRepeating = false;
            timer.Tick += (_, _) =>
            {
                timer.Stop();
                Environment.SetEnvironmentVariable("OPENSSYNAPSE_START_PAGE_DELAY_MS", null);
                StartPageIfRequested();
            };
            timer.Start();
            return;
        }

        RootNavigationView.SelectedItem = RootNavigationView.MenuItems
            .Concat(RootNavigationView.FooterMenuItems)
            .OfType<NavigationViewItem>()
            .FirstOrDefault(item => StringComparer.Ordinal.Equals(item.Tag as string, page));
    }
#endif

    private void NavigationChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        var page = (args.SelectedItemContainer as NavigationViewItem)?.Tag as string;
        OverviewPage.Visibility = page == "overview" ? Visibility.Visible : Visibility.Collapsed;
        DevicesPage.Visibility = page == "devices" ? Visibility.Visible : Visibility.Collapsed;
        LightingStudioPage.Visibility = page == "studio" ? Visibility.Visible : Visibility.Collapsed;
        MacrosPage.Visibility = page == "macros" ? Visibility.Visible : Visibility.Collapsed;
        ProfilesPage.Visibility = page == "profiles" ? Visibility.Visible : Visibility.Collapsed;
        SettingsPage.Visibility = page == "settings" ? Visibility.Visible : Visibility.Collapsed;
        DiagnosticsPage.Visibility = page == "diagnostics" ? Visibility.Visible : Visibility.Collapsed;
        if (page == "studio")
        {
            LightingStudioPage.OnNavigatedTo();
            // Entry already refreshes both HID and ARGB; start clean.
            _studioDevicesDirty = false;
            _studioDeviceSignature = StudioDeviceSignature();
        }
        else
        {
            LightingStudioPage.OnNavigatedFrom();
        }
        if (page == "devices")
        {
            DevicesPage.RefreshAdvancedFeaturesVisibility();
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
            _viewModel.Diagnostics.EnsureRazerHidRows();
            _viewModel.Refresh.RequestDeviceRefresh();
        }
    }

    private void UpdatePerformanceSamplingState()
    {
        // The overlay is a separate window, so it keeps sampling while the main
        // window is hidden. Without a visible consumer, the two-second sensor pass
        // is pure background cost.
        _viewModel.Telemetry.SetPerformanceSamplingEnabled(
            _performanceOverlayEnabled ||
            (AppWindow.IsVisible &&
             (OverviewPage.Visibility == Visibility.Visible ||
              (DevicesPage.Visibility == Visibility.Visible && DevicesPage.IsBladePanelVisible))));
        _viewModel.Refresh.SetDeviceReadoutSamplingEnabled(
            AppWindow.IsVisible &&
            (DevicesPage.Visibility == Visibility.Visible ||
             DiagnosticsPage.Visibility == Visibility.Visible));
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
