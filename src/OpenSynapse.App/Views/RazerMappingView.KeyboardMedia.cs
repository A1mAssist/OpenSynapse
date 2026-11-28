using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using OpenSynapse.App.ViewModels;
using OpenSynapse.Core.Devices;
using OpenSynapse.Core.Lighting;

namespace OpenSynapse.App.Views;

public sealed partial class RazerMappingView
{
    private void DrawKeyboardMediaButtons(RazerMappingKeyboardLayout layout)
    {
        if (Mapping is not { } mapping) return;
        foreach (var shape in layout.Visual.Paths.Where(path =>
            path.Role == StudioSvgRole.Selection && path.SelectionGroup == "multimedia"))
        {
            var ids = RazerKeyboardExtraKeyCatalog.MediaButtonsForShape(mapping.ProductId, shape.Id);
            if (ids.Count == 0 || StudioCanvas.ConvertGeometry(shape.Data) is not { } geometry) continue;
            var bounds = geometry.Bounds;
            for (var index = 0; index < ids.Count; index++)
            {
                var id = ids[index];
                var name = AppStrings.Text(RazerKeyboardExtraKeyCatalog.DescribeResourceKey(mapping.ProductId, id)!);
                var button = new Button
                {
                    Width = bounds.Width / ids.Count, Height = bounds.Height,
                    MinWidth = 0, MinHeight = 0, Padding = new Thickness(0),
                    Background = new SolidColorBrush(global::Windows.UI.Color.FromArgb(0, 0, 0, 0)),
                    BorderThickness = new Thickness(0), CornerRadius = new CornerRadius(bounds.Height / 2),
                };
                AutomationProperties.SetName(button, name);
                AutomationProperties.SetAutomationId(button, $"RazerMappingMedia{id:X2}");
                ToolTipService.SetToolTip(button, name);
                button.Click += (_, _) => mapping.SelectedRow = mapping.Rows.FirstOrDefault(row => row.ButtonId == id);
                Canvas.SetLeft(button, bounds.X + index * bounds.Width / ids.Count);
                Canvas.SetTop(button, bounds.Y);
                KeyboardCanvas.Children.Add(button);
            }
        }
    }
}
