using System.Diagnostics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace OpenSynapse.App;

public sealed partial class MainWindow
{
    private bool _navigationPaneTransitioning;
    private bool _navigationPaneWidthDirty;

    private void InitializeNavigationPaneResize()
    {
        NavigationPaneResizer.WidthRequested += ResizeNavigationPane;
        NavigationPaneResizer.ResizeCompleted += (_, _) => SaveNavigationPaneWidth();
        RootNavigationView.Loaded += (_, _) => UpdateNavigationPaneWidth();
        RootNavigationView.SizeChanged += (_, _) => UpdateNavigationPaneWidth();
        RootNavigationView.DisplayModeChanged += (_, _) => UpdateNavigationPaneWidth();
        RootNavigationView.PaneOpening += (_, _) => SetNavigationPaneTransition(true);
        RootNavigationView.PaneClosing += (_, _) => SetNavigationPaneTransition(true);
        RootNavigationView.PaneOpened += (_, _) => SetNavigationPaneTransition(false);
        RootNavigationView.PaneClosed += (_, _) => SetNavigationPaneTransition(false);
        Closed += (_, _) => SaveNavigationPaneWidth();
    }

    private void SetNavigationPaneTransition(bool transitioning)
    {
        _navigationPaneTransitioning = transitioning;
        UpdateNavigationPaneWidth();
    }

    private static double MaximumNavigationPaneWidth(double availableWidth) =>
        // Reserve the device content, its 24-DIP side padding, and navigation borders.
        Math.Clamp(availableWidth - (640 + 24 + 24 + 2),
            NavigationPaneResizeThumb.MinimumPaneWidth, NavigationPaneResizeThumb.MaximumPaneWidth);

    private void UpdateNavigationPaneWidth()
    {
        if (RootNavigationView.ActualWidth <= 0) return;
        var maximum = MaximumNavigationPaneWidth(RootNavigationView.ActualWidth);
        var width = Math.Clamp(_behaviorSettings.NavigationPaneWidth,
            NavigationPaneResizeThumb.MinimumPaneWidth, maximum);
        // A temporary window constraint must not overwrite the user's preferred width.
        RootNavigationView.OpenPaneLength = width;
        NavigationPaneResizer.UpdateRange(width, maximum);
        NavigationPaneResizer.Margin = new Thickness(width - NavigationPaneResizer.Width / 2, 0, 0, 0);
        var visible = RootNavigationView.IsPaneOpen &&
            RootNavigationView.DisplayMode == NavigationViewDisplayMode.Expanded &&
            RootNavigationView.ActualWidth >= NavigationPaneResizeThumb.MinimumPaneWidth + 690 &&
            !_navigationPaneTransitioning;
        if (!visible && NavigationPaneResizer.IsDragging) NavigationPaneResizer.CancelDrag();
        NavigationPaneResizer.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        Debug.Assert(width >= 224 && width <= 480);
        Debug.Assert(!visible || RootNavigationView.ActualWidth - width >= 690);
    }

    private void ResizeNavigationPane(double requestedWidth)
    {
        var width = (int)Math.Clamp(Math.Round(requestedWidth),
            NavigationPaneResizeThumb.MinimumPaneWidth,
            MaximumNavigationPaneWidth(RootNavigationView.ActualWidth));
        if (_behaviorSettings.NavigationPaneWidth == width) return;
        _behaviorSettings.NavigationPaneWidth = width;
        _navigationPaneWidthDirty = true;
        UpdateNavigationPaneWidth();
    }

    private void SaveNavigationPaneWidth()
    {
        if (!_navigationPaneWidthDirty) return;
        try
        {
            _behaviorSettings.Save();
            _navigationPaneWidthDirty = false;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or
            InvalidOperationException or System.Security.SecurityException)
        {
            _diagnosticLog.TryWrite("navigation", $"Navigation width was not saved: {exception.Message}");
        }
    }
}
