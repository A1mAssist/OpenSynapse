using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using Microsoft.Win32;
using OpenSynapse.App.ViewModels;
using Windows.Graphics;
using Windows.Storage.Pickers;
using Windows.UI;

namespace OpenSynapse.App;

public sealed partial class MainWindow : Window
{
    private const int MinimumWindowWidth = 1080;
    private const int MinimumWindowHeight = 680;
    private readonly MainViewModel _viewModel;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly DispatcherQueue _dispatcherQueue;
    private bool _trayLifecycleEnabled;
    private bool _exitRequested;
    private bool _enforcingMinimumSize;

    public MainWindow(MainViewModel viewModel)
    {
        _viewModel = viewModel;
        _dispatcherQueue = DispatcherQueue.GetForCurrentThread()
            ?? throw new InvalidOperationException("OpenSynapse 主窗口必须在 DispatcherQueue 线程创建。");
        InitializeComponent();
        var appIconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "OpenSynapse.ico");
        if (File.Exists(appIconPath))
        {
            AppWindow.SetIcon(appIconPath);
        }
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        RootNavigationView.DataContext = _viewModel;
        SystemBackdrop = new MicaBackdrop();
        ApplyDarkTheme();
        Activated += OnActivated;
        Closed += OnClosed;
        AppWindow.Closing += OnAppWindowClosing;
        AppWindow.Changed += OnAppWindowChanged;
        SystemEvents.PowerModeChanged += OnPowerModeChanged;
    }

    internal void RequestActivation() => _dispatcherQueue.TryEnqueue(RestoreAndActivate);

    internal void RequestNavigation(string page) => _dispatcherQueue.TryEnqueue(() =>
    {
        var item = RootNavigationView.MenuItems
            .OfType<NavigationViewItem>()
            .FirstOrDefault(candidate => StringComparer.Ordinal.Equals(candidate.Tag as string, page));
        if (item is not null)
        {
            RootNavigationView.SelectedItem = item;
        }

        RestoreAndActivate();
    });

    internal void RequestStartupChange(bool enabled) => _dispatcherQueue.TryEnqueue(
        () => _ = _viewModel.SetStartupEnabledAsync(enabled, _lifetime.Token));

    internal void EnableTrayLifecycle() => _trayLifecycleEnabled = true;

    internal void DisableTrayLifecycle() => _dispatcherQueue.TryEnqueue(() =>
    {
        _trayLifecycleEnabled = false;
        RestoreAndActivate();
    });

    internal void RequestExit() => _dispatcherQueue.TryEnqueue(() =>
    {
        _exitRequested = true;
        Close();
    });

    private void RestoreAndActivate()
    {
        AppWindow.Show();
        if (AppWindow.Presenter is OverlappedPresenter { State: OverlappedPresenterState.Minimized } presenter)
        {
            presenter.Restore();
        }

        Activate();
    }

    private void OnAppWindowClosing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        if (!_trayLifecycleEnabled || _exitRequested)
        {
            return;
        }

        args.Cancel = true;
        sender.Hide();
    }

    private void OnAppWindowChanged(AppWindow sender, AppWindowChangedEventArgs args)
    {
        if (!args.DidSizeChange || _enforcingMinimumSize)
        {
            return;
        }

        var workArea = DisplayArea.GetFromWindowId(sender.Id, DisplayAreaFallback.Primary).WorkArea;
        var minimumWidth = Math.Min(MinimumWindowWidth, workArea.Width);
        var minimumHeight = Math.Min(MinimumWindowHeight, workArea.Height);
        var size = sender.Size;
        var width = Math.Max(size.Width, minimumWidth);
        var height = Math.Max(size.Height, minimumHeight);
        if (width == size.Width && height == size.Height)
        {
            return;
        }

        _enforcingMinimumSize = true;
        try
        {
            sender.Resize(new SizeInt32(width, height));
        }
        finally
        {
            _enforcingMinimumSize = false;
        }
    }

    private void OnPowerModeChanged(object? sender, PowerModeChangedEventArgs args)
    {
        if (args.Mode == PowerModes.Suspend)
        {
            try
            {
                _viewModel.PrepareForSuspendAsync().GetAwaiter().GetResult();
            }
            catch (Exception exception)
            {
                _dispatcherQueue.TryEnqueue(() =>
                    _viewModel.ReportApplicationError($"休眠前恢复风扇：{exception.Message}"));
            }
        }
        else if (args.Mode == PowerModes.Resume)
        {
            _dispatcherQueue.TryEnqueue(_viewModel.RequestDeviceRefresh);
        }
    }

    private async void OnActivated(object sender, WindowActivatedEventArgs args)
    {
        Activated -= OnActivated;
        ResizeForCurrentDisplay();
        try
        {
            await _viewModel.InitializeAsync(_lifetime.Token);
            _ = ObserveBackgroundLoopAsync(
                () => _viewModel.RunPerformanceLoopAsync(_lifetime.Token),
                "性能刷新");
            _ = ObserveBackgroundLoopAsync(
                () => _viewModel.RunDeviceWatchLoopAsync(_lifetime.Token),
                "设备监听");
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            _viewModel.ReportApplicationError($"应用初始化：{exception.Message}");
        }
    }

    private async Task ObserveBackgroundLoopAsync(Func<Task> loop, string label)
    {
        try
        {
            await loop();
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            _viewModel.ReportApplicationError($"{label}：{exception.Message}");
        }
    }

    private void OnClosed(object sender, WindowEventArgs args)
    {
        AppWindow.Closing -= OnAppWindowClosing;
        AppWindow.Changed -= OnAppWindowChanged;
        SystemEvents.PowerModeChanged -= OnPowerModeChanged;
        _lifetime.Cancel();
        try
        {
            _viewModel.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
        catch (Exception exception)
        {
            _viewModel.ReportApplicationError($"退出前恢复风扇：{exception.Message}");
        }
        _lifetime.Dispose();
    }

    private async void RefreshClick(object sender, RoutedEventArgs e) =>
        await _viewModel.RefreshDevicesAsync(_lifetime.Token);

    private async void ApplyBrightnessClick(object sender, RoutedEventArgs e) =>
        await _viewModel.ApplyBladeBrightnessAsync(_lifetime.Token);

    private async void ApplyLightingEffectClick(object sender, RoutedEventArgs e) =>
        await _viewModel.ApplySelectedBladeLightingEffectAsync(_lifetime.Token);

    private async void ApplyPerformanceModeClick(object sender, RoutedEventArgs e) =>
        await _viewModel.ApplyBladePerformanceModeAsync(_lifetime.Token);

    private async void AutoApplyComboSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is not ComboBox { IsDropDownOpen: true, Tag: string setting })
        {
            return;
        }

        switch (setting)
        {
            case "performance":
                await _viewModel.ApplyBladePerformanceModeAsync(_lifetime.Token);
                break;
            case "charge":
                await _viewModel.ApplyBladeChargeLimitAsync(_lifetime.Token);
                break;
            case "cpuBoost":
                await _viewModel.ApplyBladeCpuBoostAsync(_lifetime.Token);
                break;
            case "gpuBoost":
                await _viewModel.ApplyBladeGpuBoostAsync(_lifetime.Token);
                break;
            case "lighting":
                await _viewModel.ApplySelectedBladeLightingEffectAsync(_lifetime.Token);
                break;
            case "logo":
                await _viewModel.ApplyBladeLogoAsync(_lifetime.Token);
                break;
        }
    }

    private async void AutoApplyMaxFanToggled(object sender, RoutedEventArgs e)
    {
        if (sender is ToggleSwitch { FocusState: not FocusState.Unfocused })
        {
            await _viewModel.ApplyBladeMaxFanAsync(_lifetime.Token);
        }
    }

    private async void AutoApplyBrightnessValueChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (sender is Slider { FocusState: not FocusState.Unfocused })
        {
            await _viewModel.ApplyBladeBrightnessAsync(_lifetime.Token);
        }
    }

    private async void ApplyChargeLimitClick(object sender, RoutedEventArgs e) =>
        await _viewModel.ApplyBladeChargeLimitAsync(_lifetime.Token);

    private async void ApplyRefreshRateClick(object sender, RoutedEventArgs e) =>
        await _viewModel.ApplyInternalDisplayRefreshRateAsync(_lifetime.Token);

    private async void ApplyCpuBoostClick(object sender, RoutedEventArgs e) =>
        await _viewModel.ApplyBladeCpuBoostAsync(_lifetime.Token);

    private async void ApplyGpuBoostClick(object sender, RoutedEventArgs e) =>
        await _viewModel.ApplyBladeGpuBoostAsync(_lifetime.Token);

    private async void ApplyMaxFanClick(object sender, RoutedEventArgs e) =>
        await _viewModel.ApplyBladeMaxFanAsync(_lifetime.Token);

    private async void ApplyGamingModeClick(object sender, RoutedEventArgs e) =>
        await _viewModel.ApplyBladeGamingModeAsync(_lifetime.Token);

    private async void ApplyStartupAnimationClick(object sender, RoutedEventArgs e) =>
        await _viewModel.ApplyBladeStartupAnimationAsync(_lifetime.Token);

    private async void ApplyOneTimeFullChargeClick(object sender, RoutedEventArgs e) =>
        await _viewModel.ApplyBladeOneTimeFullChargeAsync(_lifetime.Token);

    private async void ApplyLogoClick(object sender, RoutedEventArgs e) =>
        await _viewModel.ApplyBladeLogoAsync(_lifetime.Token);

    private async void ToggleTouchpadClick(object sender, RoutedEventArgs e) =>
        await _viewModel.ToggleBladeTouchpadAsync(_lifetime.Token);

    private async void ApplyPollingRateClick(object sender, RoutedEventArgs e) =>
        await _viewModel.ApplyViperPollingRateAsync(_lifetime.Token);

    private async void ApplyDpiClick(object sender, RoutedEventArgs e) =>
        await _viewModel.ApplyViperDpiAsync(_lifetime.Token);

    private async void ApplyDpiStagesClick(object sender, RoutedEventArgs e) =>
        await _viewModel.ApplyViperDpiStagesAsync(_lifetime.Token);

    private async void ApplyIdleClick(object sender, RoutedEventArgs e) =>
        await _viewModel.ApplyViperIdleAsync(_lifetime.Token);

    private async void ReadViperButtonMappingsClick(object sender, RoutedEventArgs e) =>
        await _viewModel.ReadViperButtonMappingsAsync(_lifetime.Token);

    private async void ApplyViperButtonMappingClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: ViperButtonAssignmentRowViewModel row })
        {
            await _viewModel.ApplyViperButtonMappingAsync(row, _lifetime.Token);
        }
    }

    private async void ProfileSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is ComboBox { SelectedItem: string profileName })
        {
            await _viewModel.SelectProfileAsync(profileName, _lifetime.Token);
        }
    }

    private async void CreateProfileClick(object sender, RoutedEventArgs e) =>
        await _viewModel.CreateProfileAsync(_lifetime.Token);

    private async void CloneProfileClick(object sender, RoutedEventArgs e) =>
        await _viewModel.CloneActiveProfileAsync(_lifetime.Token);

    private async void RenameProfileClick(object sender, RoutedEventArgs e) =>
        await _viewModel.RenameActiveProfileAsync(_lifetime.Token);

    private async void DeleteProfileClick(object sender, RoutedEventArgs e)
    {
        var dialog = new ContentDialog
        {
            Title = "删除当前配置？",
            Content = $"将删除“{_viewModel.ActiveProfileName}”，设备设置不会被卸载。",
            PrimaryButtonText = "删除",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = RootNavigationView.XamlRoot,
        };
        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
        {
            await _viewModel.DeleteActiveProfileAsync(_lifetime.Token);
        }
    }

    private async void StartupToggled(object sender, RoutedEventArgs e)
    {
        if (sender is ToggleSwitch toggle && toggle.IsOn != _viewModel.IsStartupEnabled)
        {
            await _viewModel.SetStartupEnabledAsync(toggle.IsOn, _lifetime.Token);
        }
    }

    private async void BindApplicationClick(object sender, RoutedEventArgs e)
    {
        var picker = new FileOpenPicker
        {
            SuggestedStartLocation = PickerLocationId.ComputerFolder,
        };
        picker.FileTypeFilter.Add(".exe");
        InitializePicker(picker);
        var file = await picker.PickSingleFileAsync();
        if (file is not null && !string.IsNullOrWhiteSpace(file.Path))
        {
            await _viewModel.BindApplicationAsync(file.Path, _lifetime.Token);
        }
    }

    private async void UnbindApplicationClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string executablePath })
        {
            await _viewModel.UnbindApplicationAsync(executablePath, _lifetime.Token);
        }
    }

    private async void ImportProfilesClick(object sender, RoutedEventArgs e)
    {
        var picker = new FileOpenPicker
        {
            SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
        };
        picker.FileTypeFilter.Add(".json");
        InitializePicker(picker);
        var file = await picker.PickSingleFileAsync();
        if (file is not null && !string.IsNullOrWhiteSpace(file.Path))
        {
            await _viewModel.ImportProfilesAsync(file.Path, _lifetime.Token);
        }
    }

    private async void ExportProfilesClick(object sender, RoutedEventArgs e)
    {
        var picker = new FileSavePicker
        {
            SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
            SuggestedFileName = $"OpenSynapse-{_viewModel.ActiveProfileName}",
        };
        picker.FileTypeChoices.Add("OpenSynapse 配置", [".json"]);
        InitializePicker(picker);
        var file = await picker.PickSaveFileAsync();
        if (file is not null && !string.IsNullOrWhiteSpace(file.Path))
        {
            await _viewModel.ExportProfilesAsync(file.Path, _lifetime.Token);
        }
    }

    private void InitializePicker(object picker) =>
        WinRT.Interop.InitializeWithWindow.Initialize(
            picker,
            WinRT.Interop.WindowNative.GetWindowHandle(this));

    private void DeviceSelectorClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string device })
        {
            BladeDevicePanel.Visibility = device == "blade" ? Visibility.Visible : Visibility.Collapsed;
            ViperDevicePanel.Visibility = device == "viper" ? Visibility.Visible : Visibility.Collapsed;
            UpdateDeviceSelector(device);
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
        DiagnosticsPage.Visibility = page == "diagnostics" ? Visibility.Visible : Visibility.Collapsed;
        BreadcrumbText.Text = page switch
        {
            "devices" => "设备",
            "profiles" => "配置",
            "diagnostics" => "诊断",
            _ => "概览",
        };
    }

    private void ApplyDarkTheme()
    {
        RootNavigationView.RequestedTheme = ElementTheme.Dark;
        ApplyTitleBarColors();
        UpdateDeviceSelector(BladeDevicePanel.Visibility == Visibility.Visible ? "blade" : "viper");
    }

    private void ApplyTitleBarColors()
    {
        var titleBar = AppWindow.TitleBar;
        if (!AppWindowTitleBar.IsCustomizationSupported())
        {
            return;
        }

        var background = Color.FromArgb(255, 0x20, 0x20, 0x20);
        var foreground = Color.FromArgb(255, 0xF4, 0xF4, 0xF4);
        var hoverBackground = Color.FromArgb(255, 0x38, 0x38, 0x38);
        var pressedBackground = Color.FromArgb(255, 0x4C, 0x4C, 0x4C);

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
