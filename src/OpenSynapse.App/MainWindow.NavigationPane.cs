using System.Diagnostics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Foundation;

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

    private double MaximumNavigationPaneWidth(double availableWidth) =>
        // Inline panes reserve content space; overlay panes only need an outside dismissal area.
        Math.Max(availableWidth - (RootNavigationView.DisplayMode == NavigationViewDisplayMode.Expanded
            ? 640 + 24 + 24 + 2 : RootNavigationView.CompactPaneLength), NavigationPaneResizeThumb.MinimumPaneWidth);

    private double MeasureNavigationPaneWidth()
    {
        var width = NavigationPaneResizeThumb.MinimumPaneWidth;
        foreach (var item in DevicesNavigationItem.MenuItems.OfType<NavigationViewItem>())
        {
            if (item.Content is not TextBlock label) continue;
            var measure = new TextBlock
            {
                Text = label.Text, FontFamily = label.FontFamily, FontSize = label.FontSize,
                FontWeight = label.FontWeight, FontStyle = label.FontStyle,
                FontStretch = label.FontStretch, CharacterSpacing = label.CharacterSpacing,
                IsTextScaleFactorEnabled = label.IsTextScaleFactorEnabled,
            };
            measure.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            // Nested rows use 106 DIP for indent, icon and padding; leave 2 DIP for layout rounding.
            width = Math.Max(width, Math.Ceiling(measure.DesiredSize.Width) + 108);
        }
        return width;
    }

    private void UpdateNavigationPaneWidth()
    {
        if (RootNavigationView.ActualWidth <= 0) return;
        var maximum = MaximumNavigationPaneWidth(RootNavigationView.ActualWidth);
        var preferredWidth = _behaviorSettings.NavigationPaneWidthCustomized
            ? _behaviorSettings.NavigationPaneWidth
            : Math.Max(_behaviorSettings.NavigationPaneWidth, MeasureNavigationPaneWidth());
        var width = Math.Clamp(preferredWidth,
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
        Debug.Assert(width >= NavigationPaneResizeThumb.MinimumPaneWidth && width <= maximum);
        Debug.Assert(!visible || RootNavigationView.ActualWidth - width >= 690);
    }

    private void ResizeNavigationPane(double requestedWidth)
    {
        var width = (int)Math.Clamp(Math.Round(requestedWidth),
            NavigationPaneResizeThumb.MinimumPaneWidth,
            MaximumNavigationPaneWidth(RootNavigationView.ActualWidth));
        if (_behaviorSettings.NavigationPaneWidth == width && _behaviorSettings.NavigationPaneWidthCustomized) return;
        _behaviorSettings.NavigationPaneWidth = width;
        _behaviorSettings.NavigationPaneWidthCustomized = true;
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
