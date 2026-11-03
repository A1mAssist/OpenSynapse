using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;
using OpenSynapse.Core.Lighting;
using Windows.UI;

namespace OpenSynapse.App.Views;

public sealed partial class StudioEffectInspector : UserControl
{
    private static readonly (string Hex, string LabelKey)[] Palette =
    [
        ("#44D62C", "LightingStudioColorRazerGreen"),
        ("#00D4D4", "LightingStudioColorCyan"),
        ("#0066FF", "LightingStudioColorBlue"),
        ("#8A2BE2", "LightingStudioColorPurple"),
        ("#FF2B2B", "LightingStudioColorRed"),
        ("#FF7A00", "LightingStudioColorOrange"),
        ("#FFD400", "LightingStudioColorYellow"),
        ("#FFFFFF", "LightingStudioColorWhite"),
    ];

    private StudioEffectParams? _parameters;

    public StudioEffectInspector()
    {
        InitializeComponent();
    }

    public StudioEffectParams? Parameters
    {
        get => _parameters;
        set
        {
            if (ReferenceEquals(_parameters, value)) return;
            _parameters = value;
            Refresh();
        }
    }

    public event EventHandler? Changed;

    public void Refresh()
    {
        if (Rows is null) return;
        Rows.Children.Clear();
        if (_parameters is not { } parameters) return;

        var descriptor = StudioEffectCatalog.Get(parameters.Effect);
        foreach (var field in descriptor.Fields)
        {
            if (BuildRow(field, descriptor) is { } row)
            {
                Rows.Children.Add(row);
            }
        }
    }

    private FrameworkElement? BuildRow(StudioParameterField field, StudioEffectDescriptor descriptor) => field switch
    {
        StudioParameterField.Color => BuildColorRow("LightingStudioParamColor", secondary: false),
        StudioParameterField.Color2 => BuildColorRow("LightingStudioParamColor2", secondary: true),
        StudioParameterField.ColorStops => BuildPaletteRow(),
        StudioParameterField.RandomColor => BuildToggle("LightingStudioParamRandomColor", nameof(StudioEffectParams.RandomColor)),
        StudioParameterField.Duration => BuildDurationRow(descriptor),
        StudioParameterField.Pause => BuildPauseRow(),
        StudioParameterField.Cycles => BuildCyclesRow(),
        StudioParameterField.PlaybackStart => BuildPlaybackRow("LightingStudioParamPlaybackStart", nameof(StudioEffectParams.PlaybackStart), Enum.GetValues<StudioPlaybackStart>()),
        StudioParameterField.PlaybackEnd => BuildPlaybackRow("LightingStudioParamPlaybackEnd", nameof(StudioEffectParams.PlaybackEnd), Enum.GetValues<StudioPlaybackEnd>()),
        StudioParameterField.Speed => BuildRangeSlider("LightingStudioParamSpeed", nameof(StudioEffectParams.Speed), descriptor.Ranges[StudioParameterField.Speed]),
        StudioParameterField.Width => BuildRangeSlider("LightingStudioParamWidth", nameof(StudioEffectParams.Width), descriptor.Ranges[StudioParameterField.Width]),
        StudioParameterField.Angle => BuildRangeSlider("LightingStudioParamAngle", nameof(StudioEffectParams.Angle), descriptor.Ranges[StudioParameterField.Angle], new RoundedIntConverter()),
        StudioParameterField.Split => BuildToggle("LightingStudioParamSplit", nameof(StudioEffectParams.Split), new SplitConverter()),
        StudioParameterField.Density => BuildRangeSlider("LightingStudioParamDensity", nameof(StudioEffectParams.Density), descriptor.Ranges[StudioParameterField.Density]),
        StudioParameterField.Boost => BuildRangeSlider("LightingStudioParamBoost", nameof(StudioEffectParams.Boost), descriptor.Ranges[StudioParameterField.Boost]),
        StudioParameterField.AutoBoost => BuildToggle("LightingStudioParamAutoBoost", nameof(StudioEffectParams.AutoBoost)),
        StudioParameterField.Decay => BuildRangeSlider("LightingStudioParamDecay", nameof(StudioEffectParams.Decay), descriptor.Ranges[StudioParameterField.Decay]),
        StudioParameterField.Screen => BuildScreenRow(),
        StudioParameterField.ScreenRect => BuildScreenRectRow(),
        StudioParameterField.Blur => BuildRangeSlider("LightingStudioParamBlur", nameof(StudioEffectParams.Blur), descriptor.Ranges[StudioParameterField.Blur], new RoundedIntConverter()),
        StudioParameterField.MediaSrc => BuildMediaSourceRow(),
        StudioParameterField.GenerateConfig => BuildGenerateConfigRow(),
        _ => null,
    };

    private FrameworkElement BuildColorRow(string labelKey, bool secondary)
    {
        var swatches = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
        foreach (var (hex, tooltipKey) in Palette)
        {
            var swatch = new Button
            {
                Width = 28,
                Height = 28,
                Padding = new Thickness(0),
                CornerRadius = new CornerRadius(6),
                BorderThickness = new Thickness(0),
                Background = ToBrush(hex),
            };
            var label = AppStrings.Text(tooltipKey);
            AutomationProperties.SetName(swatch, label);
            ToolTipService.SetToolTip(swatch, label);
            swatch.Click += (_, _) => SetColor(hex, secondary);
            swatches.Children.Add(swatch);
        }

        return Rowed(labelKey, swatches);
    }

    private FrameworkElement BuildPaletteRow()
    {
        var row = new StackPanel { Spacing = 6 };
        row.Children.Add(CreateLabel(AppStrings.Text("LightingStudioParamColorStops")));
        if (_parameters is null) return row;

        var strip = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
        var stops = _parameters.ColorStops;
        for (var index = 0; index < stops.Count; index++)
        {
            var stopIndex = index;
            var swatch = new Button
            {
                Width = 28,
                Height = 28,
                Padding = new Thickness(0),
                CornerRadius = new CornerRadius(6),
                BorderThickness = new Thickness(0),
                Background = ToBrush(stops[index].Color),
            };
            var picker = new ColorPicker
            {
                Color = ToColor(stops[index].Color),
                IsAlphaEnabled = false,
                IsColorChannelTextInputVisible = true,
            };
            picker.ColorChanged += (_, args) =>
            {
                if (stopIndex < _parameters.ColorStops.Count)
                {
                    _parameters.ColorStops[stopIndex] = new StudioColorStop(
                        _parameters.ColorStops[stopIndex].Stop,
                        StudioRgb.FromHex($"#{args.NewColor.R:X2}{args.NewColor.G:X2}{args.NewColor.B:X2}"));
                    _parameters.ColorStopsCustom = true;
                    swatch.Background = ToBrush(_parameters.ColorStops[stopIndex].Color);
                    RaiseChanged();
                }
            };
            swatch.Flyout = new Flyout { Content = picker };
            strip.Children.Add(swatch);
        }

        if (stops.Count < 10)
        {
            var add = new Button { Content = "+", Width = 28, Height = 28, Padding = new Thickness(0) };
            add.Click += (_, _) =>
            {
                var color = _parameters.ColorStops.Count == 0
                    ? _parameters.Primary
                    : _parameters.ColorStops[^1].Color;
                _parameters.ColorStops.Add(new StudioColorStop(100, color));
                _parameters.ColorStopsCustom = true;
                RaiseChanged();
                Refresh();
            };
            strip.Children.Add(add);
        }

        if (stops.Count > 1)
        {
            var remove = new Button { Content = "-", Width = 28, Height = 28, Padding = new Thickness(0) };
            remove.Click += (_, _) =>
            {
                _parameters.ColorStops.RemoveAt(_parameters.ColorStops.Count - 1);
                _parameters.ColorStopsCustom = true;
                RaiseChanged();
                Refresh();
            };
            strip.Children.Add(remove);
        }

        row.Children.Add(strip);
        var reset = new Button
        {
            Content = AppStrings.Text("LightingStudioResetPalette"),
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        reset.Click += (_, _) =>
        {
            var defaults = StudioEffectCatalog.CreateDefault(_parameters.Effect);
            _parameters.ColorStops.Clear();
            _parameters.ColorStops.AddRange(defaults.ColorStops);
            _parameters.ColorStopsCustom = defaults.ColorStopsCustom;
            RaiseChanged();
            Refresh();
        };
        row.Children.Add(reset);
        return row;
    }

    private FrameworkElement BuildToggle(string labelKey, string path, IValueConverter? converter = null)
    {
        var toggle = new ToggleSwitch { HorizontalAlignment = HorizontalAlignment.Stretch };
        Bind(toggle, ToggleSwitch.IsOnProperty, path, converter);
        toggle.Toggled += (_, _) => RaiseChanged();
        return Rowed(labelKey, toggle);
    }

    private FrameworkElement BuildDurationRow(StudioEffectDescriptor descriptor)
    {
        var values = descriptor.Effect switch
        {
            StudioEffectKind.Breathing => new[] { 28_000, 14_000, 6_000 },
            StudioEffectKind.Reactive => new[] { 2_000, 1_500, 500 },
            StudioEffectKind.Starlight => new[] { 3_000, 1_700, 500 },
            _ => null,
        };
        if (values is not null)
        {
            return Rowed("LightingStudioParamDuration", CreateDurationCombo(values));
        }

        return BuildRangeSlider(
            "LightingStudioParamDuration",
            nameof(StudioEffectParams.DurationMs),
            descriptor.Ranges[StudioParameterField.Duration],
            new RoundedIntConverter(),
            step: 1000);
    }

    private ComboBox CreateDurationCombo(IReadOnlyList<int> values)
    {
        var options = values
            .Select(value => new KeyValuePair<int, string>(value, FormatSeconds(value)))
            .ToList();
        if (_parameters is { } parameters && !values.Contains(parameters.DurationMs))
        {
            options.Add(new KeyValuePair<int, string>(parameters.DurationMs, FormatSeconds(parameters.DurationMs)));
        }

        var combo = new ComboBox
        {
            ItemsSource = options,
            DisplayMemberPath = "Value",
            SelectedValuePath = "Key",
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        Bind(combo, Selector.SelectedValueProperty, nameof(StudioEffectParams.DurationMs));
        combo.SelectionChanged += (_, _) => RaiseChanged();
        return combo;
    }

    private FrameworkElement BuildPauseRow()
    {
        var slider = new Slider
        {
            Minimum = 0,
            Maximum = 60,
            StepFrequency = 1,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        Bind(slider, RangeBase.ValueProperty, nameof(StudioEffectParams.PauseMs), new PauseSecondsConverter());
        slider.ValueChanged += (_, _) => RaiseChanged();
        return Rowed("LightingStudioParamPause", slider);
    }

    private FrameworkElement BuildCyclesRow()
    {
        var box = new NumberBox
        {
            Minimum = -1,
            Maximum = 100,
            SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        Bind(box, NumberBox.ValueProperty, nameof(StudioEffectParams.Cycles), new RoundedIntConverter());
        box.ValueChanged += (_, _) => RaiseChanged();
        return Rowed("LightingStudioParamCycles", box);
    }

    private FrameworkElement BuildPlaybackRow(string labelKey, string path, Array values)
    {
        var combo = new ComboBox
        {
            ItemsSource = values,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        Bind(combo, Selector.SelectedItemProperty, path);
        combo.SelectionChanged += (_, _) => RaiseChanged();
        return Rowed(labelKey, combo);
    }

    private FrameworkElement BuildScreenRow()
    {
        var options = new List<KeyValuePair<string, string>>
        {
            new("full", AppStrings.Text("LightingStudioScreenFull")),
            new("left", AppStrings.Text("LightingStudioScreenLeft")),
            new("top", AppStrings.Text("LightingStudioScreenTop")),
            new("right", AppStrings.Text("LightingStudioScreenRight")),
            new("bottom", AppStrings.Text("LightingStudioScreenBottom")),
            new("custom", AppStrings.Text("LightingStudioScreenCustom")),
        };
        var combo = new ComboBox
        {
            ItemsSource = options,
            DisplayMemberPath = "Value",
            SelectedValuePath = "Key",
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        Bind(combo, Selector.SelectedValueProperty, nameof(StudioEffectParams.Screen));
        combo.SelectionChanged += (_, _) => RaiseChanged();
        return Rowed("LightingStudioParamScreen", combo);
    }

    private FrameworkElement BuildScreenRectRow()
    {
        var row = new StackPanel { Spacing = 4 };
        row.Children.Add(CreateLabel(AppStrings.Text("LightingStudioParamScreenRect")));
        var rect = _parameters?.ScreenRect ?? new StudioScreenRect(0, 0, 1, 1);
        var x = UnitBox("X", rect.X);
        var y = UnitBox("Y", rect.Y);
        var width = UnitBox("W", rect.Width);
        var height = UnitBox("H", rect.Height);

        var syncing = false;
        ScreenRectPreview? preview = null;

        void Apply(StudioScreenRect value, bool syncBoxes)
        {
            if (_parameters is null) return;
            _parameters.ScreenRect = value;
            preview?.Render(value);
            if (syncBoxes)
            {
                syncing = true;
                x.Value = value.X;
                y.Value = value.Y;
                width.Value = value.Width;
                height.Value = value.Height;
                syncing = false;
            }

            RaiseChanged();
        }

        void ApplyBoxes()
        {
            if (syncing) return;
            Apply(
                new StudioScreenRect(UnitValue(x), UnitValue(y), UnitValue(width), UnitValue(height)),
                syncBoxes: false);
        }

        x.ValueChanged += (_, _) => ApplyBoxes();
        y.ValueChanged += (_, _) => ApplyBoxes();
        width.ValueChanged += (_, _) => ApplyBoxes();
        height.ValueChanged += (_, _) => ApplyBoxes();

        preview = new ScreenRectPreview(rect, value => Apply(value, syncBoxes: true));
        row.Children.Add(preview.View);

        var grid = new Grid { ColumnSpacing = 4, RowSpacing = 4 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.Children.Add(x);
        Grid.SetColumn(y, 1);
        grid.Children.Add(y);
        Grid.SetRow(width, 1);
        grid.Children.Add(width);
        Grid.SetRow(height, 1);
        Grid.SetColumn(height, 1);
        grid.Children.Add(height);
        row.Children.Add(grid);
        return row;
    }

    private static NumberBox UnitBox(string header, double value) => new()
    {
        Header = header,
        Minimum = 0,
        Maximum = 1,
        SmallChange = 0.05,
        SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact,
        Value = value,
        HorizontalAlignment = HorizontalAlignment.Stretch,
    };

    private FrameworkElement BuildRangeSlider(
        string labelKey,
        string path,
        StudioParameterRange range,
        IValueConverter? converter = null,
        double? step = null)
    {
        var slider = new Slider
        {
            Minimum = range.Minimum,
            Maximum = range.Maximum,
            StepFrequency = step ?? range.Step,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        Bind(slider, RangeBase.ValueProperty, path, converter);
        slider.ValueChanged += (_, _) => RaiseChanged();
        return Rowed(labelKey, slider);
    }

    private void Bind(
        FrameworkElement target,
        DependencyProperty property,
        string path,
        IValueConverter? converter = null,
        UpdateSourceTrigger trigger = UpdateSourceTrigger.Default)
    {
        if (_parameters is null) return;

        var binding = new Binding
        {
            Path = new PropertyPath(path),
            Source = _parameters,
            Mode = BindingMode.TwoWay,
            UpdateSourceTrigger = trigger,
        };
        if (converter is not null)
        {
            binding.Converter = converter;
        }

        target.SetBinding(property, binding);
    }

    private void SetColor(string hex, bool secondary)
    {
        if (_parameters is null) return;
        var color = StudioRgb.FromHex(hex);
        if (secondary)
        {
            _parameters.Secondary = color;
        }
        else
        {
            _parameters.Primary = color;
        }

        RaiseChanged();
    }

    private void RaiseChanged() => Changed?.Invoke(this, EventArgs.Empty);

    private static string FormatSeconds(int milliseconds) =>
        AppStrings.FormatText("LightingStudioSeconds", milliseconds / 1000d);

    private static StackPanel Rowed(string labelKey, FrameworkElement control)
    {
        var label = AppStrings.Text(labelKey);
        var row = new StackPanel { Spacing = 6 };
        row.Children.Add(CreateLabel(label));
        AutomationProperties.SetName(control, label);
        row.Children.Add(control);
        return row;
    }

    private static TextBlock CreateLabel(string text) => new()
    {
        Text = text,
        FontSize = 12,
        Foreground = MutedBrush,
    };

    private static Brush MutedBrush =>
        (Brush)Application.Current.Resources["MutedBrush"];

    private static SolidColorBrush ToBrush(string hex) => ToBrush(StudioRgb.FromHex(hex));

    private static Color ToColor(StudioRgb color) =>
        Color.FromArgb(255, color.R, color.G, color.B);

    private static SolidColorBrush ToBrush(StudioRgb color) =>
        new(Color.FromArgb(255, color.R, color.G, color.B));

    private sealed class PauseSecondsConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language) =>
            value is int milliseconds ? Math.Clamp(milliseconds / 1000d, 0, 60) : 0d;

        public object ConvertBack(object value, Type targetType, object parameter, string language) =>
            value is double seconds ? (int)Math.Round(Math.Clamp(seconds, 0, 60) * 1000) : 0;
    }

    private sealed class SplitConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language) =>
            value is double split && split >= 50;

        public object ConvertBack(object value, Type targetType, object parameter, string language) =>
            value is true ? 100d : 0d;
    }

    private sealed class RoundedIntConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language) =>
            value is int integer ? (double)integer : 0d;

        public object ConvertBack(object value, Type targetType, object parameter, string language) =>
            value is double number && !double.IsNaN(number) ? (int)Math.Round(number) : 0;
    }
}
