#if DEBUG
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using OpenSynapse.Core.Storage;

namespace OpenSynapse.App;

public partial class App
{
    private Window? _navigationPaneQaWindow;

    private bool TryLaunchNavigationPaneQa()
    {
        if (Environment.GetEnvironmentVariable("OPENSSYNAPSE_NAVIGATION_QA") != "1") return false;
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("OPENSSYNAPSE_UI_TEST_INSTANCE")))
            throw new InvalidOperationException("Navigation QA requires an isolated data instance.");
        _navigationPaneQaWindow = new MainWindow(AppBehaviorSettings.Load());
        _navigationPaneQaWindow.Activate();
        return true;
    }
}

public sealed partial class MainWindow
{
    // Load the production shell and resize handlers without discovery, hooks or hardware services.
    internal MainWindow(AppBehaviorSettings settings)
    {
        _viewModel = null!;
        _behaviorSettings = settings;
        _dispatcherQueue = DispatcherQueue.GetForCurrentThread()
            ?? throw new InvalidOperationException("Navigation QA requires a UI dispatcher.");
        _syncingDeviceNavigation = true;
        InitializeComponent();
        InitializeNavigationPaneResize();
        Title = "OpenSynapse Navigation QA";
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        LaunchVeil.Visibility = LaunchBackdrop.Visibility = LaunchContent.Visibility = Visibility.Collapsed;
        TitleBarLogo.Opacity = TitleBarTitleText.Opacity = TitleBarSubtitleText.Opacity = 1;
        RootNavigationView.IsPaneOpen = true;
        foreach (var name in new[] { "Blade 16 2025", "Viper V3 HyperSpeed", "Basilisk V3 Pro 35K", "USB Sound Card" })
            DevicesNavigationItem.MenuItems.Add(new NavigationViewItem
            {
                Content = new TextBlock { Text = name, TextTrimming = TextTrimming.CharacterEllipsis, MaxLines = 1 },
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                Icon = new SymbolIcon(Symbol.AllApps),
            });
        RootNavigationView.SelectedItem = DevicesNavigationItem.MenuItems[1];
        var panel = new StackPanel { Padding = new Thickness(24), Spacing = 16 };
        panel.Children.Add(new TextBlock { Text = "Viper V3 HyperSpeed", FontSize = 28 });
        var status = new TextBlock { Text = "READY" };
        AutomationProperties.SetAutomationId(status, "NavigationQaStatus");
        panel.Children.Add(status);
        void Resize(double width) => AppWindow.Resize(new global::Windows.Graphics.SizeInt32(
            (int)(width * RootLayout.XamlRoot.RasterizationScale), (int)(750 * RootLayout.XamlRoot.RasterizationScale)));
        void Add(string id, Action change, Func<bool> check)
        {
            var button = new Button { Content = id, Style = (Style)Application.Current.Resources["SecondaryButtonStyle"] };
            AutomationProperties.SetAutomationId(button, id);
            button.Click += async (_, _) =>
            {
                change();
                for (var attempt = 0; attempt < 30; attempt++)
                {
                    await Task.Delay(100);
                    if (check()) break;
                }
                status.Text = (check() ? "PASS " : "FAIL ") + id;
            };
            panel.Children.Add(button);
        }
        Add("Narrow", () => Resize(1000), () => RootNavigationView.OpenPaneLength < 360 && settings.NavigationPaneWidth == 360);
        Add("Wide", () => Resize(1280), () => RootNavigationView.OpenPaneLength == 360);
        Add("Collapse", () => RootNavigationView.IsPaneOpen = false, () => NavigationPaneResizer.Visibility == Visibility.Collapsed);
        Add("Expand", () => RootNavigationView.IsPaneOpen = true, () => NavigationPaneResizer.Visibility == Visibility.Visible);
        Add("Reload", () =>
        {
            settings.NavigationPaneWidth = AppBehaviorSettings.Load().NavigationPaneWidth;
            UpdateNavigationPaneWidth();
        }, () => RootNavigationView.OpenPaneLength == 360 && settings.NavigationPaneWidth == 360);
        PageHost.Children.Add(panel);
        Localized.RefreshTree(RootLayout);
        RootLayout.Loaded += (_, _) => Resize(1280);
        Closed += (_, _) => _lifetime.Dispose();
    }
}
#endif
