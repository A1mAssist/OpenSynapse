using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace OpenSynapse.App;

public sealed record TrayPerformanceMode(int Index, string Label, string Glyph, bool Selected);

public sealed partial class TrayMenuWindow
{
    private const double PerformanceRowHeightDip = 30;
    private readonly List<Button> _performanceRows = [];
    private readonly List<TrayPerformanceMode> _performanceModes = [];

    /// <summary>Modes to show; empty hides the panel, including a disconnected Blade.</summary>
    public Func<IReadOnlyList<TrayPerformanceMode>>? PerformanceSnapshot { get; set; }

    /// <summary>Applies one mode index. Returns false when the device kept the old mode.</summary>
    public Func<int, CancellationToken, Task<bool>>? PerformanceApplier { get; set; }

    private void PopulatePerformanceRows(IReadOnlyList<TrayPerformanceMode> modes)
    {
        PerformancePanel.Children.Clear();
        _performanceRows.Clear();
        _performanceModes.Clear();
        _performanceModes.AddRange(modes);
        if (modes.Count == 0)
        {
            PerformancePanel.Visibility = Visibility.Collapsed;
            PerformanceSeparator.Visibility = Visibility.Collapsed;
            return;
        }

        foreach (var mode in modes)
        {
            var icon = new Viewbox
            {
                Width = 16,
                Height = 16,
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Center,
                Child = new FontIcon
                {
                    Glyph = mode.Glyph,
                    FontFamily = (FontFamily)Application.Current.Resources["IconFontFamily"],
                    FontSize = 14,
                },
            };
            var label = new TextBlock
            {
                Text = mode.Label,
                FontSize = 13,
                FontFamily = new FontFamily("Microsoft YaHei UI,Segoe UI"),
                Margin = new Thickness(12, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
            };
            var check = new FontIcon
            {
                Glyph = "\uE8FB",
                FontFamily = (FontFamily)Application.Current.Resources["IconFontFamily"],
                FontSize = 12,
                Margin = new Thickness(0, 0, 8, 0),
                Visibility = mode.Selected ? Visibility.Visible : Visibility.Collapsed,
                VerticalAlignment = VerticalAlignment.Center,
            };
            var layout = new Grid();
            layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16) });
            layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            layout.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            layout.Children.Add(icon);
            Grid.SetColumn(label, 1);
            layout.Children.Add(label);
            Grid.SetColumn(check, 2);
            layout.Children.Add(check);

            var row = new Button
            {
                Style = (Style)MenuRoot.Resources["TrayMenuButtonStyle"],
                Height = PerformanceRowHeightDip,
                Tag = mode.Index,
                Content = layout,
            };
            AutomationProperties.SetName(row, mode.Label);
            row.Click += PerformanceRowClick;
            PerformancePanel.Children.Add(row);
            _performanceRows.Add(row);
        }

        PerformancePanel.Visibility = Visibility.Visible;
        PerformanceSeparator.Visibility = Visibility.Visible;
    }

    private async void PerformanceRowClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: int index } || PerformanceApplier is null)
        {
            return;
        }

        SetPerformanceBusy(true);
        try
        {
            await PerformanceApplier(index, CancellationToken.None);
        }
        catch (Exception)
        {
            // ApplyPerformanceModeAsync reports device errors; keep the menu alive.
        }
        finally
        {
            SetPerformanceBusy(false);
        }

        PopulatePerformanceRows(PerformanceSnapshot?.Invoke() ?? []);
    }

    private void SetPerformanceBusy(bool busy)
    {
        foreach (var row in _performanceRows)
        {
            row.IsEnabled = !busy;
        }
    }
}
