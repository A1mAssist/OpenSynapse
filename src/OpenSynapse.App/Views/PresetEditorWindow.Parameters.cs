using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using OpenSynapse.App.ViewModels;
using OpenSynapse.Core.Lighting;
using Windows.Foundation;
using Windows.UI;

namespace OpenSynapse.App.Views;

public sealed partial class PresetEditorWindow
{
    private bool _syncingParameters;

    private void ModulePickerClick(object sender, RoutedEventArgs e)
    {
        var search = new TextBox
        {
            PlaceholderText = PresetEditorText.Get("PresetEditorModuleSearch", "Search modules"),
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        AutomationProperties.SetAutomationId(search, "PresetEditorModuleSearch");
        var advanced = new ToggleSwitch
        {
            Header = PresetEditorText.Get("PresetEditorModuleAdvanced", "Show all modules"),
            OnContent = string.Empty,
            OffContent = string.Empty,
            MinWidth = 52,
        };
        var advancedHint = new TextBlock
        {
            Text = PresetEditorText.Get("PresetEditorModuleAdvancedHint",
                "Advanced mode shows every module, even those outside this preset kind."),
            FontSize = 11,
            Foreground = (Brush)Application.Current.Resources["MutedBrush"],
            TextWrapping = TextWrapping.Wrap,
            Visibility = Visibility.Collapsed,
        };
        var modulesPanel = new StackPanel { Spacing = 8 };
        var flyout = new Flyout
        {
            Placement = FlyoutPlacementMode.BottomEdgeAlignedLeft,
            Content = new ScrollViewer
            {
                Content = new StackPanel
                {
                    Width = 348,
                    Spacing = 8,
                    Children =
                    {
                        search,
                        advanced,
                        advancedHint,
                        new Border { Height = 1, Background = (Brush)Application.Current.Resources["LineBrush"] },
                        modulesPanel,
                    },
                },
                MaxHeight = 440,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            },
        };

        void RebuildModules()
        {
            modulesPanel.Children.Clear();
            advancedHint.Visibility = advanced.IsOn ? Visibility.Visible : Visibility.Collapsed;
            var query = search.Text.Trim();
            var allowed = ModulesForKind(_editor.Draft.Kind);
            var groups = new (string Label, IReadOnlyList<IRenderModuleFactory> Modules, int Index)[]
            {
                (PresetEditorText.Get("PresetEditorGroupAmbient", "Ambient"), RenderModules.Ambient, 0),
                (PresetEditorText.Get("PresetEditorGroupMusic", "Music"), RenderModules.Music, 1),
                (PresetEditorText.Get("PresetEditorGroupKey", "Key"), RenderModules.Key, 2),
                (PresetEditorText.Get("PresetEditorGroupSystem", "System"), RenderModules.System, 3),
            };
            var added = 0;
            foreach (var group in groups)
            {
                if (!advanced.IsOn && !allowed.Contains(group.Index)) continue;
                var matches = group.Modules
                    .Select(module => (
                        Module: module,
                        Name: PresetEditorText.ModuleName(module.Info.Id, module.Info.Name)))
                    .Where(entry => query.Length == 0 ||
                        entry.Name.Contains(query, StringComparison.CurrentCultureIgnoreCase) ||
                        entry.Module.Info.Id.Contains(query, StringComparison.OrdinalIgnoreCase))
                    .OrderBy(entry => entry.Name, StringComparer.CurrentCulture)
                    .ToArray();
                if (matches.Length == 0) continue;
                modulesPanel.Children.Add(new TextBlock
                {
                    Text = group.Label,
                    Margin = new Thickness(0, added == 0 ? 0 : 6, 0, 0),
                    FontFamily = (FontFamily)Application.Current.Resources["AppFontSemiBoldFamily"],
                    FontSize = 12,
                    FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                    Foreground = (Brush)Application.Current.Resources["AccentBrush"],
                });
                foreach (var entry in matches)
                {
                    var module = entry.Module;
                    var button = new Button
                    {
                        Content = new StackPanel
                        {
                            Spacing = 1,
                            Children =
                            {
                                new TextBlock
                                {
                                    Text = entry.Name,
                                    FontSize = 12,
                                    Foreground = (Brush)Application.Current.Resources["DataBrush"],
                                },
                                new TextBlock
                                {
                                    Text = module.Info.Id,
                                    FontSize = 10,
                                    FontFamily = new FontFamily("Cascadia Mono,Consolas"),
                                    Foreground = (Brush)Application.Current.Resources["MutedBrush"],
                                },
                            },
                        },
                        HorizontalAlignment = HorizontalAlignment.Stretch,
                        HorizontalContentAlignment = HorizontalAlignment.Left,
                        Style = (Style)Application.Current.Resources["SecondaryButtonStyle"],
                    };
                    AutomationProperties.SetAutomationId(button, "PresetEditorModule_" + module.Info.Id);
                    var moduleId = module.Info.Id;
                    button.Click += async (_, _) =>
                    {
                        flyout.Hide();
                        await ChangeModuleAsync(moduleId);
                    };
                    modulesPanel.Children.Add(button);
                    added++;
                }
            }

            if (added == 0)
            {
                modulesPanel.Children.Add(new TextBlock
                {
                    Text = PresetEditorText.Get("PresetEditorNoModule", "No module matches this filter."),
                    FontSize = 12,
                    Foreground = (Brush)Application.Current.Resources["MutedBrush"],
                    TextWrapping = TextWrapping.Wrap,
                });
            }
        }

        search.TextChanged += (_, _) => RebuildModules();
        advanced.Toggled += (_, _) => RebuildModules();
        RebuildModules();
        ModulePickerButton.Flyout = flyout;
        flyout.ShowAt(ModulePickerButton);
    }

    private async Task ChangeModuleAsync(string moduleId)
    {
        if (_editor.SelectedLayer is not { } item || item.Layer.ModuleId == moduleId) return;
        if (item.Layer.Params.Count > 0)
        {
            var moduleName = RenderModuleRegistry.TryGet(moduleId, out var module)
                ? PresetEditorText.ModuleName(moduleId, module.Info.Name)
                : moduleId;
            var dialog = new ContentDialog
            {
                Title = PresetEditorText.Get("PresetEditorModuleResetTitle", "Reset module parameters?"),
                Content = new TextBlock
                {
                    Text = PresetEditorText.Format("PresetEditorModuleResetMessage",
                        "Switching to {0} replaces the current layer parameters with that module's defaults.",
                        moduleName),
                    TextWrapping = TextWrapping.Wrap,
                },
                PrimaryButtonText = PresetEditorText.Get("PresetEditorModuleResetConfirm", "Switch module"),
                CloseButtonText = PresetEditorText.Get("LightingStudioPresetCancel", "Cancel"),
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = RootLayout.XamlRoot,
            };
            if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
        }

        _editor.ChangeModule(item, moduleId);
        BuildParameterEditor();
        BuildPaletteEditor();
        UpdateLayerControls();
        UpdateSimulationUi();
        UpdateUiState();
        RefreshPreviewDefinition(rebuildWorld: true);
        SetStatus($"{PresetEditorText.Get("PresetEditorModule", "Module")}: {item.ModuleName}");
    }

    private static HashSet<int> ModulesForKind(StudioPresetKind kind) => kind switch
    {
        StudioPresetKind.Music => [1],
        StudioPresetKind.Reactive => [1, 2],
        StudioPresetKind.Screen => [3],
        StudioPresetKind.System => [3],
        _ => [0],
    };

    private void BuildParameterEditor()
    {
        if (ParameterPanel is null) return;
        ParameterPanel.Children.Clear();
        ParameterErrorText.Visibility = Visibility.Collapsed;
        ParameterErrorText.Text = string.Empty;
        if (_editor.SelectedLayer is not { } item)
        {
            ParameterPanel.Children.Add(new TextBlock
            {
                Text = PresetEditorText.Get("PresetEditorNoModule", "No module selected."),
                FontSize = 12,
                Foreground = (Brush)Application.Current.Resources["MutedBrush"],
            });
            return;
        }

        var moduleId = item.Layer.ModuleId;
        if (string.IsNullOrWhiteSpace(moduleId))
        {
            ParameterPanel.Children.Add(new TextBlock
            {
                Text = PresetEditorText.Get("PresetEditorNoModule", "No module selected."),
                FontSize = 12,
                Foreground = (Brush)Application.Current.Resources["MutedBrush"],
            });
            return;
        }

        IReadOnlyList<RenderModuleParameter> fields;
        try
        {
            fields = RenderModuleRegistry.Describe(moduleId, JsonSerializer.SerializeToElement(item.Layer.Params));
        }
        catch (Exception exception) when (exception is InvalidDataException or FormatException or
            ArgumentException or JsonException)
        {
            ParameterErrorText.Text = exception.Message;
            ParameterErrorText.Visibility = Visibility.Visible;
            try
            {
                fields = RenderModuleRegistry.Describe(moduleId, JsonSerializer.SerializeToElement(new JsonObject()));
            }
            catch (Exception fallback) when (fallback is InvalidDataException or FormatException or
                ArgumentException or JsonException)
            {
                ParameterPanel.Children.Add(new TextBlock
                {
                    Text = fallback.Message,
                    FontSize = 11,
                    Foreground = (Brush)Application.Current.Resources["DangerBrush"],
                    TextWrapping = TextWrapping.Wrap,
                });
                return;
            }
        }

        foreach (var field in fields)
        {
            ParameterPanel.Children.Add(BuildParameterRow(item, field));
        }
    }

    private FrameworkElement BuildParameterRow(PresetEditorLayerItem item, RenderModuleParameter field)
    {
        var label = AppStrings.TryGet("LightingStudioModuleParam_" + field.Name) ??
            PresetEditorText.Humanize(field.Name);
        var key = "param:" + _editor.Draft.Layers.IndexOf(item.Layer) + ":" + field.Name;
        FrameworkElement control;
        if (field.Kind is JsonValueKind.True or JsonValueKind.False)
        {
            var toggle = new ToggleSwitch
            {
                IsOn = ReadBoolean(item.Layer.Params, field.Name, field.Default.GetBoolean()),
                OnContent = string.Empty,
                OffContent = string.Empty,
                HorizontalAlignment = HorizontalAlignment.Left,
            };
            toggle.Toggled += (_, _) =>
            {
                if (_syncingParameters) return;
                SetParameterValue(item.Layer.Params, field.Name, JsonValue.Create(toggle.IsOn), key);
            };
            control = toggle;
        }
        else if (field.Kind == JsonValueKind.Number)
        {
            var value = Math.Clamp(
                ReadNumber(item.Layer.Params, field.Name, field.Default.GetDouble()),
                field.Minimum,
                field.Maximum);
            var step = NumberStep(field);
            var slider = new Slider
            {
                Minimum = field.Minimum,
                Maximum = field.Maximum,
                StepFrequency = step,
                Value = value,
                VerticalAlignment = VerticalAlignment.Center,
            };
            var box = new NumberBox
            {
                Minimum = field.Minimum,
                Maximum = field.Maximum,
                SmallChange = step,
                LargeChange = step * 10,
                SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact,
                Value = value,
                Width = 82,
            };
            var syncing = false;
            void Apply(double next)
            {
                if (syncing || !double.IsFinite(next)) return;
                var clamped = Math.Clamp(next, field.Minimum, field.Maximum);
                syncing = true;
                try
                {
                    slider.Value = clamped;
                    box.Value = clamped;
                }
                finally
                {
                    syncing = false;
                }

                var node = field.Integer
                    ? JsonValue.Create((int)Math.Round(clamped))
                    : JsonValue.Create(clamped);
                SetParameterValue(item.Layer.Params, field.Name, node, key);
            }

            slider.ValueChanged += (_, args) => Apply(args.NewValue);
            box.ValueChanged += (_, args) => Apply(args.NewValue);
            var grid = new Grid { ColumnDefinitions = { new ColumnDefinition(), new ColumnDefinition { Width = new GridLength(84) } }, ColumnSpacing = 8 };
            grid.Children.Add(slider);
            Grid.SetColumn(box, 1);
            grid.Children.Add(box);
            AutomationProperties.SetAutomationId(box, "PresetEditorParam_" + field.Name);
            AutomationProperties.SetName(box, label);
            control = grid;
        }
        else if (field.Kind == JsonValueKind.String && field.Choices is not { Count: > 0 })
        {
            var current = ReadString(item.Layer.Params, field.Name,
                field.Default.ValueKind == JsonValueKind.String ? field.Default.GetString() : null) ?? string.Empty;
            if (IsHexColor(current))
            {
                var color = StudioRgb.FromHex(current);
                var swatch = new Button
                {
                    Width = 44,
                    Height = 32,
                    Padding = new Thickness(0),
                    CornerRadius = new CornerRadius(4),
                    Background = new SolidColorBrush(ToColor(color)),
                    BorderBrush = (Brush)Application.Current.Resources["LineBrush"],
                    BorderThickness = new Thickness(1),
                    HorizontalAlignment = HorizontalAlignment.Left,
                };
                AutomationProperties.SetName(swatch, label);
                swatch.Flyout = new Flyout
                {
                    Placement = FlyoutPlacementMode.Left,
                    Content = BuildColorEditor(color, next =>
                    {
                        swatch.Background = new SolidColorBrush(ToColor(next));
                        SetParameterValue(item.Layer.Params, field.Name,
                            JsonValue.Create(next.ToHex()), key);
                    }),
                };
                AutomationProperties.SetAutomationId(swatch, "PresetEditorParam_" + field.Name);
                control = swatch;
            }
            else
            {
                var text = new TextBox
                {
                    Text = current,
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                };
                text.TextChanged += (_, _) =>
                {
                    if (_syncingParameters) return;
                    SetParameterValue(item.Layer.Params, field.Name,
                        JsonValue.Create(text.Text), key);
                };
                AutomationProperties.SetAutomationId(text, "PresetEditorParam_" + field.Name);
                control = text;
            }
        }
        else if (field.Choices is { Count: > 0 } choices)
        {
            var current = ReadString(item.Layer.Params, field.Name,
                field.Default.ValueKind == JsonValueKind.String ? field.Default.GetString() : null);
            var picker = new ComboBox
            {
                ItemsSource = choices.Select(choice => new KeyValuePair<string, string>(
                    choice,
                    AppStrings.TryGet("LightingStudioModuleValue_" + choice) ?? choice)).ToArray(),
                DisplayMemberPath = "Value",
                SelectedValuePath = "Key",
                SelectedValue = current,
                HorizontalAlignment = HorizontalAlignment.Stretch,
            };
            picker.SelectionChanged += (_, _) =>
            {
                if (_syncingParameters || picker.SelectedValue is not string selected) return;
                SetParameterValue(item.Layer.Params, field.Name, JsonValue.Create(selected), key);
            };
            AutomationProperties.SetAutomationId(picker, "PresetEditorParam_" + field.Name);
            AutomationProperties.SetName(picker, label);
            control = picker;
        }
        else
        {
            var fallback = field.Default.ValueKind == JsonValueKind.String
                ? field.Default.GetString() ?? string.Empty
                : string.Empty;
            var current = ReadString(item.Layer.Params, field.Name, fallback) ?? string.Empty;
            var text = new TextBox
            {
                Text = current,
                HorizontalAlignment = HorizontalAlignment.Stretch,
            };
            AutomationProperties.SetAutomationId(text, "PresetEditorParam_" + field.Name);
            control = text;
        }

        AutomationProperties.SetName(control, label);
        var row = new StackPanel { Spacing = 5 };
        row.Children.Add(new TextBlock
        {
            Text = label,
            FontSize = 12,
            Foreground = (Brush)Application.Current.Resources["MutedBrush"],
        });
        row.Children.Add(control);
        return row;
    }

    private void SetParameterValue(JsonObject values, string name, JsonNode? value, string key)
    {
        if (_syncingParameters) return;
        _syncingParameters = true;
        try
        {
            _editor.BeginEdit(key);
            values[name] = value;
            _editor.ApplyEdit();
        }
        finally
        {
            _syncingParameters = false;
        }
    }

    private static double NumberStep(RenderModuleParameter field)
    {
        if (field.Integer) return 1;
        var step = (field.Maximum - field.Minimum) / 100d;
        if (!double.IsFinite(step) || step <= 0) return 0.01;
        return Math.Max(0.001, Math.Round(step, 3));
    }

    private static bool ReadBoolean(JsonObject values, string name, bool fallback)
    {
        return values.TryGetPropertyValue(name, out var node) &&
            node is JsonValue value && value.TryGetValue<bool>(out var result)
            ? result
            : fallback;
    }

    private static double ReadNumber(JsonObject values, string name, double fallback)
    {
        if (!values.TryGetPropertyValue(name, out var node) || node is not JsonValue value)
            return fallback;
        try
        {
            if (value.TryGetValue<double>(out var number) && double.IsFinite(number)) return number;
            if (value.TryGetValue<int>(out var integer)) return integer;
            if (value.TryGetValue<long>(out var longInteger)) return longInteger;
        }
        catch (InvalidOperationException)
        {
            // A malformed parameter stays at the module default; validation reports it.
        }

        return fallback;
    }

    private static string? ReadString(JsonObject values, string name, string? fallback)
    {
        return values.TryGetPropertyValue(name, out var node) &&
            node is JsonValue value && value.TryGetValue<string>(out var result)
            ? result
            : fallback;
    }

    private static bool IsHexColor(string value) =>
        value.Length == 7 && value[0] == '#' &&
        value.Skip(1).All(character =>
            character is >= '0' and <= '9' or >= 'a' and <= 'f' or >= 'A' and <= 'F');

    private FrameworkElement BuildColorEditor(StudioRgb initial, Action<StudioRgb> apply)
    {
        var picker = new ColorPicker
        {
            Color = ToColor(initial),
            Width = 300,
            IsAlphaEnabled = false,
            IsColorSpectrumVisible = true,
            IsColorChannelTextInputVisible = true,
            IsHexInputVisible = true,
            IsMoreButtonVisible = false,
        };
        picker.ColorChanged += (_, args) =>
            apply(new StudioRgb(args.NewColor.R, args.NewColor.G, args.NewColor.B));
        return picker;
    }

    private static Color ToColor(StudioRgb color) =>
        Color.FromArgb(255, color.R, color.G, color.B);

    private void BuildPaletteEditor()
    {
        if (PalettePanel is null) return;
        PalettePanel.Children.Clear();
        if (_editor.SelectedLayer is not { } item)
        {
            UpdatePaletteStrip();
            return;
        }

        for (var index = 0; index < item.Layer.Palette.Count; index++)
        {
            var stopIndex = index;
            var stop = item.Layer.Palette[index];
            var row = new Grid
            {
                ColumnDefinitions =
                {
                    new ColumnDefinition { Width = new GridLength(38) },
                    new ColumnDefinition(),
                    new ColumnDefinition { Width = new GridLength(34) },
                },
                ColumnSpacing = 6,
            };
            var swatch = new Button
            {
                Width = 36,
                Height = 30,
                Padding = new Thickness(0),
                CornerRadius = new CornerRadius(4),
                Background = new SolidColorBrush(ToColor(stop.Color)),
                BorderBrush = (Brush)Application.Current.Resources["LineBrush"],
                BorderThickness = new Thickness(1),
            };
            AutomationProperties.SetAutomationId(swatch, $"PresetEditorPaletteColor{index}");
            swatch.Flyout = new Flyout
            {
                Placement = FlyoutPlacementMode.Left,
                Content = BuildColorEditor(stop.Color, color =>
                {
                    _editor.SetPaletteColor(item, stopIndex, color);
                    swatch.Background = new SolidColorBrush(ToColor(color));
                    UpdatePaletteStrip();
                }),
            };
            row.Children.Add(swatch);
            var position = new NumberBox
            {
                Minimum = 0,
                Maximum = 100,
                SmallChange = 1,
                SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact,
                Value = stop.Stop,
                HorizontalAlignment = HorizontalAlignment.Stretch,
            };
            AutomationProperties.SetName(position,
                PresetEditorText.Get("LightingStudioPresetColorPosition", "Color position"));
            position.ValueChanged += (_, args) =>
            {
                if (!double.IsFinite(args.NewValue)) return;
                _editor.SetPaletteStop(item, stopIndex, args.NewValue);
                UpdatePaletteStrip();
            };
            Grid.SetColumn(position, 1);
            row.Children.Add(position);
            var remove = new Button
            {
                Content = "−",
                Width = 30,
                Height = 30,
                Padding = new Thickness(0),
                Style = (Style)Application.Current.Resources["SecondaryButtonStyle"],
                IsEnabled = item.Layer.Palette.Count > 1,
            };
            AutomationProperties.SetName(remove,
                PresetEditorText.Get("PresetEditorRemoveLayer", "Remove"));
            remove.Click += (_, _) =>
            {
                _editor.RemovePaletteStop(item, stopIndex);
                BuildPaletteEditor();
            };
            Grid.SetColumn(remove, 2);
            row.Children.Add(remove);
            PalettePanel.Children.Add(row);
        }

        UpdatePaletteStrip();
    }

    private void UpdatePaletteStrip()
    {
        if (PaletteStrip is null) return;
        var stops = _editor.SelectedLayer?.Layer.Palette;
        if (stops is null || stops.Count == 0)
        {
            PaletteStrip.Background = (Brush)Application.Current.Resources["SurfaceRaisedBrush"];
            return;
        }

        var ordered = stops.OrderBy(stop => stop.Stop).ToArray();
        var brush = new LinearGradientBrush { StartPoint = new Point(0, 0.5), EndPoint = new Point(1, 0.5) };
        if (ordered.Length == 1)
        {
            brush.GradientStops.Add(new GradientStop { Color = ToColor(ordered[0].Color), Offset = 0 });
            brush.GradientStops.Add(new GradientStop { Color = ToColor(ordered[0].Color), Offset = 1 });
        }
        else
        {
            if (ordered[0].Stop > 0)
            {
                brush.GradientStops.Add(new GradientStop
                {
                    Color = ToColor(ordered[0].Color),
                    Offset = 0,
                });
            }
            foreach (var stop in ordered)
            {
                brush.GradientStops.Add(new GradientStop
                {
                    Color = ToColor(stop.Color),
                    Offset = Math.Clamp(stop.Stop / 100d, 0, 1),
                });
            }
            if (ordered[^1].Stop < 100)
            {
                brush.GradientStops.Add(new GradientStop
                {
                    Color = ToColor(ordered[^1].Color),
                    Offset = 1,
                });
            }
        }

        PaletteStrip.Background = brush;
    }

    private void AddPaletteClick(object sender, RoutedEventArgs e)
    {
        if (_editor.SelectedLayer is not { } item) return;
        _editor.AddPaletteStop(item);
        BuildPaletteEditor();
        UpdateSelectionPanels();
    }

    private void ResetPaletteClick(object sender, RoutedEventArgs e)
    {
        if (_editor.SelectedLayer is not { } item) return;
        _editor.ResetPalette(item);
        BuildPaletteEditor();
        UpdateSelectionPanels();
    }
}
