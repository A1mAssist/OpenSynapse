using System.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using OpenSynapse.App.ViewModels;
using OpenSynapse.Core.Devices;
using OpenSynapse.Core.Lighting;
using Windows.UI;
using Path = Microsoft.UI.Xaml.Shapes.Path;

namespace OpenSynapse.App.Views;

public sealed partial class AnalogKeyEditor : UserControl
{
    private RazerHidDeviceViewModel? _viewModel;
    private readonly Dictionary<byte, Path> _pathsByKeyId = [];
    private readonly Dictionary<byte, AnalogKeyKeyViewModel> _keysById = [];
    private readonly List<AnalogKeyKeyViewModel> _subscribedKeys = [];

    public AnalogKeyEditor()
    {
        InitializeComponent();
        DataContextChanged += EditorDataContextChanged;
        Loaded += EditorLoaded;
        Unloaded += EditorUnloaded;
    }

    private void EditorDataContextChanged(FrameworkElement sender, DataContextChangedEventArgs args) =>
        BindViewModel();

    private void EditorLoaded(object sender, RoutedEventArgs e) => BindViewModel();

    private void EditorUnloaded(object sender, RoutedEventArgs e) => UnbindViewModel();

    private void BindViewModel()
    {
        var viewModel = DataContext as RazerHidDeviceViewModel;
        if (ReferenceEquals(_viewModel, viewModel))
        {
            return;
        }

        UnbindViewModel();
        _viewModel = viewModel;
        if (_viewModel is null)
        {
            return;
        }

        _viewModel.PropertyChanged += ViewModelPropertyChanged;
        BindKeys();
        DrawKeyboard();
    }

    private void UnbindViewModel()
    {
        UnbindKeys();
        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged -= ViewModelPropertyChanged;
            _viewModel = null;
        }

        AnalogKeyboardCanvas.Children.Clear();
        _pathsByKeyId.Clear();
    }

    private void BindKeys()
    {
        UnbindKeys();
        if (_viewModel is null)
        {
            return;
        }

        foreach (var key in _viewModel.AnalogKeyRows.SelectMany(row => row.Keys))
        {
            _keysById[key.KeyId] = key;
            _subscribedKeys.Add(key);
            key.PropertyChanged += KeyPropertyChanged;
        }
    }

    private void UnbindKeys()
    {
        foreach (var key in _subscribedKeys)
        {
            key.PropertyChanged -= KeyPropertyChanged;
        }

        _subscribedKeys.Clear();
        _keysById.Clear();
    }

    private void ViewModelPropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(RazerHidDeviceViewModel.AnalogKeyVisual))
        {
            DrawKeyboard();
        }
        else if (args.PropertyName == nameof(RazerHidDeviceViewModel.AnalogKeyRows))
        {
            BindKeys();
            DrawKeyboard();
        }
    }

    private void KeyPropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (sender is not AnalogKeyKeyViewModel key)
        {
            return;
        }

        if (args.PropertyName == nameof(AnalogKeyKeyViewModel.IsSelected))
        {
            UpdateKeyboardSelection();
        }
        else if (args.PropertyName == nameof(AnalogKeyKeyViewModel.AnalogKeyTooltipText) &&
            _pathsByKeyId.TryGetValue(key.KeyId, out var path))
        {
            AutomationProperties.SetHelpText(path, key.AnalogKeyTooltipText);
            ToolTipService.SetToolTip(path, key.AnalogKeyTooltipText);
        }
    }

    private void DrawKeyboard()
    {
        AnalogKeyboardCanvas.Children.Clear();
        _pathsByKeyId.Clear();
        if (_viewModel?.AnalogKeyVisual is not { } visual)
        {
            return;
        }

        AnalogKeyboardCanvas.Width = visual.Width;
        AnalogKeyboardCanvas.Height = visual.Height;
        var ledKeys = visual.Paths
            .Where(path => path.Role == StudioSvgRole.Led && !string.IsNullOrWhiteSpace(path.KeyName))
            .Select(path => path.KeyName!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var source in visual.Paths.OrderBy(path => StudioCanvasGeometry.RoleOrder(path.Role)))
        {
            if (source.Role == StudioSvgRole.Selection &&
                source.KeyName is { Length: > 0 } selectedKey &&
                ledKeys.Contains(selectedKey))
            {
                continue;
            }

            if (StudioCanvas.ConvertGeometry(source.Data) is not { } geometry)
            {
                continue;
            }

            var element = new Path
            {
                Data = geometry,
                IsHitTestVisible = false,
                Opacity = double.IsFinite(source.Opacity) ? Math.Clamp(source.Opacity, 0, 1) : 1,
            };
            if (StudioCanvasGeometry.ParseColor(source.Fill ?? string.Empty) is { } fill)
            {
                element.Fill = new SolidColorBrush(fill);
            }

            if (StudioCanvasGeometry.ParseColor(source.Stroke ?? string.Empty) is { } stroke)
            {
                element.Stroke = new SolidColorBrush(stroke);
                element.StrokeThickness = 1;
            }

            if (source.Role is StudioSvgRole.Led or StudioSvgRole.Selection &&
                source.KeyName is { Length: > 0 } keyName &&
                AnalogKeyboardKeyNames.TryGet(keyName, out var keyId) &&
                _keysById.TryGetValue(keyId, out var key) &&
                _pathsByKeyId.TryAdd(keyId, element))
            {
                element.IsHitTestVisible = true;
                element.Fill = new SolidColorBrush(Color.FromArgb(0, 0, 0, 0));
                element.Stroke = Application.Current.Resources["LineStrongBrush"] as Brush;
                element.StrokeThickness = 0.6;
                element.Tag = key;
                element.PointerPressed += KeyboardKeyPressed;
                AutomationProperties.SetName(element, key.Label);
                AutomationProperties.SetHelpText(element, key.AnalogKeyTooltipText);
                ToolTipService.SetToolTip(element, key.AnalogKeyTooltipText);
            }

            AnalogKeyboardCanvas.Children.Add(element);
        }

        UpdateKeyboardSelection();
    }

    private void KeyboardKeyPressed(object sender, PointerRoutedEventArgs e)
    {
        if (sender is not Path { Tag: AnalogKeyKeyViewModel key } || _viewModel is null)
        {
            return;
        }

        key.IsSelected = !key.IsSelected;
        _viewModel.SynchronizeAnalogKeySelection();
        e.Handled = true;
    }

    private void UpdateKeyboardSelection()
    {
        var accent = Application.Current.Resources["AccentBrush"] as Brush ??
            new SolidColorBrush(Color.FromArgb(255, 0x44, 0xD6, 0x2C));
        var accentColor = accent is SolidColorBrush solid
            ? solid.Color
            : Color.FromArgb(255, 0x44, 0xD6, 0x2C);
        foreach (var (keyId, element) in _pathsByKeyId)
        {
            var isSelected = _keysById.TryGetValue(keyId, out var key) && key.IsSelected;
            element.Fill = isSelected
                ? new SolidColorBrush(Color.FromArgb(72, accentColor.R, accentColor.G, accentColor.B))
                : new SolidColorBrush(Color.FromArgb(0, 0, 0, 0));
            element.Stroke = isSelected
                ? accent
                : Application.Current.Resources["LineStrongBrush"] as Brush;
            element.StrokeThickness = isSelected ? 1.8 : 0.6;
        }
    }

    private void SelectAllClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is RazerHidDeviceViewModel viewModel)
        {
            viewModel.SelectAllAnalogKeys();
        }
    }

    private void ClearSelectionClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is RazerHidDeviceViewModel viewModel)
        {
            viewModel.ClearAnalogKeySelection();
        }
    }

    private async void ApplySelectedClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is RazerHidDeviceViewModel viewModel)
        {
            await viewModel.ApplyAnalogKeyToSelectionAsync();
        }
    }

    private async void ApplyAllClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is RazerHidDeviceViewModel viewModel)
        {
            await viewModel.ApplyAnalogKeyToAllAsync();
        }
    }

    private void AddSnapTapPairClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is RazerHidDeviceViewModel viewModel)
        {
            viewModel.AddAnalogSnapTapPair();
        }
    }

    private void RemoveSnapTapPairClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is RazerHidDeviceViewModel viewModel &&
            sender is FrameworkElement { DataContext: AnalogSnapTapPairViewModel pair })
        {
            viewModel.RemoveAnalogSnapTapPair(pair);
        }
    }

    private async void ApplySnapTapClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is RazerHidDeviceViewModel viewModel)
        {
            await viewModel.ApplyAnalogSnapTapAsync();
        }
    }
}
