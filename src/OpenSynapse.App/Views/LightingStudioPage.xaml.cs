using System.Diagnostics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using OpenSynapse.App.ViewModels;
using OpenSynapse.Core.Devices;
using OpenSynapse.Core.Lighting;
using OpenSynapse.Windows.Lighting;
using OpenSynapse.Windows.Protocols;
using Windows.ApplicationModel.DataTransfer;
using Windows.Foundation;
using Windows.Storage.Pickers;
using Windows.UI;

namespace OpenSynapse.App.Views;

public sealed partial class LightingStudioPage : UserControl
{
    private const double CellSize = 28;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(1000d / 60d) };
    private readonly Stopwatch _clock = new();
    private Rectangle[] _cells = [];
    private SolidColorBrush[] _brushes = [];
    private (int Rows, int Columns) _previewSize;
    private LightingStudioViewModel? _viewModel;
    private StudioInputSampler? _inputSampler;
    private StudioLayerItem? _draggedLayer;
    private StudioLayerRow? _dropRow;
    private Border? _dropHint;
    private ComboBoxItem? _customZoomItem;
    private bool _canvasFitted;
    private string? _fittedVisualSignature;
#if DEBUG
    private StudioCanvasBenchmark? _studioBenchmark;
    private bool _studioBenchmarkOwnsCanvas;
    private bool _studioSelectionQa;
#endif

    internal MainWindow Host { get; set; } = null!;

    public LightingStudioPage()
    {
        InitializeComponent();
        ToolTipService.SetToolTip(ZoomOutButton, AppStrings.Text("LightingStudioZoomOut"));
        ToolTipService.SetToolTip(ZoomInButton, AppStrings.Text("LightingStudioZoomIn"));
        // ListViewBase has its own drag plumbing, so keep receiving these even if it handles them.
        LayerList.AddHandler(DragOverEvent, new DragEventHandler(LayerDragOver), true);
        LayerList.AddHandler(DropEvent, new DragEventHandler(LayerDrop), true);
        Inspector.CaptureRegionChanged += OnCaptureRegionChanged;
        _timer.Tick += (_, _) => RenderPreview();
        _captureSaveTimer.Tick += (_, _) => SavePendingCaptureRegion();
        Unloaded += (_, _) =>
        {
            _timer.Stop();
            SavePendingCaptureRegion();
        };
    }

    internal LightingStudioViewModel? ViewModel => _viewModel;

    /// <summary>
    /// Applies one decoded Blade key to the studio. The hardware frame source
    /// runs on its own clock; the preview uses this page's preview clock.
    /// </summary>
    internal void HandleMappingInput(BladeMappingInputEvent input)
    {
        if (_viewModel?.HandleMappingInput(input, _clock.Elapsed) > 0) RenderPreview();
    }

    internal void Initialize(
        IBladeLightingController? lighting,
        Func<IReadOnlyList<DeviceDescriptor>> devices,
        Func<IReadOnlyList<StudioDeviceOption>>? matrixDevices = null,
        StudioHidOutputRouter? hidRouter = null,
        StudioInputSampler? inputSampler = null,
        StudioArgbOutputRouter? argbRouter = null)
    {
        _viewModel?.Dispose();
        _inputSampler = inputSampler;
        _viewModel = new LightingStudioViewModel(
            lighting, devices, matrixDevices, hidRouter, inputSampler, argbRouter);
        StartStudioTimingIfRequested();
        StudioTimingAttachViewModel();
        RefreshDevicesWithTiming("initialize");
        _ = RefreshDevicesFromHostAsync();
        _viewModel.DocumentChanged += (_, _) => RenderPreview();
        _viewModel.PropertyChanged += (_, args) =>
        {
            StudioTimingOnPropertyChanged(args.PropertyName);
            if (args.PropertyName == nameof(LightingStudioViewModel.DeviceVisual) ||
                args.PropertyName == nameof(LightingStudioViewModel.DeviceVisuals) ||
                args.PropertyName == nameof(LightingStudioViewModel.Profile)) RefreshCanvas();
            else if (args.PropertyName == nameof(LightingStudioViewModel.SelectedParams)) BindInspector();
            else if (args.PropertyName == nameof(LightingStudioViewModel.SelectedEffectChoice)) Inspector.Refresh();
            else if (args.PropertyName == nameof(LightingStudioViewModel.PreviewRows) ||
                     args.PropertyName == nameof(LightingStudioViewModel.PreviewColumns)) BuildPreview();
        };
        Inspector.Changed += (_, _) =>
        {
            _viewModel?.NotifyEdited();
            RenderPreview();
        };
        DataContext = _viewModel;
        DeviceCanvas.DeviceMoved += OnDeviceMoved;
        DeviceCanvas.LedShiftClicked += (_, row, column) => _viewModel?.SetTidalCenter(row, column);
        DeviceCanvas.BucketClicked += (instanceId, row, column) =>
        {
            if (_viewModel?.BucketSelection(instanceId, row, column) is { Count: > 0 } cells)
            {
                DeviceCanvas.SelectLeds(cells);
            }
        };
        DeviceCanvas.ZoomChanged += (_, _) => SyncZoomPicker();
        UpdatePlayButton();
        if (_inputSampler is not null && Host is not null)
        {
            _inputSampler.SetCaptureRegion(Host.BehaviorSettings.AmbientCaptureRegion);
        }
        BindInspector();
        RefreshCanvas();
        RenderPreview();
    }

    internal void OnNavigatedTo()
    {
        StudioTimingMark("navigate-enter");
        RefreshDevicesWithTiming("navigate");
        _ = RefreshDevicesFromHostAsync();
        _viewModel?.EnsureLayerRows();
        if (_viewModel?.IsPlaying == true)
        {
            _clock.Restart();
            _timer.Start();
        }

#if DEBUG
        AttachStudioSelectionDump();
        if (StartStudioSelectionQaIfRequested() || StartStudioBenchmarkIfRequested()) return;
#endif
        RenderPreview();
        StudioTimingMark("navigate-exit");
    }

    internal void OnNavigatedFrom()
    {
        _timer.Stop();
#if DEBUG
        _studioBenchmark?.Stop();
#endif
    }


    private void BuildPreview()
    {
        var rows = _viewModel?.PreviewRows ?? BladeLightingLayout.LogicalRows;
        var columns = _viewModel?.PreviewColumns ?? BladeLightingLayout.LogicalColumns;
        if (_previewSize == (rows, columns) && _cells.Length == rows * columns) return;

        PreviewCanvas.Children.Clear();
        _previewSize = (rows, columns);
        _cells = new Rectangle[rows * columns];
        _brushes = new SolidColorBrush[rows * columns];
        PreviewCanvas.Width = columns * CellSize;
        PreviewCanvas.Height = rows * CellSize;
        for (var row = 0; row < rows; row++)
        {
            for (var column = 0; column < columns; column++)
            {
                var index = row * columns + column;
                var brush = new SolidColorBrush(Color.FromArgb(255, 0, 0, 0));
                var cell = new Rectangle
                {
                    Width = Math.Max(1, CellSize - 1),
                    Height = Math.Max(1, CellSize - 1),
                    Fill = brush,
                    RadiusX = 3,
                    RadiusY = 3,
                };
                Canvas.SetLeft(cell, column * CellSize);
                Canvas.SetTop(cell, row * CellSize);
                PreviewCanvas.Children.Add(cell);
                _cells[index] = cell;
                _brushes[index] = brush;
            }
        }
    }

    private void RefreshCanvas()
    {
#if DEBUG
        if (_studioBenchmarkOwnsCanvas || _studioSelectionQa) return;
#endif
        if (_viewModel is null) return;
        var timingStarted = Stopwatch.GetTimestamp();
        var visuals = _viewModel.DeviceVisuals;
#if DEBUG
        var dumpPath = Environment.GetEnvironmentVariable("OPENSSYNAPSE_STUDIO_DUMP");
        if (!string.IsNullOrEmpty(dumpPath))
        {
            File.AppendAllText(dumpPath,
                $"{Environment.NewLine}page visuals={visuals.Count} selected={_viewModel.SelectedDevice?.Name} canvas={DeviceCanvas.Visibility}");
        }
#endif
        if (visuals.Count == 0)
        {
            DeviceCanvas.Visibility = Visibility.Collapsed;
            FallbackPreview.Visibility = Visibility.Visible;
            StudioTimingMarkRefresh(timingStarted, visuals, "empty");
            return;
        }

        var visualSignature = string.Join(
            "|",
            visuals.Select(visual =>
                FormattableString.Invariant(
                    $"{visual.InstanceId}:{visual.Width:0.##}x{visual.Height:0.##}:{visual.Leds.Count}")));
        var needsFit = !_canvasFitted ||
            !string.Equals(_fittedVisualSignature, visualSignature, StringComparison.Ordinal);
        DeviceCanvas.Devices = visuals.ToArray();
        DeviceCanvas.Profile = _viewModel.Profile;
        DeviceCanvas.Refresh();
        DeviceCanvas.Visibility = Visibility.Visible;
        FallbackPreview.Visibility = Visibility.Collapsed;
        DeviceCanvas.UpdateLayout();
        // New hosts mean new brushes and possibly different matrix dimensions; repaint the current frame.
        RenderPreview();
        StudioTimingMarkRefresh(timingStarted, visuals, "build");
        // Fit after the device manifest replaces the temporary grid, and when the
        // selected device changes. Later frame refreshes keep the user's zoom.
        if (!needsFit) return;
        _canvasFitted = true;
        _fittedVisualSignature = visualSignature;
        DispatcherQueue.TryEnqueue(() =>
        {
            DeviceCanvas.FitToView();
            DispatcherQueue.TryEnqueue(() => DeviceCanvas.FitToView());
        });
    }

    private void RenderPreview()
    {
#if DEBUG
        if (_studioBenchmarkOwnsCanvas || _studioSelectionQa) return;
#endif
        if (_viewModel is null) return;
        if (DeviceCanvas.Visibility == Visibility.Visible)
        {
            DeviceCanvas.SetFrames(_viewModel.RenderDeviceFrames(_clock.Elapsed));
            StudioTimingMarkFrameApplied();
            return;
        }

        var frame = _viewModel.RenderPreview(_clock.Elapsed);
        BuildPreview();
        for (var index = 0; index < frame.Length && index < _brushes.Length; index++)
        {
            var color = frame[index];
            _brushes[index].Color = Color.FromArgb(255, color.R, color.G, color.B);
        }
    }

    private void SelectAllClick(object sender, RoutedEventArgs e) => DeviceCanvas.SelectAll();

    private void ApplySelectionClick(object sender, RoutedEventArgs e) =>
        _viewModel?.ApplySelectionToLayer(DeviceCanvas.Selection);

    private void ClearSelectionClick(object sender, RoutedEventArgs e) => DeviceCanvas.ClearSelection();

    /// <summary>Title boxes bind straight to the model, so the undo point lands on commit.</summary>
    private void NameBoxLostFocus(object sender, RoutedEventArgs e) => _viewModel?.NotifyEdited();

    private void ZoomInClick(object sender, RoutedEventArgs e) => DeviceCanvas.ZoomStepUp();

    private void RotateDeviceClick(object sender, RoutedEventArgs e)
    {
        var instanceId = _viewModel?.SelectedDevice?.EffectiveInstanceId;
        if (string.IsNullOrEmpty(instanceId)) return;
        var placed = DeviceCanvas.TryGetPlacement(instanceId, out var left, out var top);
        _viewModel?.RotateSelectedDevice(placed ? left : 5000, placed ? top : 5000);
    }

    private void ZoomOutClick(object sender, RoutedEventArgs e) => DeviceCanvas.ZoomStepDown();

    private void TogglePlayClick(object sender, RoutedEventArgs e)
    {
        if (_viewModel is null) return;
        _viewModel.TogglePlayback();
        UpdatePlayButton();
        if (_viewModel.IsPlaying)
        {
            _clock.Restart();
            _timer.Start();
        }
        else
        {
            _timer.Stop();
            RenderPreview();
        }
    }

    private void UpdatePlayButton()
    {
        PlayButton.Content = AppStrings.Text(_viewModel?.IsPlaying == true ? "LightingStudioPause" : "LightingStudioPlay");
        PlayButton.HorizontalContentAlignment = HorizontalAlignment.Center;
    }

    private async void ApplyClick(object sender, RoutedEventArgs e)
    {
        if (_viewModel is null) return;
        await _viewModel.ApplyAsync();
    }

    private async void StopClick(object sender, RoutedEventArgs e)
    {
        if (_viewModel is null) return;
        await _viewModel.StopAsync();
    }

    private void InspectorValueChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e) =>
        _viewModel?.NotifyEdited();

    private void AddLayerClick(object sender, RoutedEventArgs e) => _viewModel?.AddLayer();

    private void AddGroupClick(object sender, RoutedEventArgs e) => _viewModel?.AddGroup();

    private void DuplicateLayerClick(object sender, RoutedEventArgs e) => _viewModel?.DuplicateLayer();

    private void MoveIntoGroupClick(object sender, RoutedEventArgs e) => _viewModel?.MoveIntoGroup();

    private void MoveToRootClick(object sender, RoutedEventArgs e) => _viewModel?.MoveToRoot();

    private void LayerDragStarting(object sender, DragItemsStartingEventArgs e)
    {
        var layer = e.Items.Count == 1 ? e.Items[0] as StudioLayerItem : null;
        if (layer is null || _viewModel?.CanDragLayer(layer) != true)
        {
            e.Cancel = true;
            return;
        }

        _draggedLayer = layer;
    }

    private void LayerDragOver(object sender, DragEventArgs e)
    {
        var row = RowUnderPointer(e);
        var accepted = _draggedLayer is { } layer &&
            (row is { IsGroup: true }
                ? _viewModel?.CanDropIntoGroup(layer, row.Layer) == true
                : _viewModel?.CanDropToRoot(layer) == true);
        e.AcceptedOperation = accepted ? DataPackageOperation.Move : DataPackageOperation.None;
        SetDropTarget(accepted && row is { IsGroup: true } ? row : null);
        e.Handled = true;
    }

    private void LayerDrop(object sender, DragEventArgs e)
    {
        var layer = _draggedLayer;
        var row = RowUnderPointer(e);
        ClearDropTarget();
        _draggedLayer = null;
        e.Handled = true;
        if (layer is null || _viewModel is not { } viewModel) return;

        var moved = row is { IsGroup: true }
            ? viewModel.MoveLayerIntoGroup(layer, row.Layer)
            : viewModel.MoveLayerToRoot(layer);
        e.AcceptedOperation = moved ? DataPackageOperation.Move : DataPackageOperation.None;
    }

    private void LayerDragCompleted(object sender, DragItemsCompletedEventArgs args)
    {
        ClearDropTarget();
        _draggedLayer = null;
    }

    private StudioLayerRow? RowUnderPointer(DragEventArgs e)
    {
        var point = e.GetPosition(LayerList);
        for (var index = 0; index < LayerList.Items.Count; index++)
        {
            if (LayerList.ContainerFromIndex(index) is not ListViewItem container) continue;
            var bounds = container.TransformToVisual(LayerList)
                .TransformBounds(new Rect(0, 0, container.ActualWidth, container.ActualHeight));
            if (bounds.Contains(point)) return container.DataContext as StudioLayerRow;
        }

        return null;
    }

    private void SetDropTarget(StudioLayerRow? row)
    {
        if (ReferenceEquals(row, _dropRow)) return;
        ClearDropTarget();
        _dropRow = row;
        if (row is null || LayerList.ContainerFromItem(row) is not ListViewItem container) return;
        _dropHint = FindDropHint(container);
        if (_dropHint is not null) _dropHint.BorderBrush = (Brush)Application.Current.Resources["AccentBrush"];
    }

    private void ClearDropTarget()
    {
        if (_dropHint is not null) _dropHint.BorderBrush = null;
        _dropHint = null;
        _dropRow = null;
    }

    private static Border? FindDropHint(DependencyObject root)
    {
        if (root is Border { Tag: "RowDropHint" } hint) return hint;
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            if (FindDropHint(VisualTreeHelper.GetChild(root, index)) is { } found) return found;
        }

        return null;
    }

    private void AddProfileClick(object sender, RoutedEventArgs e) => _viewModel?.AddProfile();

    private void DuplicateProfileClick(object sender, RoutedEventArgs e) => _viewModel?.DuplicateProfile();

    private void RemoveProfileClick(object sender, RoutedEventArgs e) => _viewModel?.RemoveProfile();

    private void UndoClick(object sender, RoutedEventArgs e) => _viewModel?.Undo();

    private void RedoClick(object sender, RoutedEventArgs e) => _viewModel?.Redo();

    private void ToolChanged(object sender, SelectionChangedEventArgs e)
    {
        // The toolbar may initialize before the canvas it controls.
        if (DeviceCanvas is null) return;
        DeviceCanvas.Tool = ToolPicker.SelectedIndex switch
        {
            1 => StudioCanvasTool.Pen,
            2 => StudioCanvasTool.Bucket,
            3 => StudioCanvasTool.Move,
            _ => StudioCanvasTool.Select,
        };
    }

    private void ZoomStepChanged(object sender, SelectionChangedEventArgs e)
    {
        // The toolbar may initialize before the canvas it controls.
        if (sender is not ComboBox picker || DeviceCanvas is null) return;
        if (ReferenceEquals(picker.SelectedItem, _customZoomItem)) return;
        if (picker.SelectedIndex >= 0) DeviceCanvas.SetZoomStep(picker.SelectedIndex);
    }

    private void SyncZoomPicker()
    {
        var step = DeviceCanvas.ZoomStepIndex;
        if (step >= 0)
        {
            if (_customZoomItem is not null)
            {
                // Removing the selected item leaves the ComboBox in a broken state.
                if (ReferenceEquals(ZoomPicker.SelectedItem, _customZoomItem)) ZoomPicker.SelectedIndex = -1;
                ZoomPicker.Items.Remove(_customZoomItem);
                _customZoomItem = null;
            }
            if (ZoomPicker.SelectedIndex != step) ZoomPicker.SelectedIndex = step;
            return;
        }

        _customZoomItem ??= new ComboBoxItem();
        _customZoomItem.Content = $"{DeviceCanvas.ZoomPercent:0.#}%";
        if (!ZoomPicker.Items.Contains(_customZoomItem)) ZoomPicker.Items.Add(_customZoomItem);
        if (!ReferenceEquals(ZoomPicker.SelectedItem, _customZoomItem)) ZoomPicker.SelectedItem = _customZoomItem;
    }

    private void OnDeviceMoved(string instanceId, double x, double y) =>
        _viewModel?.MoveDevice(instanceId, x, y);

    private void RemoveLayerClick(object sender, RoutedEventArgs e) => _viewModel?.RemoveLayer();

    private void MoveUpClick(object sender, RoutedEventArgs e) => _viewModel?.MoveLayerUp();

    private void MoveDownClick(object sender, RoutedEventArgs e) => _viewModel?.MoveLayerDown();

    private async void SaveClick(object sender, RoutedEventArgs e)
    {
        if (_viewModel is null) return;
        await _viewModel.SaveAsync();
    }

    private async void LoadClick(object sender, RoutedEventArgs e)
    {
        if (_viewModel is null) return;
        await _viewModel.LoadAsync();
        BindInspector();
        RenderPreview();
    }

    private async void ImportChromaClick(object sender, RoutedEventArgs e)
    {
        if (_viewModel is null) return;
        var picker = new FileOpenPicker
        {
            SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
        };
        picker.FileTypeFilter.Add(OpenSynapse.Core.Lighting.StudioChromaFileService.Extension);
        picker.FileTypeFilter.Add(".json");
        picker.FileTypeFilter.Add(".zip");
        picker.FileTypeFilter.Add(".xml");
        Host.InitializePicker(picker);
        var file = await picker.PickSingleFileAsync();
        if (file is not null && !string.IsNullOrWhiteSpace(file.Path))
        {
            var imported = await _viewModel.ImportChromaAsync(file.Path);
            RefreshCanvas();
            RenderPreview();
            if (imported is { Issues.Count: > 0 })
            {
                await ShowImportIssuesAsync(imported);
            }
        }
    }

    /// <summary>Chroma shows what import changed; a bare count hides dropped or remapped devices.</summary>
    private async Task ShowImportIssuesAsync(OpenSynapse.Core.Lighting.StudioImportResult result)
    {
        var lines = result.Issues
            .GroupBy(issue => issue.Kind)
            .Select(group => AppStrings.FormatText(
                "LightingStudioImportIssueLine",
                AppStrings.Text($"LightingStudioImportIssue{group.Key}"),
                group.Count()))
            .ToArray();
        var dialog = new ContentDialog
        {
            Title = AppStrings.FormatText("LightingStudioImportIssuesTitle", result.Issues.Count),
            Content = new TextBlock
            {
                Text = string.Join(Environment.NewLine, lines),
                TextWrapping = TextWrapping.Wrap,
            },
            CloseButtonText = AppStrings.Text("LightingStudioImportIssuesClose"),
            XamlRoot = XamlRoot,
        };
        await dialog.ShowAsync();
    }

    private async void ExportChromaClick(object sender, RoutedEventArgs e)
    {
        if (_viewModel is null) return;
        var profileIds = await PickExportProfilesAsync();
        if (profileIds is null) return;
        var picker = new FileSavePicker
        {
            SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
            SuggestedFileName = "chroma-studio-profiles",
        };
        picker.FileTypeChoices.Add(
            "Chroma Effects",
            [OpenSynapse.Core.Lighting.StudioChromaFileService.Extension]);
        Host.InitializePicker(picker);
        var file = await picker.PickSaveFileAsync();
        if (file is not null && !string.IsNullOrWhiteSpace(file.Path))
        {
            _viewModel.ExportChroma(file.Path, profileIds);
        }
    }

    /// <summary>Chroma exports the profiles the user ticks; a single profile needs no dialog.</summary>
    private async Task<IReadOnlyList<string>?> PickExportProfilesAsync()
    {
        if (_viewModel is null) return null;
        var profiles = _viewModel.Profiles;
        if (profiles.Count <= 1) return profiles.Select(profile => profile.Id).ToArray();
        var boxes = profiles
            .Select(profile => new CheckBox { Content = profile.Title, IsChecked = true, Tag = profile.Id })
            .ToArray();
        var panel = new StackPanel { Spacing = 6 };
        foreach (var box in boxes) panel.Children.Add(box);
        var dialog = new ContentDialog
        {
            Title = AppStrings.Text("LightingStudioExportProfilesTitle"),
            Content = new ScrollViewer { Content = panel, MaxHeight = 360 },
            PrimaryButtonText = AppStrings.Text("LightingStudioExportProfilesConfirm"),
            CloseButtonText = AppStrings.Text("LightingStudioImportIssuesClose"),
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = XamlRoot,
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return null;
        var ids = boxes.Where(box => box.IsChecked == true).Select(box => (string)box.Tag).ToArray();
        return ids.Length == 0 ? null : ids;
    }

    private void NewClick(object sender, RoutedEventArgs e)
    {
        _viewModel?.NewDocument();
        BindInspector();
        RenderPreview();
    }

    private void BindInspector()
    {
        Inspector.Parameters = _viewModel?.SelectedParams;
        if (Host is not null) Inspector.CaptureRegion = Host.BehaviorSettings.AmbientCaptureRegion;
        Inspector.Refresh();
    }

}
