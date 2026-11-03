using System.Globalization;
using System.Text.Json.Nodes;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Data;
using OpenSynapse.Core.Lighting;
using Windows.Storage.Pickers;

namespace OpenSynapse.App.Views;

public sealed partial class StudioEffectInspector
{
    private static readonly string[] Patterns =
        Enumerable.Range(1, 9).Select(value => value.ToString(CultureInfo.InvariantCulture)).ToArray();

    private FrameworkElement BuildMediaSourceRow()
    {
        var box = new TextBox
        {
            Header = AppStrings.Text("LightingStudioParamMediaSrc"),
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        Bind(box, TextBox.TextProperty, nameof(StudioEffectParams.MediaSrc),
            trigger: UpdateSourceTrigger.PropertyChanged);
        box.TextChanged += (_, _) => RaiseChanged();

        var browse = new Button
        {
            Content = AppStrings.Text("LightingStudioBrowse"),
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        browse.Click += async (_, _) =>
        {
            var parameters = _parameters;
            if (parameters is null ||
                XamlRoot?.ContentIslandEnvironment is not { } environment) return;

            var picker = new FileOpenPicker
            {
                SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
            };
            picker.FileTypeFilter.Add("*");
            WinRT.Interop.InitializeWithWindow.Initialize(
                picker,
                Microsoft.UI.Win32Interop.GetWindowFromWindowId(environment.AppWindowId));

            var file = await picker.PickSingleFileAsync();
            if (file is null || string.IsNullOrWhiteSpace(file.Path) ||
                !ReferenceEquals(_parameters, parameters)) return;

            parameters.MediaSrc = file.Path;
            box.Text = file.Path;
        };

        var row = new StackPanel { Spacing = 6 };
        row.Children.Add(box);
        row.Children.Add(browse);
        return row;
    }

    private FrameworkElement BuildGenerateConfigRow()
    {
        var config = Normalize(StudioGenerateConfig.Parse(_parameters?.CurrentConfigJson));
        var panel = new StackPanel { Spacing = 10 };

        var offsetX = NumberField(AppStrings.Text("LightingStudioGenerateOffsetX"), config.OffsetX, 0, 10, 0.1);
        var offsetY = NumberField(AppStrings.Text("LightingStudioGenerateOffsetY"), config.OffsetY, 0, 10, 0.1);
        var rotation = NumberField(AppStrings.Text("LightingStudioGenerateRotation"), config.Rotation, -180, 180, 1);
        var scaleX = NumberField(AppStrings.Text("LightingStudioGenerateScaleX"), config.ScaleX, 0.1, 10, 0.1);
        var scaleY = NumberField(AppStrings.Text("LightingStudioGenerateScaleY"), config.ScaleY, 0.1, 10, 0.1);

        var mirrorX = Toggle(AppStrings.Text("LightingStudioGenerateMirrorX"), config.MirrorX);
        var mirrorY = Toggle(AppStrings.Text("LightingStudioGenerateMirrorY"), config.MirrorY);
        var flipX = Toggle(AppStrings.Text("LightingStudioGenerateFlipX"), config.FlipX);
        var flipY = Toggle(AppStrings.Text("LightingStudioGenerateFlipY"), config.FlipY);

        var pattern = new ComboBox
        {
            Header = AppStrings.Text("LightingStudioGeneratePattern"),
            ItemsSource = Patterns,
            SelectedItem = config.Pattern.ToString(CultureInfo.InvariantCulture),
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        var blend = new ComboBox
        {
            Header = AppStrings.Text("LightingStudioGenerateBlend"),
            ItemsSource = Enum.GetValues<StudioGenerateBlend>(),
            SelectedItem = config.Blend,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        var spread = NumberField(AppStrings.Text("LightingStudioGenerateSpread"), config.PatternSpread, 1, 50, 0.1);
        var speed = NumberField(AppStrings.Text("LightingStudioGenerateSpeed"), config.PatternSpeed, -10, 10, 0.1);

        void Commit(Func<StudioGenerateConfig, StudioGenerateConfig> update)
        {
            if (_parameters is null) return;
            _parameters.CurrentConfigJson = SerializeConfig(
                Normalize(update(StudioGenerateConfig.Parse(_parameters.CurrentConfigJson))));
            RaiseChanged();
        }

        offsetX.ValueChanged += (_, _) => Commit(current => current with
        {
            OffsetX = ReadNumber(offsetX, current.OffsetX, 0, 10),
        });
        offsetY.ValueChanged += (_, _) => Commit(current => current with
        {
            OffsetY = ReadNumber(offsetY, current.OffsetY, 0, 10),
        });
        rotation.ValueChanged += (_, _) => Commit(current => current with
        {
            Rotation = ReadNumber(rotation, current.Rotation, -180, 180),
        });
        scaleX.ValueChanged += (_, _) => Commit(current => current with
        {
            ScaleX = ReadNumber(scaleX, current.ScaleX, 0.1, 10),
        });
        scaleY.ValueChanged += (_, _) => Commit(current => current with
        {
            ScaleY = ReadNumber(scaleY, current.ScaleY, 0.1, 10),
        });
        spread.ValueChanged += (_, _) => Commit(current => current with
        {
            PatternSpread = ReadNumber(spread, current.PatternSpread, 1, 50),
        });
        speed.ValueChanged += (_, _) => Commit(current => current with
        {
            PatternSpeed = ReadNumber(speed, current.PatternSpeed, -10, 10),
        });

        mirrorX.Click += (_, _) => Commit(current => current with { MirrorX = mirrorX.IsChecked == true });
        mirrorY.Click += (_, _) => Commit(current => current with { MirrorY = mirrorY.IsChecked == true });
        flipX.Click += (_, _) => Commit(current => current with { FlipX = flipX.IsChecked == true });
        flipY.Click += (_, _) => Commit(current => current with { FlipY = flipY.IsChecked == true });

        pattern.SelectionChanged += (_, _) =>
        {
            if (pattern.SelectedItem is string text &&
                int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
            {
                Commit(current => current with { Pattern = value });
            }
        };
        blend.SelectionChanged += (_, _) =>
        {
            if (blend.SelectedItem is StudioGenerateBlend value)
            {
                Commit(current => current with { Blend = value });
            }
        };

        panel.Children.Add(CreateLabel(AppStrings.Text("LightingStudioGenerateTransform")));
        panel.Children.Add(GridOf(3, offsetX, offsetY, rotation, scaleX, scaleY));
        panel.Children.Add(CreateLabel(AppStrings.Text("LightingStudioGenerateMirrorFlip")));
        panel.Children.Add(GridOf(2, mirrorX, mirrorY, flipX, flipY));
        panel.Children.Add(CreateLabel(AppStrings.Text("LightingStudioGeneratePatternSection")));
        panel.Children.Add(GridOf(2, pattern, blend, spread, speed));

        var ai = new CheckBox
        {
            Content = AppStrings.Text("LightingStudioGenerateAi"),
            IsChecked = false,
            IsEnabled = false,
        };
        var color = new StackPanel { Spacing = 6 };
        color.Children.Add(CreateLabel(AppStrings.Text("LightingStudioGenerateColorSource")));
        color.Children.Add(ai);
        panel.Children.Add(color);
        return panel;
    }

    private static NumberBox NumberField(
        string header,
        double value,
        double minimum,
        double maximum,
        double step) => new()
    {
        Header = header,
        Minimum = minimum,
        Maximum = maximum,
        SmallChange = step,
        LargeChange = step * 10,
        SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact,
        Value = double.IsFinite(value) ? Math.Clamp(value, minimum, maximum) : minimum,
        HorizontalAlignment = HorizontalAlignment.Stretch,
    };

    private static CheckBox Toggle(string label, bool value) => new()
    {
        Content = label,
        IsChecked = value,
    };

    private static Grid GridOf(int columns, params FrameworkElement[] children)
    {
        var grid = new Grid { ColumnSpacing = 8, RowSpacing = 6 };
        var rows = (children.Length + columns - 1) / columns;
        for (var column = 0; column < columns; column++)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition
            {
                Width = new GridLength(1, GridUnitType.Star),
            });
        }
        for (var row = 0; row < rows; row++)
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        }
        for (var index = 0; index < children.Length; index++)
        {
            Grid.SetColumn(children[index], index % columns);
            Grid.SetRow(children[index], index / columns);
            grid.Children.Add(children[index]);
        }

        return grid;
    }

    private static double ReadNumber(NumberBox box, double fallback, double minimum, double maximum) =>
        double.IsFinite(box.Value) ? Math.Clamp(box.Value, minimum, maximum) : fallback;

    private static StudioGenerateConfig Normalize(StudioGenerateConfig config) => config with
    {
        OffsetX = Math.Clamp(config.OffsetX, 0, 10),
        OffsetY = Math.Clamp(config.OffsetY, 0, 10),
        Rotation = Math.Clamp(config.Rotation, -180, 180),
        ScaleX = Math.Clamp(config.ScaleX, 0.1, 10),
        ScaleY = Math.Clamp(config.ScaleY, 0.1, 10),
        PatternSpread = Math.Clamp(config.PatternSpread, 1, 50),
        PatternSpeed = Math.Clamp(config.PatternSpeed, -10, 10),
        Pattern = Math.Clamp(config.Pattern, 1, 9),
    };

    private static string SerializeConfig(StudioGenerateConfig config)
    {
        var json = new JsonObject
        {
            // The AI generator has no local model yet; keep the native selector procedural.
            ["colorSource"] = "procedural",
            ["offsetX"] = Round(config.OffsetX),
            ["offsetY"] = Round(config.OffsetY),
            ["rotation"] = Round(config.Rotation),
            // ScaleX/ScaleY already include the parsed multiplier, so emit scale as 1.
            ["scale"] = 1,
            ["scaleX"] = Round(config.ScaleX),
            ["scaleY"] = Round(config.ScaleY),
            ["mirrorX"] = config.MirrorX,
            ["mirrorY"] = config.MirrorY,
            ["flipX"] = config.FlipX,
            ["flipY"] = config.FlipY,
            ["patternSpread"] = Round(config.PatternSpread),
            ["patternSpeed"] = Round(config.PatternSpeed),
            ["blend"] = config.Blend.ToString(),
            ["pattern"] = config.Pattern.ToString(CultureInfo.InvariantCulture),
        };
        return json.ToJsonString();
    }

    private static double Round(double value) =>
        double.IsFinite(value) ? Math.Round(value, 3) : 0;

}
