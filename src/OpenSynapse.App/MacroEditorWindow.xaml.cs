using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using OpenSynapse.App.ViewModels;
using OpenSynapse.Core.Macros;
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
    private const double MacroKeyUnitHeight = 30;
    private const double MacroKeyGap = 3;

    private readonly MainViewModel _viewModel;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly double _scale;
    private MousePathEditorWindow? _mousePathEditorWindow;
    private bool _allowClose;
    private bool _closeSaveInProgress;

    public MacroEditorWindow(MainViewModel viewModel, double scale)
    {
        InitializeComponent();
        _viewModel = viewModel;
        _scale = scale;
        RootLayout.DataContext = viewModel;
        if (!WindowAppearance.Apply(this))
        {
            RootLayout.Background = (Brush)Application.Current.Resources["CanvasBrush"];
        }

        var appIconPath = System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "OpenSynapse.ico");
        if (File.Exists(appIconPath))
        {
            AppWindow.SetIcon(appIconPath);
        }

        AppWindow.Closing += OnMacroEditorClosing;
        Closed += (_, _) =>
        {
            _mousePathEditorWindow?.Close();
            _mousePathEditorWindow = null;
            _lifetime.Cancel();
            _lifetime.Dispose();
        };
        // The owner can sit on a virtual display whose work area is larger than
        // the screen the user is looking at, so the editor is sized against the
        // primary display instead.
        var workArea = DisplayArea.Primary.WorkArea;
        ConfigurePresenter(scale, workArea);
        ApplyTitleBarColors();
        PositionOnOwnerDisplay(workArea, scale);
        BuildMacroKeyboard();
        BuildMacroMediaKeys();
        InitializeMacroMouse();
        RefreshLocalization();
    }

    internal nint Handle => WinRT.Interop.WindowNative.GetWindowHandle(this);

    internal bool OwnsWindow(nint handle) =>
        handle != 0 &&
        (handle == Handle || (_mousePathEditorWindow is not null && handle == _mousePathEditorWindow.Handle));

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
        _viewModel.CancelMacroPlaybackShortcutCapture();
        _viewModel.CancelMacroPlaybackCountdown();
        _viewModel.DiscardMacroRecording();
    }

    private void ConfigurePresenter(double scale, RectInt32 workArea)
    {
        if (AppWindow.Presenter is not OverlappedPresenter presenter)
        {
            return;
        }

        // The three property columns share the space next to the action list, so
        // about 1080 logical pixels keeps every column usable. Cap both
        // dimensions so the status bar and keyboard stay reachable on small
        // displays.
        presenter.PreferredMinimumWidth = Math.Min(
            (int)Math.Round(1080 * scale),
            Math.Max(1, workArea.Width));
        presenter.PreferredMinimumHeight = Math.Min(
            (int)Math.Round(680 * scale),
            Math.Max(1, workArea.Height));
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

    private void PositionOnOwnerDisplay(RectInt32 workArea, double scale)
    {
        // The editor shows the action list, three property columns and the whole
        // keyboard at once. It opens at a size that fits all of them without
        // covering the whole display, and the columns shrink together when the
        // user resizes it narrower. The work area is in physical pixels, so the
        // requested logical size is clamped against the logical work area.
        var logicalWorkAreaWidth = workArea.Width / scale;
        var logicalWorkAreaHeight = workArea.Height / scale;
        var width = (int)Math.Round(Math.Min(1080, logicalWorkAreaWidth) * scale);
        var height = (int)Math.Round(Math.Min(760, logicalWorkAreaHeight) * scale);
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
        MacroKeyboardScroll.SizeChanged += (_, _) =>
            MacroKeyboardScroll.DispatcherQueue.TryEnqueue(ResizeMacroKeyboard);
        RootLayout.Loaded += (_, _) => ResizeMacroKeyboard();
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

        // Leave room for the ScrollViewer's own padding and the key margin on the right.
        var available = Math.Max(0, MacroKeyboardScroll.ActualWidth - 24);
        if (available <= 0)
        {
            return;
        }

        var picker = _viewModel.MacroKeyPicker;
        var boardWidth = picker.Columns * MacroKeyUnitWidth;
        // The board is capped so the editor chrome above it keeps its room; a
        // taller keyboard would push the property column into a scrollbar.
        var scale = Math.Clamp(available / boardWidth, 0.6, 0.92);
        var unit = MacroKeyUnitWidth * scale;
        var row = MacroKeyUnitHeight * scale;

        var grid = (Grid)MacroKeyboardHost.Children[0];
        grid.Width = boardWidth * scale;
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

    private static readonly SolidColorBrush MouseWheelFill = new(Color.FromArgb(0xFF, 0x24, 0x24, 0x24));
    private static readonly SolidColorBrush MouseWheelHoverFill = new(Color.FromArgb(0xFF, 0x32, 0x45, 0x2F));
    private static readonly SolidColorBrush MouseWheelStroke = new(Color.FromArgb(0xFF, 0x5C, 0x5C, 0x5C));
    private static readonly SolidColorBrush MouseWheelHoverStroke = new(Color.FromArgb(0xFF, 0x99, 0xDD, 0x72));

    /// <summary>
    /// Binds the drawn mouse to the picker. The art is static, so each region
    /// carries its option index in XAML; swapping that for the option here lets
    /// the shared click handler and the hover caption work off the real data.
    /// </summary>
    private void BuildMacroMediaKeys()
    {
        MacroMediaKeyHost.Children.Clear();
        foreach (var option in _viewModel.MacroKeyPicker.MediaKeys)
        {
            var button = new Button
            {
                Content = option.Label,
                Tag = option,
                Style = (Style)Application.Current.Resources["SecondaryButtonStyle"],
                Height = 30,
                MinWidth = 54,
                Padding = new Thickness(8, 0, 8, 0),
                FontSize = 12,
            };
            AutomationProperties.SetName(button, option.Label);
            ToolTipService.SetToolTip(button, option.Label);
            button.Click += MacroKeyOptionClick;
            MacroMediaKeyHost.Children.Add(button);
        }
    }

    private void InitializeMacroMouse()
    {
        var picker = _viewModel.MacroKeyPicker;
        foreach (var button in MacroMouseHost.Children.OfType<Button>()
            .Concat(MacroMouseExtraHost.Children.OfType<Button>()))
        {
            if (button.Tag is not string rawIndex || !int.TryParse(rawIndex, out var index))
            {
                continue;
            }

            var option = index < picker.MouseButtons.Count
                ? picker.MouseButtons[index]
                : picker.Wheels[index - picker.MouseButtons.Count];
            button.Tag = option;
            var description = MacroRowViewModel.DescribeInput(option.Input);
            AutomationProperties.SetName(button, description);
            ToolTipService.SetToolTip(button, description);
            // Hover and keyboard focus share one cue: the region lights up. That
            // keeps focus visible without the square focus rectangle the shape
            // does not want.
            button.PointerEntered += (_, _) => RefreshMacroMouseHover();
            button.PointerExited += (_, _) => RefreshMacroMouseHover();
            button.GotFocus += (_, _) => RefreshMacroMouseHover();
            button.LostFocus += (_, _) => RefreshMacroMouseHover();
        }

        RefreshMacroMouseHover();
    }

    private void RefreshMacroMouseHover()
    {
        MacroKeyOptionViewModel? activeOption = null;
        var leftActive = false;
        var rightActive = false;
        var wheelActive = false;

        foreach (var button in MacroMouseHost.Children.OfType<Button>())
        {
            var option = button.Tag as MacroKeyOptionViewModel;
            if (option is null ||
                (!button.IsPointerOver && button.FocusState is not (FocusState.Keyboard or FocusState.Programmatic)))
            {
                continue;
            }

            activeOption ??= option;
            switch (option.Input)
            {
                case { Kind: MacroInputKind.MouseWheel }:
                case { Kind: MacroInputKind.MouseButton, MouseButton: 3 }:
                    wheelActive = true;
                    break;
                case { Kind: MacroInputKind.MouseButton, MouseButton: 1 }:
                    leftActive = true;
                    break;
                case { Kind: MacroInputKind.MouseButton, MouseButton: 2 }:
                    rightActive = true;
                    break;
            }
        }

        foreach (var button in MacroMouseExtraHost.Children.OfType<Button>())
        {
            var option = button.Tag as MacroKeyOptionViewModel;
            if (option is not null &&
                (button.IsPointerOver || button.FocusState is FocusState.Keyboard or FocusState.Programmatic))
            {
                activeOption ??= option;
            }
        }

        MacroMouseLeftHighlight.Opacity = leftActive ? 1 : 0;
        MacroMouseRightHighlight.Opacity = rightActive ? 1 : 0;
        MacroMouseWheelShape.Fill = wheelActive ? MouseWheelHoverFill : MouseWheelFill;
        MacroMouseWheelShape.Stroke = wheelActive ? MouseWheelHoverStroke : MouseWheelStroke;
        MacroMouseCaption.Text = activeOption is null
            ? string.Empty
            : MacroRowViewModel.DescribeInput(activeOption.Input);
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
        _mousePathEditorWindow?.RefreshLocalization();
    }

    private async void MacroRecordStartClick(object sender, RoutedEventArgs e)
    {
        if (_viewModel.IsMacroRecording || _viewModel.IsMacroCountdown)
        {
            await _viewModel.ToggleMacroRecordingAsync(_lifetime.Token);
            return;
        }

        if (!_viewModel.CanStartMacroRecording)
        {
            return;
        }

        var append = false;
        if (_viewModel.SelectedMacro is { } selected &&
            selected.Definition.Steps.Count > 0)
        {
            var dialog = new ContentDialog
            {
                Title = AppStrings.Text("MacroRecordOverwriteTitle"),
                Content = AppStrings.FormatText(
                    "MacroRecordOverwriteMessage",
                    selected.Name,
                    MacroStepTree.CountSteps(selected.Definition.Steps)),
                PrimaryButtonText = AppStrings.Text("MacroRecordReplace"),
                SecondaryButtonText = AppStrings.Text("MacroRecordAppend"),
                CloseButtonText = AppStrings.Text("MacroCancel"),
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = RootLayout.XamlRoot,
            };
            var result = await dialog.ShowAsync();
            if (result == ContentDialogResult.None)
            {
                return;
            }

            append = result == ContentDialogResult.Secondary;
        }

        await _viewModel.StartMacroRecordingAsync(append, _lifetime.Token);
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

    private async void CaptureMacroTriggerClick(object sender, RoutedEventArgs e) =>
        await _viewModel.CaptureMacroTriggerAsync(_lifetime.Token);

    private async void CaptureMacroPlaybackStartShortcutClick(object sender, RoutedEventArgs e) =>
        await _viewModel.CaptureMacroPlaybackStartShortcutAsync(_lifetime.Token);

    private async void CaptureMacroPlaybackStopShortcutClick(object sender, RoutedEventArgs e) =>
        await _viewModel.CaptureMacroPlaybackStopShortcutAsync(_lifetime.Token);

    private async void ClearMacroPlaybackStartShortcutClick(object sender, RoutedEventArgs e) =>
        await _viewModel.ClearMacroPlaybackStartShortcutAsync(_lifetime.Token);

    private async void ClearMacroPlaybackStopShortcutClick(object sender, RoutedEventArgs e) =>
        await _viewModel.ClearMacroPlaybackStopShortcutAsync(_lifetime.Token);

    private async void PlayMacroClick(object sender, RoutedEventArgs e) =>
        await _viewModel.PlaySelectedMacroAsync(_lifetime.Token);

    private void OpenMousePathEditorClick(object sender, RoutedEventArgs e)
    {
        if (_viewModel.SelectedMacroStep is not { IsMouseMove: true } row)
        {
            return;
        }

        if (_mousePathEditorWindow is not null)
        {
            _mousePathEditorWindow.BringToFront();
            return;
        }

        var editor = new MousePathEditorViewModel(row.Step.MouseMoveMode, row.Step.MousePath);
        var window = new MousePathEditorWindow(
            AppWindow,
            editor,
            _scale,
            points => row.ApplyMousePath(points));
        window.Closed += (_, _) =>
        {
            if (ReferenceEquals(_mousePathEditorWindow, window))
            {
                _mousePathEditorWindow = null;
            }
        };
        _mousePathEditorWindow = window;
        window.Activate();
        window.BringToFront();
    }

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

    private async void DeleteMacroStepRowClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: MacroStepRowViewModel row })
        {
            await _viewModel.DeleteMacroStepAsync(row, _lifetime.Token);
        }
    }

    private async void MacroStepListKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != global::Windows.System.VirtualKey.Delete ||
            _viewModel.SelectedMacroStep is null)
        {
            return;
        }

        e.Handled = true;
        await _viewModel.DeleteSelectedMacroStepAsync(_lifetime.Token);
    }

    private async void AddMacroTextClick(object sender, RoutedEventArgs e)
    {
        var text = new TextBox
        {
            Header = AppStrings.Text("MacroTextDialogLabel"),
            PlaceholderText = AppStrings.Text("MacroTextDialogPlaceholder"),
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            MinHeight = 120,
            MaxLength = MacroStep.MaxTextLength,
        };
        AutomationProperties.SetName(text, AppStrings.Text("MacroTextDialogLabel"));
        var dialog = new ContentDialog
        {
            Title = AppStrings.Text("MacroTextDialogTitle"),
            Content = text,
            PrimaryButtonText = AppStrings.Text("MacroAdd"),
            CloseButtonText = AppStrings.Text("MacroCancel"),
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = RootLayout.XamlRoot,
        };
        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
        {
            await _viewModel.AddMacroTextStepAsync(text.Text, _lifetime.Token);
        }
    }

    private async void AddMacroCommandClick(object sender, RoutedEventArgs e)
    {
        var command = CreateCommandTextBox("MacroStepCommandLabel/Text", MacroStep.MaxCommandLength);
        var arguments = CreateCommandTextBox("MacroStepArgumentsLabel/Text", MacroStep.MaxArgumentsLength);
        var workingDirectory = CreateCommandTextBox(
            "MacroStepWorkingDirectoryLabel/Text",
            MacroStep.MaxWorkingDirectoryLength);
        var content = new StackPanel { Spacing = 8 };
        content.Children.Add(command);
        content.Children.Add(arguments);
        content.Children.Add(workingDirectory);

        var dialog = new ContentDialog
        {
            Title = AppStrings.Text("MacroCommandDialogTitle"),
            Content = content,
            PrimaryButtonText = AppStrings.Text("MacroAdd"),
            CloseButtonText = AppStrings.Text("MacroCancel"),
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = RootLayout.XamlRoot,
        };
        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
        {
            await _viewModel.AddMacroRunCommandStepAsync(
                command.Text,
                arguments.Text,
                workingDirectory.Text,
                _lifetime.Token);
        }
    }

    private static TextBox CreateCommandTextBox(string labelKey, int maxLength)
    {
        var textBox = new TextBox
        {
            Header = AppStrings.Text(labelKey),
            MaxLength = maxLength,
        };
        AutomationProperties.SetName(textBox, AppStrings.Text(labelKey));
        return textBox;
    }

    private async void AddMacroReferenceClick(object sender, RoutedEventArgs e)
    {
        var options = _viewModel.MacroReferenceCandidates;
        if (options.Count == 0)
        {
            var emptyDialog = new ContentDialog
            {
                Title = AppStrings.Text("MacroReferenceDialogTitle"),
                Content = AppStrings.Text("MacroReferenceEmpty"),
                CloseButtonText = AppStrings.Text("MacroCancel"),
                XamlRoot = RootLayout.XamlRoot,
            };
            await emptyDialog.ShowAsync();
            return;
        }

        var picker = new ComboBox
        {
            Header = AppStrings.Text("MacroReferenceLabel"),
            ItemsSource = options,
            DisplayMemberPath = nameof(MacroReferenceOption.Name),
            SelectedValuePath = nameof(MacroReferenceOption.Id),
            SelectedIndex = 0,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        AutomationProperties.SetName(picker, AppStrings.Text("MacroReferenceLabel"));
        var dialog = new ContentDialog
        {
            Title = AppStrings.Text("MacroReferenceDialogTitle"),
            Content = picker,
            PrimaryButtonText = AppStrings.Text("MacroAdd"),
            CloseButtonText = AppStrings.Text("MacroCancel"),
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = RootLayout.XamlRoot,
        };
        if (await dialog.ShowAsync() == ContentDialogResult.Primary &&
            picker.SelectedItem is MacroReferenceOption option)
        {
            await _viewModel.AddMacroReferenceStepAsync(option.Id, _lifetime.Token);
        }
    }

    private async void AddMacroComboClick(object sender, RoutedEventArgs e)
    {
        var control = CreateModifierCheckBox("MacroModifierControl");
        var shift = CreateModifierCheckBox("MacroModifierShift");
        var alt = CreateModifierCheckBox("MacroModifierAlt");
        var windows = CreateModifierCheckBox("MacroModifierWindows");
        var modifiersPanel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
        modifiersPanel.Children.Add(control);
        modifiersPanel.Children.Add(shift);
        modifiersPanel.Children.Add(alt);
        modifiersPanel.Children.Add(windows);

        var keyPicker = new ComboBox
        {
            Header = AppStrings.Text("MacroComboKeyLabel"),
            ItemsSource = _viewModel.MacroKeyPicker.KeyboardKeys,
            DisplayMemberPath = nameof(MacroKeyOptionViewModel.Label),
            SelectedIndex = 0,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        AutomationProperties.SetName(keyPicker, AppStrings.Text("MacroComboKeyLabel"));

        var content = new StackPanel { Spacing = 10 };
        content.Children.Add(new TextBlock
        {
            Text = AppStrings.Text("MacroComboModifiersLabel"),
        });
        content.Children.Add(modifiersPanel);
        content.Children.Add(keyPicker);

        var dialog = new ContentDialog
        {
            Title = AppStrings.Text("MacroComboDialogTitle"),
            Content = content,
            PrimaryButtonText = AppStrings.Text("MacroAdd"),
            CloseButtonText = AppStrings.Text("MacroCancel"),
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = RootLayout.XamlRoot,
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary ||
            keyPicker.SelectedItem is not MacroKeyOptionViewModel key)
        {
            return;
        }

        var modifiers = MacroModifierKeys.None;
        if (control.IsChecked == true) modifiers |= MacroModifierKeys.Control;
        if (shift.IsChecked == true) modifiers |= MacroModifierKeys.Shift;
        if (alt.IsChecked == true) modifiers |= MacroModifierKeys.Alt;
        if (windows.IsChecked == true) modifiers |= MacroModifierKeys.Windows;
        await _viewModel.AddMacroComboStepAsync(modifiers, key.Input, _lifetime.Token);
    }

    private static CheckBox CreateModifierCheckBox(string labelKey)
    {
        var checkBox = new CheckBox { Content = AppStrings.Text(labelKey) };
        AutomationProperties.SetName(checkBox, AppStrings.Text(labelKey));
        return checkBox;
    }

    private void MacroRecordFlyoutOpening(object sender, object e)
    {
        MacroRecordStartItem.Text = AppStrings.Text(
            _viewModel.IsMacroCountdown ? "MacroCancelCountdown" : "MacroRecordStart/Text");
        MacroRecordStartItem.IsEnabled =
            _viewModel.CanStartMacroRecording || _viewModel.IsMacroCountdown;
        MacroRecordStopItem.IsEnabled = _viewModel.CanStopMacroRecording;
    }

    private void MacroTriggerFlyoutOpening(object sender, object e)
    {
        MacroClearTriggerItem.IsEnabled =
            _viewModel.CanEditMacro &&
            _viewModel.SelectedMacro?.Definition.Trigger is not null;
        MacroNagaTriggerMenu.IsEnabled = _viewModel.CanEditMacro;
        if (MacroNagaTriggerMenu.Items.Count == 0)
        {
            for (var sideButton = 1; sideButton <= 12; sideButton++)
            {
                var item = new MenuFlyoutItem
                {
                    Text = AppStrings.FormatText("MacroNagaSideButton", sideButton),
                    Tag = sideButton,
                };
                item.Click += NagaSideButtonTriggerClick;
                MacroNagaTriggerMenu.Items.Add(item);
            }
        }
    }

    private void MacroRecordDelayModeFlyoutOpening(object sender, object e)
    {
        var index = _viewModel.MacroRecordingDelayModeIndex;
        MacroRecordDelayRecordedItem.IsChecked = index == 0;
        MacroRecordDelayFixedItem.IsChecked = index == 1;
        MacroRecordDelayNoneItem.IsChecked = index == 2;
    }

    private void MacroRecordMouseModeFlyoutOpening(object sender, object e)
    {
        var index = _viewModel.MacroRecordingMouseTrackingModeIndex;
        MacroRecordMouseNoneItem.IsChecked = index == 0;
        MacroRecordMouseAbsoluteItem.IsChecked = index == 1;
        MacroRecordMouseRelativeItem.IsChecked = index == 2;
        MacroRecordMouseStartItem.IsChecked = index == 3;
        MacroRecordMouseForegroundItem.IsChecked = index == 4;
    }

    private void MacroRecordDelayModeClick(object sender, RoutedEventArgs e)
    {
        var index = sender switch
        {
            ToggleMenuFlyoutItem item when ReferenceEquals(item, MacroRecordDelayRecordedItem) => 0,
            ToggleMenuFlyoutItem item when ReferenceEquals(item, MacroRecordDelayFixedItem) => 1,
            ToggleMenuFlyoutItem item when ReferenceEquals(item, MacroRecordDelayNoneItem) => 2,
            _ => -1,
        };
        if (index >= 0)
        {
            _viewModel.MacroRecordingDelayModeIndex = index;
        }
    }

    private void MacroRecordMouseModeClick(object sender, RoutedEventArgs e)
    {
        var index = sender switch
        {
            ToggleMenuFlyoutItem item when ReferenceEquals(item, MacroRecordMouseNoneItem) => 0,
            ToggleMenuFlyoutItem item when ReferenceEquals(item, MacroRecordMouseAbsoluteItem) => 1,
            ToggleMenuFlyoutItem item when ReferenceEquals(item, MacroRecordMouseRelativeItem) => 2,
            ToggleMenuFlyoutItem item when ReferenceEquals(item, MacroRecordMouseStartItem) => 3,
            ToggleMenuFlyoutItem item when ReferenceEquals(item, MacroRecordMouseForegroundItem) => 4,
            _ => -1,
        };
        if (index >= 0)
        {
            _viewModel.MacroRecordingMouseTrackingModeIndex = index;
        }
    }

    private async void CaptureMacroTriggerComboClick(object sender, RoutedEventArgs e) =>
        await _viewModel.CaptureMacroTriggerComboAsync(_lifetime.Token);

    private async void CaptureMacroDeviceTriggerClick(object sender, RoutedEventArgs e) =>
        await _viewModel.CaptureMacroDeviceTriggerAsync(_lifetime.Token);

    private async void ClearMacroTriggerClick(object sender, RoutedEventArgs e) =>
        await _viewModel.ClearMacroTriggerAsync(_lifetime.Token);

    private async void NagaSideButtonTriggerClick(object sender, RoutedEventArgs e)
    {
        if (sender is MenuFlyoutItem { Tag: int sideButton })
        {
            await _viewModel.SetMacroNagaSideButtonTriggerAsync(
                sideButton,
                _lifetime.Token);
        }
    }

    private void TogglePauseMacroClick(object sender, RoutedEventArgs e) =>
        _viewModel.ToggleMacroPlaybackPause();

    private void StopMacroClick(object sender, RoutedEventArgs e) =>
        _viewModel.StopMacroPlayback();

    private async void AddMacroLoopClick(object sender, RoutedEventArgs e) =>
        await _viewModel.AddMacroLoopStepAsync(_lifetime.Token);
    private async void OnMacroEditorClosing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        if (_allowClose)
        {
            return;
        }

        var hasPendingWork = _viewModel.HasUnsavedMacroChanges ||
            _viewModel.IsMacroRecording ||
            _viewModel.IsMacroCountdown ||
            _viewModel.IsMacroStepCapturing ||
            _viewModel.IsMacroTriggerCapturing;
        if (!hasPendingWork)
        {
            return;
        }

        args.Cancel = true;
        if (_closeSaveInProgress)
        {
            return;
        }

        _closeSaveInProgress = true;
        try
        {
            CancelPendingCapture();
            if (_viewModel.HasUnsavedMacroChanges &&
                !await _viewModel.SaveMacroChangesOnCloseAsync(CancellationToken.None))
            {
                return;
            }

            _allowClose = true;
            Close();
        }
        finally
        {
            _closeSaveInProgress = false;
        }
    }

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


