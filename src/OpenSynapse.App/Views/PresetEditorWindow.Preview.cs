using System.Diagnostics;
using System.Collections.Specialized;
using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Microsoft.UI.Text;
using OpenSynapse.App.ViewModels;
using OpenSynapse.Core.Lighting;
using Windows.Foundation;
using Windows.Graphics.Imaging;
using Windows.Storage.Pickers;
using Windows.UI;

namespace OpenSynapse.App.Views;

public sealed partial class PresetEditorWindow
{
    private const int WorldGrid = StudioWorldCanvas.GridSize;
    private const int WorldView = 48;
    private const double TimelineMaximum = 10;
    private readonly List<StudioDeviceOption> _deviceOptions = new();
    private StudioDeviceOption? _selectedDevice;
    private Rectangle[] _deviceCells = [];
    private SolidColorBrush[] _deviceBrushes = [];
    private Rectangle[] _worldCells = [];
    private SolidColorBrush[] _worldBrushes = [];
    private (int Rows, int Columns) _deviceSize;
    private double _deviceCellSize;
    private double _worldCellSize;
    private StudioEffectState _deviceState = new();
    private StudioEffectState _worldState = new();
    private StudioPresetConfig? _compiled;
    private StudioWorldCanvas? _worldCanvas;
    private StudioKeyHeat? _keyHeat;
    private int _keyRows;
    private int _keyColumns;
    private bool _simulateKeys = true;
    private int _lastAutoKeyStep = -1;
    private int _autoKeyRow = -1;
    private int _autoKeyColumn = -1;
    private int _previewMode;
    private bool _playing = true;
    private bool _syncingPreview;
    private bool _syncingTimeline;
    private double _seconds;
    private long _lastPreviewTimestamp;
    private double _fpsWindow;
    private int _frameCount;
    private bool _worldSynthetic;
    private byte[]? _screenPixels;
    private string? _screenImageName;

    private void RefreshPreviewModeItems()
    {
        if (PreviewDeviceButton is null || PreviewWorldButton is null) return;
        var selected = Math.Clamp(_previewMode, 0, 1);
        _syncingPreview = true;
        try
        {
            PreviewDeviceButton.Content = PresetEditorText.Get("PresetEditorPreviewDevice", "This device");
            PreviewWorldButton.Content = PresetEditorText.Get("PresetEditorPreviewWorld", "Shared canvas");
            SetPreviewModeButton(PreviewDeviceButton, selected == 0);
            SetPreviewModeButton(PreviewWorldButton, selected == 1);
            _previewMode = selected;
        }
        finally
        {
            _syncingPreview = false;
        }

        ApplyPreviewModeVisibility();
    }

    private void PreviewModeDeviceClick(object sender, RoutedEventArgs e) => SetPreviewMode(0);

    private void PreviewModeWorldClick(object sender, RoutedEventArgs e) => SetPreviewMode(1);

    private void SetPreviewMode(int mode)
    {
        if (_syncingPreview) return;
        _previewMode = Math.Clamp(mode, 0, 1);
        _syncingPreview = true;
        try
        {
            SetPreviewModeButton(PreviewDeviceButton, _previewMode == 0);
            SetPreviewModeButton(PreviewWorldButton, _previewMode == 1);
        }
        finally
        {
            _syncingPreview = false;
        }

        ApplyPreviewModeVisibility();
        RefreshPreviewDefinition(rebuildWorld: _previewMode == 1);
        LayoutPreviewSurface();
        RenderPreviewNow();
    }

    private static void SetPreviewModeButton(Button button, bool selected)
    {
        // Restyling a Button replaces its automation peer, which makes UI
        // automation hit a stale element. Visual-only selection keeps it stable.
        button.FontWeight = selected ? FontWeights.SemiBold : FontWeights.Normal;
        button.Foreground = (Brush)Application.Current.Resources[
            selected ? "AccentBrush" : "DataBrush"];
    }

    private void ApplyPreviewModeVisibility()
    {
        var world = _previewMode == 1;
        DevicePicker.Visibility = world ? Visibility.Collapsed : Visibility.Visible;
        DevicePreviewCanvas.Visibility = world ? Visibility.Collapsed : Visibility.Visible;
        WorldPreviewCanvas.Visibility = world ? Visibility.Visible : Visibility.Collapsed;
        WorldHintText.Visibility = world && _worldSynthetic ? Visibility.Visible : Visibility.Collapsed;
    }

    private void BuildDeviceOptions()
    {
        _deviceOptions.Clear();
        if (_studio?.Devices is { Count: > 0 } devices)
        {
            _deviceOptions.AddRange(devices);
        }

        if (_deviceOptions.Count == 0)
        {
            _deviceOptions.Add(new StudioDeviceOption(
                PresetEditorText.Get("LightingStudioNoDevice", "No device"),
                null,
                rows: 6,
                columns: 22));
        }

        var preferred = _studio?.SelectedDevice is { } selected &&
            _deviceOptions.Contains(selected)
            ? selected
            : _deviceOptions.FirstOrDefault(item => item.HasMatrix) ?? _deviceOptions[0];
        _selectedDevice = preferred;
        _syncingPreview = true;
        try
        {
            DevicePicker.ItemsSource = null;
            DevicePicker.ItemsSource = _deviceOptions;
            DevicePicker.SelectedItem = preferred;
        }
        finally
        {
            _syncingPreview = false;
        }

        _deviceSize = DevicePreviewSize();
        EnsureKeyHeat();
        BuildDevicePreview();
    }

    private void DeviceChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_syncingPreview) return;
        _selectedDevice = DevicePicker.SelectedItem as StudioDeviceOption;
        _deviceSize = DevicePreviewSize();
        EnsureKeyHeat();
        BuildDevicePreview();
        RenderPreviewNow();
    }

    private (int Rows, int Columns) DevicePreviewSize()
    {
        var option = _selectedDevice;
        if (option is null) return (6, 22);
        if (option.IsBlade && option.Rows > 0 && option.Columns > 0)
            return (option.Rows, option.Columns);

        var visual = !string.IsNullOrWhiteSpace(option.EffectiveInstanceId)
            ? _studio?.DeviceVisuals.FirstOrDefault(item =>
                string.Equals(item.InstanceId, option.EffectiveInstanceId, StringComparison.OrdinalIgnoreCase))
            : null;
        if (visual?.Layout is { MatrixRows: > 0, MatrixColumns: > 0 } layout)
            return (layout.MatrixRows, layout.MatrixColumns);
        if (option.Rows > 0 && option.Columns > 0)
            return (option.Rows, option.Columns);
        return (6, 22);
    }

    private void EnsureKeyHeat()
    {
        var (rows, columns) = _deviceSize;
        if (_keyHeat is not null && _keyRows == rows && _keyColumns == columns) return;
        _keyRows = rows;
        _keyColumns = columns;
        _keyHeat = new StudioKeyHeat(rows, columns);
        _lastAutoKeyStep = -1;
        _autoKeyRow = -1;
        _autoKeyColumn = -1;
    }

    private void BuildDevicePreview()
    {
        if (DevicePreviewCanvas is null) return;
        var (rows, columns) = _deviceSize;
        if (rows <= 0 || columns <= 0) rows = 6;
        if (columns <= 0) columns = 22;
        _deviceSize = (rows, columns);
        var availableWidth = PreviewSurface.ActualWidth > 0 ? PreviewSurface.ActualWidth - 28 : 520;
        var availableHeight = PreviewSurface.ActualHeight > 0 ? PreviewSurface.ActualHeight - 28 : 280;
        var cell = Math.Max(2, Math.Floor(Math.Min(availableWidth / columns, availableHeight / rows)));
        cell = Math.Min(cell, 38);
        _deviceCellSize = cell;
        DevicePreviewCanvas.Children.Clear();
        _deviceCells = new Rectangle[rows * columns];
        _deviceBrushes = new SolidColorBrush[rows * columns];
        DevicePreviewCanvas.Width = columns * cell;
        DevicePreviewCanvas.Height = rows * cell;
        for (var row = 0; row < rows; row++)
        {
            for (var column = 0; column < columns; column++)
            {
                var index = row * columns + column;
                var brush = new SolidColorBrush(Color.FromArgb(255, 0, 0, 0));
                var rectangle = new Rectangle
                {
                    Width = Math.Max(1, cell - 1),
                    Height = Math.Max(1, cell - 1),
                    RadiusX = Math.Min(3, cell / 5),
                    RadiusY = Math.Min(3, cell / 5),
                    Fill = brush,
                };
                Canvas.SetLeft(rectangle, column * cell);
                Canvas.SetTop(rectangle, row * cell);
                DevicePreviewCanvas.Children.Add(rectangle);
                _deviceCells[index] = rectangle;
                _deviceBrushes[index] = brush;
            }
        }
    }

    private void LayoutPreviewSurface()
    {
        if (!_loaded) return;
        if (_previewMode == 0)
        {
            BuildDevicePreview();
            RenderPreviewNow();
            return;
        }

        BuildWorldView();
        RenderPreviewNow();
    }

    private void BuildWorldView()
    {
        if (WorldPreviewCanvas is null) return;
        var availableWidth = PreviewSurface.ActualWidth > 0 ? PreviewSurface.ActualWidth - 28 : 520;
        var availableHeight = PreviewSurface.ActualHeight > 0 ? PreviewSurface.ActualHeight - 28 : 280;
        var cell = Math.Max(2, Math.Floor(Math.Min(availableWidth / WorldView, availableHeight / WorldView)));
        _worldCellSize = cell;
        if (_worldCells.Length == WorldView * WorldView)
        {
            WorldPreviewCanvas.Width = WorldView * cell;
            WorldPreviewCanvas.Height = WorldView * cell;
            for (var row = 0; row < WorldView; row++)
            {
                for (var column = 0; column < WorldView; column++)
                {
                    var rectangle = _worldCells[row * WorldView + column];
                    rectangle.Width = Math.Max(1, cell);
                    rectangle.Height = Math.Max(1, cell);
                    Canvas.SetLeft(rectangle, column * cell);
                    Canvas.SetTop(rectangle, row * cell);
                }
            }

            return;
        }

        WorldPreviewCanvas.Children.Clear();
        _worldCells = new Rectangle[WorldView * WorldView];
        _worldBrushes = new SolidColorBrush[WorldView * WorldView];
        WorldPreviewCanvas.Width = WorldView * cell;
        WorldPreviewCanvas.Height = WorldView * cell;
        for (var row = 0; row < WorldView; row++)
        {
            for (var column = 0; column < WorldView; column++)
            {
                var index = row * WorldView + column;
                var brush = new SolidColorBrush(Color.FromArgb(255, 0, 0, 0));
                var rectangle = new Rectangle
                {
                    Width = cell,
                    Height = cell,
                    Fill = brush,
                };
                Canvas.SetLeft(rectangle, column * cell);
                Canvas.SetTop(rectangle, row * cell);
                WorldPreviewCanvas.Children.Add(rectangle);
                _worldCells[index] = rectangle;
                _worldBrushes[index] = brush;
            }
        }
    }

    private void ResetPreviewState()
    {
        _compiled = null;
        _worldCanvas = null;
        _worldState = new StudioEffectState();
        _keyHeat = null;
        _lastAutoKeyStep = -1;
        _autoKeyRow = -1;
        _autoKeyColumn = -1;
        ResetPreviewClock();
    }

    private void ResetPreviewClock()
    {
        _seconds = 0;
        _lastPreviewTimestamp = Stopwatch.GetTimestamp();
        _fpsWindow = 0;
        _frameCount = 0;
        _syncingTimeline = true;
        TimelineSlider.Value = 0;
        _syncingTimeline = false;
        UpdateTimeText();
    }

    private void RefreshPreviewDefinition(bool rebuildWorld = false)
    {
        try
        {
            _compiled = StudioPresetCompiler.Compile(_editor.Draft);
        }
        catch (Exception exception) when (exception is InvalidDataException or FormatException or
            ArgumentException or InvalidOperationException or JsonException)
        {
            _compiled = null;
        }

        if (_previewMode == 1 && (rebuildWorld || _worldCanvas is null))
        {
            BuildWorldCanvas();
        }

        RenderPreviewNow();
    }

    private void RenderPreviewTick()
    {
        if (!_loaded) return;
        var now = Stopwatch.GetTimestamp();
        var elapsed = Math.Clamp((now - _lastPreviewTimestamp) / (double)Stopwatch.Frequency, 0, 0.25);
        _lastPreviewTimestamp = now;
        if (_playing)
        {
            _seconds += elapsed;
            if (_seconds >= TimelineMaximum)
            {
                if (LoopToggle.IsOn)
                {
                    _seconds %= TimelineMaximum;
                }
                else
                {
                    _seconds = TimelineMaximum;
                    _playing = false;
                    UpdatePlayButton();
                }
            }
        }

        AdvanceKeySimulation(elapsed);
        UpdateTimelineSlider();
        RenderPreviewNow();
        UpdateFps(elapsed);
    }

    private void RenderPreviewNow()
    {
        if (!_loaded || NoPreviewText is null) return;
        if (_compiled is null)
        {
            NoPreviewText.Visibility = Visibility.Visible;
            NoPreviewText.Text = PresetEditorText.Get("PresetEditorNoPreview", "No preview");
            return;
        }

        NoPreviewText.Visibility = Visibility.Collapsed;
        if (_previewMode == 0)
        {
            RenderDeviceFrame();
        }
        else
        {
            RenderWorldFrame();
        }
    }

    private void RenderDeviceFrame()
    {
        var (rows, columns) = _deviceSize;
        if (rows <= 0 || columns <= 0) return;
        StudioRgb[] frame;
        try
        {
            frame = StudioPresetEngine.Render(
                _compiled,
                rows,
                columns,
                _seconds,
                BuildPreviewInput(),
                _deviceState);
        }
        catch (Exception exception) when (exception is InvalidDataException or ArgumentException or
            InvalidOperationException or IndexOutOfRangeException)
        {
            NoPreviewText.Text = exception.Message;
            NoPreviewText.Visibility = Visibility.Visible;
            return;
        }

        for (var index = 0; index < frame.Length && index < _deviceBrushes.Length; index++)
        {
            var color = frame[index];
            _deviceBrushes[index].Color = Color.FromArgb(255, color.R, color.G, color.B);
        }
    }

    private void RenderWorldFrame()
    {
        _worldCanvas ??= BuildWorldCanvas();
        if (_worldCanvas is null)
        {
            NoPreviewText.Visibility = Visibility.Visible;
            return;
        }

        StudioRgb[] frame;
        try
        {
            // The editor builds one whole-canvas preset layer. Render it with a
            // persistent state so stateful key modules do not restart every frame.
            frame = _worldCanvas.Layers is [var layer]
                ? StudioEffectRenderer.Render(
                    layer.Params, WorldGrid, WorldGrid, _seconds, BuildPreviewInput(), _worldState)
                : StudioCompositor.RenderLayers(
                    _worldCanvas.Layers, WorldGrid, WorldGrid, _seconds, BuildPreviewInput());
        }
        catch (Exception exception) when (exception is InvalidDataException or ArgumentException or
            InvalidOperationException or IndexOutOfRangeException)
        {
            NoPreviewText.Text = exception.Message;
            NoPreviewText.Visibility = Visibility.Visible;
            return;
        }

        if (_worldBrushes.Length != WorldView * WorldView) BuildWorldView();

        // 2304 rectangles stay smooth at 30 fps; the 96 x 96 render is averaged
        // in 2 x 2 blocks for display. shortcut: rebuild the world view if a
        // future preview needs per-pixel 96 x 96 editing.
        for (var row = 0; row < WorldView; row++)
        {
            for (var column = 0; column < WorldView; column++)
            {
                var r = 0;
                var g = 0;
                var b = 0;
                for (var y = 0; y < 2; y++)
                {
                    for (var x = 0; x < 2; x++)
                    {
                        var source = frame[(row * 2 + y) * WorldGrid + column * 2 + x];
                        r += source.R;
                        g += source.G;
                        b += source.B;
                    }
                }

                _worldBrushes[row * WorldView + column].Color =
                    Color.FromArgb(255, (byte)(r / 4), (byte)(g / 4), (byte)(b / 4));
            }
        }
    }

    private StudioEffectInput BuildPreviewInput()
    {
        var keys = UsesKeyModules() ? _keyHeat?.Snapshot() : null;
        var input = StudioPresetPreviewSimulation.Create(
            _seconds, _compiled?.Layers ?? [], keys);
        if (_screenPixels is not null && (UsesScreenModules() || _editor.Draft.Kind == StudioPresetKind.Screen))
        {
            input = input with { ScreenSamplerUv = SampleScreen };
        }

        return input;
    }

    private StudioRgb SampleScreen(double u, double v)
    {
        if (_screenPixels is not { Length: >= 96 * 96 * 4 } pixels) return StudioRgb.Black;
        var column = Math.Clamp((int)(u * 96), 0, 95);
        var row = Math.Clamp((int)(v * 96), 0, 95);
        var index = (row * 96 + column) * 4;
        return new StudioRgb(pixels[index + 2], pixels[index + 1], pixels[index]);
    }

    private bool UsesKeyModules() => _editor.Draft.Layers.Any(layer =>
        layer.ModuleId.StartsWith("key.", StringComparison.Ordinal));

    private bool UsesScreenModules() => _editor.Draft.Layers.Any(layer =>
        layer.ModuleId.StartsWith("screen.", StringComparison.Ordinal));

    private bool UsesMusicModules() => _editor.Draft.Layers.Any(layer =>
        layer.ModuleId.StartsWith("music.", StringComparison.Ordinal));

    private void UpdateSimulationUi()
    {
        if (SimKeysToggle is null) return;
        var keys = UsesKeyModules();
        var screen = UsesScreenModules() || _editor.Draft.Kind == StudioPresetKind.Screen;
        SimKeysToggle.Visibility = keys ? Visibility.Visible : Visibility.Collapsed;
        SimScreenPanel.Visibility = screen ? Visibility.Visible : Visibility.Collapsed;
        if (keys)
        {
            _simulateKeys = SimKeysToggle.IsOn;
        }

        SimScreenText.Text = _screenPixels is null
            ? PresetEditorText.Get("PresetEditorSimScreenNone", "No image")
            : PresetEditorText.Format("PresetEditorSimScreenImage", "Image: {0}",
                _screenImageName ?? string.Empty);
        SimHintText.Text = _editor.Draft.Kind == StudioPresetKind.Music || UsesMusicModules()
            ? PresetEditorText.Get("PresetEditorSimMusic", "Music input is simulated automatically.")
            : keys
                ? PresetEditorText.Get("PresetEditorSimHint",
                    "Click the device preview or enable simulated keys.")
                : screen
                    ? PresetEditorText.Get("PresetEditorSimHint",
                        "Screen input uses a procedural sampler unless you choose an image.")
                    : PresetEditorText.Get("PresetEditorSimHint",
                        "System values are simulated while you edit.");
        AutomationProperties.SetName(SimKeysToggle,
            PresetEditorText.Get("PresetEditorSimKeys", "Simulate keys"));
    }

    private void SimKeysToggled(object sender, RoutedEventArgs e)
    {
        _simulateKeys = SimKeysToggle.IsOn;
        _lastAutoKeyStep = -1;
        if (!_simulateKeys && _keyHeat is not null && _autoKeyRow >= 0)
        {
            _keyHeat.Release(_autoKeyRow, _autoKeyColumn);
            _autoKeyRow = -1;
            _autoKeyColumn = -1;
        }
    }

    private void AdvanceKeySimulation(double elapsed)
    {
        _keyHeat?.Decay(elapsed);
        if (!_simulateKeys || _keyHeat is null) return;
        // Keep one readable press on screen; the old 180 ms scan stacked most of
        // the preset's life into a random-looking patchwork.
        var step = (int)Math.Floor(_seconds / 0.9);
        if (step == _lastAutoKeyStep) return;
        if (_autoKeyRow >= 0) _keyHeat.Release(_autoKeyRow, _autoKeyColumn);
        if (_keyHeat.Rows <= 0 || _keyHeat.Columns <= 0) return;
        // Walk one column at a time so the preview reads as a sweep, not noise.
        _autoKeyRow = step % _keyHeat.Rows;
        _autoKeyColumn = (step / _keyHeat.Rows) % _keyHeat.Columns;
        _keyHeat.Press(_autoKeyRow, _autoKeyColumn);
        _lastAutoKeyStep = step;
    }

    private void PreviewSurfacePointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (_previewMode != 0 || !UsesKeyModules() || _keyHeat is null) return;
        var point = e.GetCurrentPoint(DevicePreviewCanvas).Position;
        if (point.X < 0 || point.Y < 0 ||
            point.X >= DevicePreviewCanvas.Width || point.Y >= DevicePreviewCanvas.Height ||
            _deviceCellSize <= 0)
        {
            return;
        }

        var column = Math.Clamp((int)(point.X / _deviceCellSize), 0, _deviceSize.Columns - 1);
        var row = Math.Clamp((int)(point.Y / _deviceCellSize), 0, _deviceSize.Rows - 1);
        _keyHeat.Press(row, column);
        RenderPreviewNow();
        var heat = _keyHeat;
        _ = Task.Run(async () =>
        {
            await Task.Delay(140);
            heat.Release(row, column);
            DispatcherQueue.TryEnqueue(RenderPreviewNow);
        });
    }

    private async void ChooseScreenImageClick(object sender, RoutedEventArgs e)
    {
        try
        {
            var picker = new FileOpenPicker
            {
                SuggestedStartLocation = PickerLocationId.PicturesLibrary,
            };
            picker.FileTypeFilter.Add(".png");
            picker.FileTypeFilter.Add(".jpg");
            picker.FileTypeFilter.Add(".jpeg");
            picker.FileTypeFilter.Add(".bmp");
            WinRT.Interop.InitializeWithWindow.Initialize(picker, Handle);
            var file = await picker.PickSingleFileAsync();
            if (file is null) return;
            using var stream = await file.OpenReadAsync();
            var decoder = await BitmapDecoder.CreateAsync(stream);
            var transform = new BitmapTransform
            {
                ScaledWidth = 96,
                ScaledHeight = 96,
                InterpolationMode = BitmapInterpolationMode.Fant,
            };
            var pixels = await decoder.GetPixelDataAsync(
                BitmapPixelFormat.Bgra8,
                BitmapAlphaMode.Ignore,
                transform,
                ExifOrientationMode.IgnoreExifOrientation,
                ColorManagementMode.DoNotColorManage);
            _screenPixels = pixels.DetachPixelData();
            _screenImageName = file.Name;
            UpdateSimulationUi();
            SetStatus(PresetEditorText.Format("PresetEditorSimScreenImage", "Image: {0}", file.Name));
            RenderPreviewNow();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or
            InvalidOperationException or ArgumentException or COMException)
        {
            SetStatus(PresetEditorText.Format("PresetEditorOpenFailed", "Could not open file: {0}",
                exception.Message));
        }
    }

    private StudioWorldCanvas? BuildWorldCanvas()
    {
        if (_compiled is null) return null;
        var devices = new List<StudioWorldCanvasDevice>();
        if (_studio?.DeviceVisuals is { Count: >= 2 } visuals)
        {
            foreach (var visual in visuals)
            {
                var option = _studio.Devices.FirstOrDefault(item =>
                    string.Equals(item.EffectiveInstanceId, visual.InstanceId,
                        StringComparison.OrdinalIgnoreCase));
                var placement = _studio.Profile?.Devices.FirstOrDefault(item =>
                    string.Equals(item.InstanceId, visual.InstanceId, StringComparison.OrdinalIgnoreCase));
                var rows = visual.Layout is { MatrixRows: > 0 } layout ? layout.MatrixRows : option?.Rows ?? 0;
                var columns = visual.Layout is { MatrixColumns: > 0 } layout2
                    ? layout2.MatrixColumns
                    : option?.Columns ?? 0;
                var deviceRows = visual.Layout is { DeviceRows: > 0 } layout3 ? layout3.DeviceRows : rows;
                var deviceColumns = visual.Layout is { DeviceColumns: > 0 } layout4
                    ? layout4.DeviceColumns
                    : columns;
                if (rows <= 0 || columns <= 0) continue;
                devices.Add(new StudioWorldCanvasDevice(
                    visual.InstanceId,
                    visual.ProductId,
                    visual,
                    placement?.X,
                    placement?.Y,
                    placement?.Rotation ?? 0,
                    rows,
                    columns,
                    deviceRows,
                    deviceColumns,
                    placement is { Width: > 0 } ? placement.Width : null,
                    placement is { Height: > 0 } ? placement.Height : null));
            }
        }

        _worldSynthetic = devices.Count < 2;
        if (_worldSynthetic)
        {
            devices.Clear();
            var keyboardLayout = GridLayout(6, 22);
            var zoneLayout = GridLayout(1, 1);
            var keyboard = StudioDeviceVisualBuilder.BuildGrid(
                0x0A01, "preset-preview-keyboard",
                "Preview keyboard",
                keyboardLayout);
            var zone = StudioDeviceVisualBuilder.BuildGrid(
                0x0A02, "preset-preview-zone",
                "Preview zone",
                zoneLayout);
            devices.Add(new StudioWorldCanvasDevice(
                keyboard.InstanceId, keyboard.ProductId, keyboard, 0, 0, 0,
                keyboardLayout.MatrixRows, keyboardLayout.MatrixColumns,
                keyboardLayout.DeviceRows, keyboardLayout.DeviceColumns));
            devices.Add(new StudioWorldCanvasDevice(
                zone.InstanceId, zone.ProductId, zone, keyboard.Width + 120, 0, 0,
                zoneLayout.MatrixRows, zoneLayout.MatrixColumns,
                zoneLayout.DeviceRows, zoneLayout.DeviceColumns));
        }

        var profile = new StudioProfile("Preset preview");
        var layer = StudioLayerItem.CreateLayer("Preset", StudioEffectKind.Preset);
        layer.Params.PresetDefinition = _compiled;
        profile.Items.Add(layer);
        _worldCanvas = StudioWorldCanvas.Build(devices, profile);
        WorldHintText.Visibility = _previewMode == 1 && _worldSynthetic
            ? Visibility.Visible
            : Visibility.Collapsed;
        return _worldCanvas;
    }

    private static StudioLedLayout GridLayout(int rows, int columns)
    {
        var leds = new List<StudioLedMap>(rows * columns);
        for (var row = 0; row < rows; row++)
        {
            for (var column = 0; column < columns; column++)
            {
                leds.Add(new StudioLedMap(row, column, row, column));
            }
        }

        return StudioLedLayout.Create(rows, columns, rows, columns, leds);
    }

    private void OnStudioDevicesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (!_loaded) return;
        BuildDeviceOptions();
        RefreshPreviewDefinition(rebuildWorld: true);
    }

    private void OnStudioVisualsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (!_loaded) return;
        _deviceSize = DevicePreviewSize();
        EnsureKeyHeat();
        BuildDevicePreview();
        if (_previewMode == 1) RefreshPreviewDefinition(rebuildWorld: true);
        RenderPreviewNow();
    }

    private void TogglePlayback()
    {
        _playing = !_playing;
        if (_playing && !LoopToggle.IsOn && _seconds >= TimelineMaximum) _seconds = 0;
        _lastPreviewTimestamp = Stopwatch.GetTimestamp();
        UpdatePlayButton();
        RenderPreviewNow();
    }

    private void UpdatePlayButton()
    {
        if (PlayButton is null) return;
        PlayButton.Content = _playing
            ? PresetEditorText.Get("PresetEditorPause", "Pause")
            : PresetEditorText.Get("PresetEditorPlay", "Play");
        AutomationProperties.SetName(PlayButton, PlayButton.Content as string ?? "Play");
    }

    private void UpdateTimelineSlider()
    {
        _syncingTimeline = true;
        try
        {
            TimelineSlider.Value = _seconds;
        }
        finally
        {
            _syncingTimeline = false;
        }

        UpdateTimeText();
    }

    private void TimelineChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        if (_syncingTimeline) return;
        _seconds = Math.Clamp(e.NewValue, 0, TimelineMaximum);
        _lastPreviewTimestamp = Stopwatch.GetTimestamp();
        UpdateTimeText();
        RenderPreviewNow();
    }

    private void UpdateTimeText()
    {
        if (TimeText is null) return;
        TimeText.Text =
            $"{PresetEditorText.Get("PresetEditorTime", "Time")} {_seconds:0.00} / {TimelineMaximum:0.00} s";
    }

    private void UpdateFps(double elapsed)
    {
        _frameCount++;
        _fpsWindow += elapsed;
        if (_fpsWindow < 0.5) return;
        var fps = _frameCount / _fpsWindow;
        _frameCount = 0;
        _fpsWindow = 0;
        FpsText.Text = PresetEditorText.Format("PresetEditorFps", "{0:0} FPS", fps);
    }
}
