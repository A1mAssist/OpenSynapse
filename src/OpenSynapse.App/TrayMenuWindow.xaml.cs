using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using OpenSynapse.App.ViewModels;
using Windows.Graphics;

namespace OpenSynapse.App;

public sealed partial class TrayMenuWindow : Window
{
    private bool _showPending;
    private bool _closing;

    public TrayMenuWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        MenuAnchor.RequestedTheme = ElementTheme.Dark;
        MenuAnchor.DataContext = viewModel;
        AppWindow.IsShownInSwitchers = false;
        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.SetBorderAndTitleBar(false, false);
            presenter.IsResizable = false;
            presenter.IsMaximizable = false;
            presenter.IsMinimizable = false;
        }
    }

    public event Action? ShowRequested;

    public event Action? ExitRequested;

    public event Action<string>? NavigationRequested;

    public event Action<bool>? StartupChangeRequested;

    public void ShowAt(int x, int y)
    {
        if (_closing || _showPending || TrayMenuFlyout.IsOpen)
        {
            return;
        }

        _showPending = true;
        var workArea = DisplayArea.GetFromPoint(
            new PointInt32(x, y),
            DisplayAreaFallback.Primary).WorkArea;
        AppWindow.MoveAndResize(new RectInt32(x, workArea.Y + workArea.Height, 2, 2));
        Activate();
        if (!MenuAnchor.DispatcherQueue.TryEnqueue(() =>
            {
                _showPending = false;
                TrayMenuFlyout.ShowAt(MenuAnchor);
            }))
        {
            _showPending = false;
            AppWindow.Hide();
        }
    }

    private void OpenMainPanelClick(object sender, RoutedEventArgs e) => ShowRequested?.Invoke();

    private void NavigateClick(object sender, RoutedEventArgs e)
    {
        if (sender is MenuFlyoutItem { Tag: string page })
        {
            NavigationRequested?.Invoke(page);
        }
    }

    private void StartupClick(object sender, RoutedEventArgs e)
    {
        if (sender is ToggleMenuFlyoutItem item)
        {
            StartupChangeRequested?.Invoke(item.IsChecked);
        }
    }

    private void ExitClick(object sender, RoutedEventArgs e) => ExitRequested?.Invoke();

    public void CloseMenuHost()
    {
        _closing = true;
        TrayMenuFlyout.Hide();
        Close();
    }

    private void TrayMenuClosed(object sender, object e)
    {
        if (!_closing)
        {
            AppWindow.Hide();
        }
    }

}
