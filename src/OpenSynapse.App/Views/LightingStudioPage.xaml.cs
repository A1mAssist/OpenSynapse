using System.Diagnostics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using OpenSynapse.App.ViewModels;
using OpenSynapse.Core.Devices;
using OpenSynapse.Core.Lighting;
using OpenSynapse.Windows.Lighting;
using Windows.ApplicationModel.DataTransfer;
using Windows.Foundation;
using Windows.Storage.Pickers;
using Windows.UI;

namespace OpenSynapse.App.Views;

public sealed partial class LightingStudioPage : UserControl
{
    private const double CellSize = 28;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(33) };
    private readonly Stopwatch _clock = new();
    private Rectangle[] _cells = [];
    private SolidColorBrush[] _brushes = [];
    private (int Rows, int Columns) _previewSize;
    private LightingStudioViewModel? _viewModel;
    private StudioLayerItem? _draggedLayer;
    private StudioLayerRow? _dropRow;
    private Border? _dropHint;
    private ComboBoxItem? _customZoomItem;

    internal MainWindow Host { get; set; } = null!;

    public LightingStudioPage()
    {
        InitializeComponent();
        // ListViewBase has its own drag plumbing, so keep receiving these even if it handles them.
        LayerList.AddHandler(DragOverEvent, new DragEventHandler(LayerDragOver), true);
        LayerList.AddHandler(DropEvent, new DragEventHandler(LayerDrop), true);
        _timer.Tick += (_, _) => RenderPreview();
        Unloaded += (_, _) => _timer.Stop();
    }

    internal LightingStudioViewModel? ViewModel => _viewModel;

    internal void Initialize(
        IBladeLightingController? lighting,
        Func<IReadOnlyList<DeviceDescriptor>> devices,
        Func<IReadOnlyList<StudioDeviceOption>>? matrixDevices = null,
        StudioHidOutputRouter? hidRouter = null,
        StudioInputSampler? inputSampler = null)
    {
        _viewModel?.Dispose();
        _viewModel = new LightingStudioViewModel(lighting, devices, matrixDevices, hidRouter, inputSampler);
        _viewModel.RefreshDevices();
        _viewModel.DocumentChanged += (_, _) => RenderPreview();
        _viewModel.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(LightingStudioViewModel.DeviceVisual) ||
                args.PropertyName == nameof(LightingStudioViewModel.DeviceVisuals)) RefreshCanvas();
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
        DeviceCanvas.ZoomChanged += (_, _) => SyncZoomPicker();
        UpdatePlayButton();
        BindInspector();
        RefreshCanvas();
        RenderPreview();
    }

    internal void OnNavigatedTo()
    {
        _viewModel?.RefreshDevices();
        _viewModel?.EnsureLayerRows();
        if (_viewModel?.IsPlaying == true)
        {
            _clock.Restart();
            _timer.Start();
        }

        RenderPreview();
    }

    internal void OnNavigatedFrom() => _timer.Stop();

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
        if (_viewModel is null) return;
        var visuals = _viewModel.DeviceVisuals;
        if (visuals.Count == 0)
        {
            DeviceCanvas.Visibility = Visibility.Collapsed;
            FallbackPreview.Visibility = Visibility.Visible;
            return;
        }

        DeviceCanvas.Devices = visuals.ToArray();
        DeviceCanvas.Profile = _viewModel.Profile;
        DeviceCanvas.Refresh();
        DeviceCanvas.Visibility = Visibility.Visible;
        FallbackPreview.Visibility = Visibility.Collapsed;
        DeviceCanvas.UpdateLayout();
        DispatcherQueue.TryEnqueue(() => DeviceCanvas.FitToView());
    }

    private void RenderPreview()
    {
        if (_viewModel is null) return;
        if (DeviceCanvas.Visibility == Visibility.Visible)
        {
            DeviceCanvas.SetFrames(_viewModel.RenderDeviceFrames(_clock.Elapsed));
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

    private void SelectAllClick(object sender, RoutedEventArgs e)
    {
        if (_viewModel?.DeviceVisual is not { } visual) return;
        DeviceCanvas.SelectLeds(visual.Leds.Select(led =>
            new OpenSynapse.Core.Lighting.StudioLedRef(led.MatrixRow, led.MatrixCol)));
    }

    private void ApplySelectionClick(object sender, RoutedEventArgs e) =>
        _viewModel?.ApplySelectionToLayer(DeviceCanvas.Selection);

    private void ClearRegionClick(object sender, RoutedEventArgs e) => _viewModel?.ClearRegions();

    private void ZoomInClick(object sender, RoutedEventArgs e) =>
        DeviceCanvas.SetZoom(DeviceCanvas.ZoomPercent * 1.25);

    private void ZoomOutClick(object sender, RoutedEventArgs e) =>
        DeviceCanvas.SetZoom(DeviceCanvas.ZoomPercent / 1.25);

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
        if (ReferenceEquals(ZoomPicker.SelectedItem, _customZoomItem)) return;
        if (ZoomPicker.SelectedIndex >= 0) DeviceCanvas.SetZoomStep(ZoomPicker.SelectedIndex);
    }

    private void SyncZoomPicker()
    {
        var step = DeviceCanvas.ZoomStepIndex;
        if (step >= 0)
        {
            if (_customZoomItem is not null)
            {
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
            await _viewModel.ImportChromaAsync(file.Path);
            RefreshCanvas();
            RenderPreview();
        }
    }

    private async void ExportChromaClick(object sender, RoutedEventArgs e)
    {
        if (_viewModel is null) return;
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
            _viewModel.ExportChroma(file.Path);
        }
    }

    private void NewClick(object sender, RoutedEventArgs e)
    {
        _viewModel?.NewDocument();
        BindInspector();
        RenderPreview();
    }

    private void BindInspector() => Inspector.Parameters = _viewModel?.SelectedParams;
}
