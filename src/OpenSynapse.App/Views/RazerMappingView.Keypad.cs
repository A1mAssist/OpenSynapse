using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;
using OpenSynapse.App.ViewModels;
using Windows.Foundation;
using Windows.UI;

namespace OpenSynapse.App.Views;

public sealed partial class RazerMappingView
{
    private void KeypadWidgetClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Content: RazerHidMappingRowViewModel row } && Mapping is { } mapping)
        {
            mapping.SelectedRow = row;
        }
    }

    private void UpdateKeypadWidgets()
    {
        if (Mapping is not { } mapping)
        {
            return;
        }

        BindKeypadButton(KeypadScrollUpButton, mapping, 0x09);
        BindKeypadButton(KeypadScrollClickButton, mapping, 0x03);
        BindKeypadButton(KeypadScrollDownButton, mapping, 0x0A);
        BindKeypadButton(KeypadDpadUpButton, mapping, 0x2C);
        BindKeypadButton(KeypadDpadDownButton, mapping, 0x2D);
        BindKeypadButton(KeypadDpadLeftButton, mapping, 0x2E);
        BindKeypadButton(KeypadDpadRightButton, mapping, 0x2F);
        UpdateKeypadWidgetSelection();
    }

    private static void BindKeypadButton(Button button, RazerHidMappingViewModel mapping, byte buttonId)
    {
        var row = mapping.Rows.FirstOrDefault(item => item.ButtonId == buttonId);
        button.Content = row;
        button.Tag = row;
        button.Visibility = row is null ? Visibility.Collapsed : Visibility.Visible;
        button.SetBinding(AutomationProperties.NameProperty, new Binding
        {
            Source = row, Path = new PropertyPath(nameof(RazerHidMappingRowViewModel.ButtonName)),
        });
        button.SetBinding(ToolTipService.ToolTipProperty, new Binding
        {
            Source = row, Path = new PropertyPath(nameof(RazerHidMappingRowViewModel.CurrentActionText)),
        });
    }

    private void UpdateKeypadWidgetSelection()
    {
        if (Mapping is not { } mapping)
        {
            return;
        }

        var accent = Application.Current.Resources["AccentBrush"] as Brush ??
            new SolidColorBrush(Color.FromArgb(255, 0x44, 0xD6, 0x2C));
        var accentColor = accent is SolidColorBrush solid
            ? solid.Color
            : Color.FromArgb(255, 0x44, 0xD6, 0x2C);
        var line = Application.Current.Resources["LineBrush"] as Brush;
        foreach (var button in KeypadWidgetButtons())
        {
            var selected = button.Content is RazerHidMappingRowViewModel row &&
                ReferenceEquals(row, mapping.SelectedRow);
            button.BorderBrush = selected ? accent : line;
            button.BorderThickness = new Thickness(selected ? 2 : 1);
            button.Background = selected
                ? new SolidColorBrush(Color.FromArgb(36, accentColor.R, accentColor.G, accentColor.B))
                : new SolidColorBrush(Color.FromArgb(0, 0, 0, 0));
        }

        DrawKeypadConnectors();
    }

    private void DrawKeypadConnectors()
    {
        KeypadConnectorCanvas.Children.Clear();
        if (Mapping is not { KeypadWidgetsVisibility: Visibility.Visible } mapping)
        {
            return;
        }

        var accent = Application.Current.Resources["AccentBrush"] as Brush ??
            new SolidColorBrush(Color.FromArgb(255, 0x44, 0xD6, 0x2C));
        var selected = mapping.SelectedRow?.ButtonId;
        AddKeypadConnector(KeypadScrollUpButton, 0x09, new Point(195, 40), new Point(165, 40),
            new Point(138, 72), accent, selected);
        AddKeypadConnector(KeypadScrollClickButton, 0x03, new Point(195, 94), new Point(165, 94),
            new Point(138, 94), accent, selected);
        AddKeypadConnector(KeypadScrollDownButton, 0x0A, new Point(195, 148), new Point(165, 148),
            new Point(138, 116), accent, selected);
        AddKeypadConnector(KeypadDpadUpButton, 0x2C, new Point(195, 318), new Point(195, 324),
            new Point(195, 330), accent, selected);
        AddKeypadConnector(KeypadDpadDownButton, 0x2D, new Point(195, 393), new Point(195, 387),
            new Point(195, 380), accent, selected);
        AddKeypadConnector(KeypadDpadLeftButton, 0x2E, new Point(132, 355), new Point(151, 355),
            new Point(170, 355), accent, selected);
        AddKeypadConnector(KeypadDpadRightButton, 0x2F, new Point(258, 355), new Point(239, 355),
            new Point(220, 355), accent, selected);
    }

    private void AddKeypadConnector(
        Button button,
        byte buttonId,
        Point start,
        Point corner,
        Point target,
        Brush accent,
        byte? selected)
    {
        if (button.Visibility != Visibility.Visible)
        {
            return;
        }

        var isSelected = selected == buttonId;
        AddConnector(KeypadConnectorCanvas, start, corner, target, accent, isSelected ? 1 : 0.4);
        AddDot(KeypadConnectorCanvas, target, isSelected ? 7 : 5, accent, isSelected ? 1 : 0.5);
    }

    private Button[] KeypadWidgetButtons() =>
    [
        KeypadScrollUpButton,
        KeypadScrollClickButton,
        KeypadScrollDownButton,
        KeypadDpadUpButton,
        KeypadDpadDownButton,
        KeypadDpadLeftButton,
        KeypadDpadRightButton,
    ];
}
