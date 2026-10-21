using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using OpenSynapse.App.ViewModels;
using System.Runtime.InteropServices;
using Windows.Graphics;
using Windows.UI;

namespace OpenSynapse.App;

/// <summary>
/// Hosts the macro editor in its own window, so the on-screen keyboard can be
/// drawn at full size instead of squeezed into a flyout on the macro page.
/// </summary>
public sealed partial class MacroEditorWindow : Window
{
    private const int ShowWindowRestore = 9;

    // The board is drawn in quarter-key units, so one standard key is four
    // columns wide and one row tall.
    private const double MacroKeyUnitWidth = 10.6;
    private const double MacroKeyUnitHeight = 36;
    private const double MacroKeyGap = 3;

    private readonly MainViewModel _viewModel;
    private readonly CancellationTokenSource _lifetime = new();

    public MacroEditorWindow(AppWindow owner, MainViewModel viewModel, double scale)
    {
        InitializeComponent();
        _viewModel = viewModel;
        RootLayout.DataContext = viewModel;
        SystemBackdrop = new MicaBackdrop();
        var appIconPath = System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "OpenSynapse.ico");
        if (File.Exists(appIconPath))
        {
            AppWindow.SetIcon(appIconPath);
        }

        ConfigurePresenter(scale);
        ApplyTitleBarColors();
        PositionOnOwnerDisplay(owner, scale);
        BuildMacroKeyboard();
        BuildMacroMouse();
        RefreshLocalization();
    }

    internal nint Handle => WinRT.Interop.WindowNative.GetWindowHandle(this);

    internal void BringToFront()
    {
        var windowHandle = WinRT.Interop.WindowNative.GetWindowHandle(this);
        if (IsIconic(windowHandle))
        {
            ShowWindow(windowHandle, ShowWindowRestore);
        }

        Activate();
        SetForegroundWindow(windowHandle);
    }

    /// <summary>
    /// A recording or a capture outlives the window unless it is canceled, so
    /// the window stops both when it closes. The take is discarded rather than
    /// committed: closing without pressing save must not write it.
    /// </summary>
    internal void CancelPendingCapture()
    {
        _viewModel.CancelMacroCapture();
        _viewModel.CancelMacroStepCapture();
        _viewModel.DiscardMacroRecording();
    }

    private void ConfigurePresenter(double scale)
    {
        if (AppWindow.Presenter is not OverlappedPresenter presenter)
        {
            return;
        }

        presenter.PreferredMinimumWidth = (int)Math.Round(1240 * scale);
        presenter.PreferredMinimumHeight = (int)Math.Round(760 * scale);
    }

    private void ApplyTitleBarColors()
    {
        if (!AppWindowTitleBar.IsCustomizationSupported())
        {
            return;
        }

        var titleBar = AppWindow.TitleBar;
        var background = Color.FromArgb(255, 0x20, 0x20, 0x20);
        var foreground = Color.FromArgb(255, 0xF4, 0xF4, 0xF4);
        titleBar.BackgroundColor = background;
        titleBar.ForegroundColor = foreground;
        titleBar.InactiveBackgroundColor = background;
        titleBar.InactiveForegroundColor = foreground;
        titleBar.ButtonBackgroundColor = background;
        titleBar.ButtonForegroundColor = foreground;
        titleBar.ButtonHoverBackgroundColor = Color.FromArgb(255, 0x38, 0x38, 0x38);
        titleBar.ButtonHoverForegroundColor = foreground;
        titleBar.ButtonPressedBackgroundColor = Color.FromArgb(255, 0x4C, 0x4C, 0x4C);
        titleBar.ButtonPressedForegroundColor = foreground;
    }

    private void PositionOnOwnerDisplay(AppWindow owner, double scale)
    {
        var workArea = DisplayArea.GetFromWindowId(owner.Id, DisplayAreaFallback.Primary).WorkArea;
        var width = Math.Min((int)Math.Round(1480 * scale), workArea.Width);
        var height = Math.Min((int)Math.Round(880 * scale), workArea.Height);
        AppWindow.MoveAndResize(new RectInt32(
            workArea.X + Math.Max(0, (workArea.Width - width) / 2),
            workArea.Y + Math.Max(0, (workArea.Height - height) / 2),
            width,
            height));
    }

    /// <summary>
    /// Draws the whole board as one grid, so the function row, the typing
    /// area, the navigation cluster and the number pad line up the way they
    /// sit on the desk.
    /// </summary>
    private void BuildMacroKeyboard()
    {
        var picker = _viewModel.MacroKeyPicker;
        var grid = new Grid();
        for (var column = 0; column < picker.Columns; column++)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition
            {
                Width = new GridLength(MacroKeyUnitWidth),
            });
        }

        for (var row = 0; row < picker.Rows; row++)
        {
            grid.RowDefinitions.Add(new RowDefinition
            {
                Height = new GridLength(MacroKeyUnitHeight),
            });
        }

        foreach (var slot in picker.Slots)
        {
            var button = CreateMacroKeyButton(slot.Key);
            Grid.SetColumn(button, slot.Column);
            Grid.SetRow(button, slot.Row);
            Grid.SetColumnSpan(button, slot.ColumnSpan);
            Grid.SetRowSpan(button, slot.RowSpan);
            grid.Children.Add(button);
        }

        MacroKeyboardHost.Children.Clear();
        MacroKeyboardHost.Children.Add(grid);
        MacroKeyboardScroll.SizeChanged += (_, _) => ResizeMacroKeyboard();
    }

    /// <summary>
    /// The board is drawn in quarter-key units, so it is scaled to the width
    /// the window actually has. Without this the number pad falls off the
    /// right edge on a narrow window.
    /// </summary>
    private void ResizeMacroKeyboard()
    {
        if (MacroKeyboardHost.Children.Count == 0)
        {
            return;
        }

        var available = MacroKeyboardScroll.ViewportWidth;
        if (available <= 0)
        {
            return;
        }

        var picker = _viewModel.MacroKeyPicker;
        var boardWidth = picker.Columns * MacroKeyUnitWidth;
        var scale = Math.Clamp(available / boardWidth, 0.6, 1.4);
        var unit = MacroKeyUnitWidth * scale;
        var row = MacroKeyUnitHeight * scale;

        var grid = (Grid)MacroKeyboardHost.Children[0];
        for (var column = 0; column < grid.ColumnDefinitions.Count; column++)
        {
            grid.ColumnDefinitions[column].Width = new GridLength(unit);
        }

        for (var index = 0; index < grid.RowDefinitions.Count; index++)
        {
            grid.RowDefinitions[index].Height = new GridLength(row);
        }

        foreach (var child in grid.Children)
        {
            if (child is Button button)
            {
                button.FontSize = Math.Round(12 * scale, 1);
            }
        }
    }

    /// <summary>
    /// Draws the pick list as a mouse: left and right click on the two halves
    /// of the shell, the wheel halves on the wheel, the thumb buttons on the
    /// flank, the way they sit on the device. The region name appears beside
    /// the shape on hover instead of being crushed into it.
    /// </summary>
    private void BuildMacroMouse()
    {
        var picker = _viewModel.MacroKeyPicker;
        var canvas = new Canvas { Width = MacroMouseHost.Width, Height = MacroMouseHost.Height };

        var shell = new Border
        {
            Width = 62,
            Height = 92,
            CornerRadius = new CornerRadius(30, 30, 26, 26),
            Background = new SolidColorBrush(Color.FromArgb(0xFF, 0x2A, 0x2A, 0x2A)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0xFF, 0x4C, 0x4C, 0x4C)),
            BorderThickness = new Thickness(1),
        };
        Canvas.SetLeft(shell, 44);
        Canvas.SetTop(shell, 4);
        canvas.Children.Add(shell);

        var split = new Rectangle
        {
            Width = 60,
            Height = 1,
            Fill = new SolidColorBrush(Color.FromArgb(0xFF, 0x4C, 0x4C, 0x4C)),
        };
        Canvas.SetLeft(split, 45);
        Canvas.SetTop(split, 46);
        canvas.Children.Add(split);

        canvas.Children.Add(Region(picker.MouseButtons[0], 45, 5, 30, 40));
        canvas.Children.Add(Region(picker.MouseButtons[1], 75, 5, 30, 40));
        canvas.Children.Add(Region(picker.Wheels[0], 71, 22, 7, 15));
        canvas.Children.Add(Region(picker.Wheels[1], 71, 37, 7, 15));
        canvas.Children.Add(Region(picker.MouseButtons[2], 58, 50, 34, 20));
        canvas.Children.Add(Region(picker.MouseButtons[3], 22, 42, 22, 19));
        canvas.Children.Add(Region(picker.MouseButtons[4], 22, 63, 22, 19));

        MacroMouseHost.Children.Clear();
        MacroMouseHost.Children.Add(canvas);
    }

    private Border Region(MacroKeyOptionViewModel option, double left, double top, double width, double height)
    {
        var button = new Button
        {
            Style = (Style)Application.Current.Resources["IconButtonStyle"],
            Width = width,
            Height = height,
            MinWidth = 0,
            MinHeight = 0,
            Padding = new Thickness(0),
            Margin = new Thickness(0),
            CornerRadius = new CornerRadius(4),
            Background = new SolidColorBrush(Color.FromArgb(0x00, 0x99, 0xDD, 0x72)),
            Tag = option,
        };
        AutomationProperties.SetName(button, option.Label);
        ToolTipService.SetToolTip(button, option.Label);
        button.Click += MacroKeyOptionClick;
        button.PointerEntered += (_, _) =>
        {
            MacroMouseCaption.Text = option.Label;
            button.Background = new SolidColorBrush(Color.FromArgb(0x66, 0x99, 0xDD, 0x72));
        };
        button.PointerExited += (_, _) =>
            button.Background = new SolidColorBrush(Color.FromArgb(0x00, 0x99, 0xDD, 0x72));

        var host = new Border { Child = button };
        Canvas.SetLeft(host, left);
        Canvas.SetTop(host, top);
        return host;
    }

    private Button CreateMacroKeyButton(MacroKeyOptionViewModel key)
    {
        var button = new Button
        {
            Style = (Style)Application.Current.Resources["SecondaryButtonStyle"],
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Stretch,
            Margin = new Thickness(0, 0, MacroKeyGap, MacroKeyGap),
            Padding = new Thickness(2, 0, 2, 0),
            MinHeight = 0,
            MinWidth = 0,
            FontSize = 12,
            Tag = key,
        };
        AutomationProperties.SetName(button, key.Label);
        button.Content = key.Label;
        button.Click += MacroKeyOptionClick;
        return button;
    }

    internal void RefreshLocalization()
    {
        Localized.RefreshTree(RootLayout);
        Title = AppStrings.Text("MacroWindowTitle");
    }

    private async void MacroRecordStartClick(object sender, RoutedEventArgs e)
    {
        if (_viewModel.IsMacroRecording)
        {
            await _viewModel.ToggleMacroRecordingAsync(_lifetime.Token);
        }
    }

    private async void MacroRecordStopClick(object sender, RoutedEventArgs e)
    {
        if (_viewModel.IsMacroRecording)
        {
            await _viewModel.ToggleMacroRecordingAsync(_lifetime.Token);
        }
    }

    private void MacroRecordDelaysClick(object sender, RoutedEventArgs e)
    {
        if (sender is ToggleMenuFlyoutItem item)
        {
            _viewModel.IsMacroRecordingDelays = item.IsChecked;
        }
    }

    private async void MacroInsertCaptureClick(object sender, RoutedEventArgs e) =>
        await _viewModel.AddMacroInputStepAsync(_lifetime.Token);

    private async void AddMacroDelayClick(object sender, RoutedEventArgs e) =>
        await _viewModel.AddMacroDelayAsync(_lifetime.Token);

    private async void MacroKeyOptionClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: MacroKeyOptionViewModel option })
        {
            return;
        }

        _viewModel.MacroKeyPicker.Selected = option;
        await _viewModel.AddMacroStepFromPickerAsync(_lifetime.Token);
    }

    private void MacroPropertiesHeaderClick(object sender, RoutedEventArgs e)
    {
        var expanded = MacroPropertiesPanel.Visibility == Visibility.Visible;
        MacroPropertiesPanel.Visibility = expanded ? Visibility.Collapsed : Visibility.Visible;
        MacroPropertiesChevron.Glyph = expanded ? "\uE70D" : "\uE70E";
    }

    private async void CaptureMacroTriggerClick(object sender, RoutedEventArgs e) =>
        await _viewModel.CaptureMacroTriggerAsync(_lifetime.Token);

    private async void PlayMacroClick(object sender, RoutedEventArgs e) =>
        await _viewModel.PlaySelectedMacroAsync(_lifetime.Token);

    private async void SaveMacroClick(object sender, RoutedEventArgs e) =>
        await _viewModel.SaveSelectedMacroAsync(_lifetime.Token);

    private async void RecaptureMacroStepClick(object sender, RoutedEventArgs e) =>
        await _viewModel.RecaptureMacroStepInputAsync(_lifetime.Token);

    private async void MoveMacroStepUpClick(object sender, RoutedEventArgs e) =>
        await _viewModel.MoveSelectedMacroStepAsync(-1, _lifetime.Token);

    private async void MoveMacroStepDownClick(object sender, RoutedEventArgs e) =>
        await _viewModel.MoveSelectedMacroStepAsync(1, _lifetime.Token);

    private async void DeleteMacroStepClick(object sender, RoutedEventArgs e) =>
        await _viewModel.DeleteSelectedMacroStepAsync(_lifetime.Token);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindow(nint windowHandle, int command);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsIconic(nint windowHandle);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(nint windowHandle);
}
