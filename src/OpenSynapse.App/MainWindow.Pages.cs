using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media.Animation;
using OpenSynapse.App.ViewModels;
using OpenSynapse.App.Views;
using OpenSynapse.Windows.Devices;
using OpenSynapse.Windows.Lighting;
using OpenSynapse.Core.Storage;
using OpenSynapse.App.Runtime;

namespace OpenSynapse.App;

public sealed partial class MainWindow
{
    private OverviewPage? _overviewPage;
    private DevicesPage? _devicesPage;
    private LightingStudioPage? _lightingStudioPage;
    private ProfilesPage? _profilesPage;
    private MacrosPage? _macrosPage;
    private SettingsPage? _settingsPage;
    private DiagnosticsPage? _diagnosticsPage;
    private string _currentPage = "overview";
    private readonly List<string> _pageUseOrder = [];

    private int PageCacheLimit => LiteModePolicy.PageCacheLimit(_viewModel.IsLiteMode);

    private OverviewPage OverviewPage => EnsureCachedPage("overview", ref _overviewPage, CreateOverviewPage);
    private DevicesPage DevicesPage => EnsureCachedPage("devices", ref _devicesPage, CreateDevicesPage);
    private LightingStudioPage LightingStudioPage =>
        EnsureCachedPage("studio", ref _lightingStudioPage, CreateLightingStudioPage);
    private ProfilesPage ProfilesPage => EnsureCachedPage("profiles", ref _profilesPage, CreateProfilesPage);
    private MacrosPage MacrosPage => EnsureCachedPage("macros", ref _macrosPage, CreateMacrosPage);
    private SettingsPage SettingsPage => EnsureCachedPage("settings", ref _settingsPage, CreateSettingsPage);
    private DiagnosticsPage DiagnosticsPage =>
        EnsureCachedPage("diagnostics", ref _diagnosticsPage, CreateDiagnosticsPage);

    private T EnsureCachedPage<T>(string key, ref T? page, Func<T> create)
        where T : FrameworkElement
    {
        if (page is null)
        {
            page = create();
            TouchPage(key);
            TrimPageCache();
        }
        return page;
    }

    private OverviewPage CreateOverviewPage()
    {
        var page = new OverviewPage { DataContext = _viewModel };
        page.DeviceRequested += OnOverviewDeviceRequested;
        page.VisibilityChanged += UpdatePerformanceSamplingState;
        return AddPage(page);
    }

    private void InitializeHome()
    {
        _viewModel.Home.QuickActionRequested += OnHomeQuickActionAsync;
        _viewModel.DeviceRules.EnabledChanged += enabled => _viewModel.SetApplicationSwitchingEnabled(enabled);
    }

    private Task OnHomeQuickActionAsync(string id, string? argument, CancellationToken cancellationToken)
    {
        if (id == HomeLayoutSettings.OpenStudio) RequestNavigation("studio");
        return Task.CompletedTask;
    }

    private DevicesPage CreateDevicesPage()
    {
        var page = new DevicesPage { Host = this };
        page.SetLiteMode(_viewModel.IsLiteMode);
        page.RefreshAdvancedFeaturesVisibility();
        page.RefreshLightingPresetOptions();
        AddPage(page);
        page.SyncBladeComboSelections();
        page.SyncTouchpadToggle();
        return page;
    }

    private LightingStudioPage CreateLightingStudioPage()
    {
        var page = new LightingStudioPage { Host = this };
        _viewModel.SuspendStudioLighting = SuspendStudioLightingAsync;
        var sampler = new StudioInputSampler();
        _studioInputSampler = sampler;
        page.Initialize(
            _bladeLightingController,
            () => _viewModel.CurrentDeviceDescriptors,
            BuildStudioDevices,
            CreateStudioHidRouter(sampler),
            sampler,
            CreateStudioArgbRouter(sampler),
            CreateStudioHeadsetRouter(sampler),
            _viewModel.RestoreLightingAfterExternalAsync);
        _studioDevicesDirty = true;
        return AddPage(page);
    }

    private void AddMakoXStudioDevice(List<StudioDeviceOption> options)
    {
        if (_viewModel.Accessories.SelectedHeadsetAudioConnection is not
            { Transport: RazerHeadsetAudioTransport.Jolie64 } mako)
        {
            return;
        }

        options.Add(new StudioDeviceOption(
            mako.Name,
            null,
            mako.DevicePath,
            mako.ProductId,
            1,
            RazerMakoXChromaService.LedCount,
            Category: "headset"));
    }

    private StudioHeadsetOutputRouter? CreateStudioHeadsetRouter(StudioInputSampler sampler) =>
        new(
            () => _viewModel.Accessories.SelectedHeadsetAudioConnection,
            (rows, columns) => sampler.Sample(rows, columns),
            () => _viewModel.Accessories.SelectedHeadsetAudio?.StopSoftwareEffectForExternalAsync() ??
                Task.CompletedTask);

    private ProfilesPage CreateProfilesPage()
    {
        var page = new ProfilesPage { Host = this, DataContext = _viewModel };
        page.InitializeBehaviorSettingsUi();
        return AddPage(page);
    }

    private MacrosPage CreateMacrosPage()
    {
        var page = new MacrosPage { Host = this };
        page.OpenEditorRequested += (_, _) => OpenMacroEditorWindow();
        return AddPage(page);
    }

    private SettingsPage CreateSettingsPage()
    {
        var page = new SettingsPage { Host = this, DataContext = _viewModel };
        page.SetLiteMode(_viewModel.IsLiteMode);
        page.Initialize();
        page.InitializeBehaviorSettingsUi();
        page.InitializeUpdateUi();
        page.UpdateChromaRestStatusTimer(AppWindow.IsVisible);
        return AddPage(page);
    }

    private DiagnosticsPage CreateDiagnosticsPage()
    {
        var page = new DiagnosticsPage { Host = this };
        return AddPage(page);
    }

    private T AddPage<T>(T page) where T : FrameworkElement
    {
        page.Visibility = Visibility.Collapsed;
        PageHost.Children.Add(page);
        MemoryTrace.PageHostCount = PageHost.Children.Count;
        Localized.RefreshTree(page);
        return page;
    }

    private FrameworkElement EnsurePage(string page)
    {
        FrameworkElement view = page switch
        {
            "devices" => DevicesPage,
            "studio" => LightingStudioPage,
            "profiles" => ProfilesPage,
            "macros" => MacrosPage,
            "settings" => SettingsPage,
            "diagnostics" => DiagnosticsPage,
            _ => OverviewPage,
        };
        TouchPage(page);
        return view;
    }

    private void ShowPage(string page)
    {
        _currentPage = page;
        var view = EnsurePage(page);
        var wasVisible = view.Visibility == Visibility.Visible;
        foreach (var child in PageHost.Children.OfType<FrameworkElement>())
        {
            if (!ReferenceEquals(child, view))
            {
                child.Visibility = Visibility.Collapsed;
            }
        }

        view.Visibility = Visibility.Visible;
        TrimPageCache();
        MemoryTrace.Snapshot($"page-{page}");
        if (view is OverviewPage overview)
        {
            overview.RefreshLayout();
            UpdatePerformanceSamplingState();
        }
        if (wasVisible)
        {
            return;
        }

        view.Opacity = 0;
        var animation = new DoubleAnimation
        {
            To = 1,
            Duration = new Duration(TimeSpan.FromMilliseconds(160)),
            EnableDependentAnimation = true,
        };
        Storyboard.SetTarget(animation, view);
        Storyboard.SetTargetProperty(animation, nameof(UIElement.Opacity));
        var storyboard = new Storyboard();
        storyboard.Children.Add(animation);
        storyboard.Begin();
    }

    private void InitializePageHost() => ShowPage(_viewModel.IsLiteMode ? "devices" : "overview");

    private bool IsPageCurrent(string page) =>
        StringComparer.Ordinal.Equals(_currentPage, page);

    private void TouchPage(string page)
    {
        _pageUseOrder.Remove(page);
        _pageUseOrder.Add(page);
        MemoryTrace.PageCacheCount = _pageUseOrder.Count;
        MemoryTrace.PageCacheKeys = string.Join(',', _pageUseOrder);
    }

    private void TrimPageCache()
    {
        while (_pageUseOrder.Count > PageCacheLimit)
        {
            var oldest = _pageUseOrder.FirstOrDefault(page =>
                !StringComparer.Ordinal.Equals(page, _currentPage));
            if (oldest is null)
            {
                break;
            }

            ReleasePage(oldest);
        }
    }

    private void ReleasePage(string page)
    {
        FrameworkElement? view = page switch
        {
            "overview" => _overviewPage,
            "devices" => _devicesPage,
            "studio" => _lightingStudioPage,
            "profiles" => _profilesPage,
            "macros" => _macrosPage,
            "settings" => _settingsPage,
            "diagnostics" => _diagnosticsPage,
            _ => null,
        };
        if (view is not null)
        {
            if (view is DiagnosticsPage diagnostics)
            {
                // Clear the repeater's source while its native peer is still alive;
                // a removed ItemsRepeater keeps the subscription and fails later.
                diagnostics.Shutdown();
            }
            PageHost.Children.Remove(view);
            MemoryTrace.PageHostCount = PageHost.Children.Count;
            switch (view)
            {
                case OverviewPage overview:
                    overview.DataContext = null;
                    break;
                case DevicesPage devices:
                    devices.Shutdown();
                    break;
                case LightingStudioPage studio:
                    studio.Shutdown();
                    _studioInputSampler = null;
                    break;
                case SettingsPage settings:
                    settings.Shutdown();
                    break;
            }
        }

        switch (page)
        {
            case "overview":
                _overviewPage = null;
                break;
            case "devices":
                _devicesPage = null;
                break;
            case "studio":
                _lightingStudioPage = null;
                break;
            case "profiles":
                _profilesPage = null;
                break;
            case "macros":
                _macrosPage = null;
                break;
            case "settings":
                _settingsPage = null;
                break;
            case "diagnostics":
                _diagnosticsPage = null;
                break;
        }
        _pageUseOrder.Remove(page);
        MemoryTrace.PageCacheCount = _pageUseOrder.Count;
        MemoryTrace.PageCacheKeys = string.Join(',', _pageUseOrder);
    }
}
