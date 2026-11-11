using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media.Animation;
using OpenSynapse.App.Views;

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

    private OverviewPage OverviewPage => _overviewPage ??= CreateOverviewPage();
    private DevicesPage DevicesPage => _devicesPage ??= CreateDevicesPage();
    private LightingStudioPage LightingStudioPage => _lightingStudioPage ??= CreateLightingStudioPage();
    private ProfilesPage ProfilesPage => _profilesPage ??= CreateProfilesPage();
    private MacrosPage MacrosPage => _macrosPage ??= CreateMacrosPage();
    private SettingsPage SettingsPage => _settingsPage ??= CreateSettingsPage();
    private DiagnosticsPage DiagnosticsPage => _diagnosticsPage ??= CreateDiagnosticsPage();

    private OverviewPage CreateOverviewPage()
    {
        var page = new OverviewPage();
        page.DeviceRequested += OnOverviewDeviceRequested;
        return AddPage(page);
    }

    private DevicesPage CreateDevicesPage()
    {
        var page = new DevicesPage { Host = this };
        page.SetLiteMode(_viewModel.IsLiteMode);
        page.RefreshAdvancedFeaturesVisibility();
        page.RefreshLightingPresetOptions();
        return AddPage(page);
    }

    private LightingStudioPage CreateLightingStudioPage()
    {
        var page = new LightingStudioPage { Host = this };
        page.Initialize(
            _bladeLightingController,
            () => _viewModel.CurrentDeviceDescriptors,
            BuildStudioDevices,
            CreateStudioHidRouter(_studioInputSampler!),
            _studioInputSampler!,
            CreateStudioArgbRouter(_studioInputSampler!),
            _viewModel.RestoreLightingAfterExternalAsync);
        _studioDevicesDirty = true;
        return AddPage(page);
    }

    private ProfilesPage CreateProfilesPage()
    {
        var page = new ProfilesPage { Host = this };
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
        var page = new SettingsPage { Host = this };
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
        Localized.RefreshTree(page);
        return page;
    }

    private FrameworkElement EnsurePage(string page) => page switch
    {
        "devices" => DevicesPage,
        "studio" => LightingStudioPage,
        "profiles" => ProfilesPage,
        "macros" => MacrosPage,
        "settings" => SettingsPage,
        "diagnostics" => DiagnosticsPage,
        _ => OverviewPage,
    };

    private void ShowPage(string page)
    {
        var view = EnsurePage(page);
        var wasVisible = view.Visibility == Visibility.Visible;
        foreach (var child in PageHost.Children.OfType<FrameworkElement>())
        {
            if (!ReferenceEquals(child, view))
            {
                child.Visibility = Visibility.Collapsed;
            }
        }

        _currentPage = page;
        view.Visibility = Visibility.Visible;
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
}
