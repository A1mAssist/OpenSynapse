using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using OpenSynapse.Core.Lighting;

namespace OpenSynapse.App.Views;

public sealed partial class StudioEffectInspector
{
    private FrameworkElement BuildColorEditor(StudioRgb initial, Action<StudioRgb> apply)
    {
        var preview = new Border
        {
            Height = 34,
            CornerRadius = new CornerRadius(6),
            BorderThickness = new Thickness(1),
            BorderBrush = (Brush)Application.Current.Resources["LineStrongBrush"],
            Background = ToBrush(initial),
        };
        NumberBox ChannelBox(string header, byte value) => new()
        {
            Header = header,
            Value = value,
            Minimum = 0,
            Maximum = 255,
            SmallChange = 1,
            SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Hidden,
            Width = 72,
        };
        var red = ChannelBox("R", initial.R);
        var green = ChannelBox("G", initial.G);
        var blue = ChannelBox("B", initial.B);
        AutomationProperties.SetAutomationId(red, "StudioColorR");
        AutomationProperties.SetAutomationId(green, "StudioColorG");
        AutomationProperties.SetAutomationId(blue, "StudioColorB");
        var channels = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        channels.Children.Add(red);
        channels.Children.Add(green);
        channels.Children.Add(blue);
        var hex = new TextBox
        {
            Text = initial.ToHex(),
            PlaceholderText = "#RRGGBB",
            Width = 132,
        };
        AutomationProperties.SetAutomationId(hex, "StudioColorHex");
        var updating = false;
        void Apply(StudioRgb color, bool updateFields)
        {
            if (updating) return;
            updating = true;
            try
            {
                if (updateFields)
                {
                    red.Value = color.R;
                    green.Value = color.G;
                    blue.Value = color.B;
                }
                hex.Text = color.ToHex();
                preview.Background = ToBrush(color);
            }
            finally
            {
                updating = false;
            }

            apply(color);
        }
        void ApplyFields()
        {
            if (updating) return;
            Apply(new StudioRgb(
                (byte)Math.Clamp(double.IsNaN(red.Value) ? 0 : red.Value, 0, 255),
                (byte)Math.Clamp(double.IsNaN(green.Value) ? 0 : green.Value, 0, 255),
                (byte)Math.Clamp(double.IsNaN(blue.Value) ? 0 : blue.Value, 0, 255)), updateFields: false);
        }
        red.ValueChanged += (_, _) => ApplyFields();
        green.ValueChanged += (_, _) => ApplyFields();
        blue.ValueChanged += (_, _) => ApplyFields();
        hex.TextChanged += (_, _) =>
        {
            if (updating) return;
            var text = hex.Text.Trim();
            if (text.StartsWith('#')) text = text[1..];
            if (text.Length != 6 ||
                !int.TryParse(text, System.Globalization.NumberStyles.HexNumber, null, out _))
            {
                return;
            }
            Apply(StudioRgb.FromHex(text), updateFields: true);
        };
        var content = new StackPanel { Width = 248, Spacing = 8 };
        content.Children.Add(preview);
        content.Children.Add(channels);
        content.Children.Add(hex);
        return content;
    }
}
