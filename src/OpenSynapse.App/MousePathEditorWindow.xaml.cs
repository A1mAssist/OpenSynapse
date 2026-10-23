using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using OpenSynapse.App.ViewModels;
using OpenSynapse.Core.Macros;
using System.Runtime.InteropServices;
using Windows.Graphics;
using Windows.UI;

namespace OpenSynapse.App;

/// <summary>
/// Edits a recorded mouse path without changing the macro until Apply is pressed.
/// The path is kept in a draft view model so Cancel can discard every edit.
/// </summary>
public sealed partial class MousePathEditorWindow : Window
{
    private const int ShowWindowRestore = 9;

    private readonly MousePathEditorViewModel _editor;
    private readonly Action<IReadOnlyList<MacroMousePoint>> _apply;

    public MousePathEditorWindow(
        AppWindow owner,
        MousePathEditorViewModel editor,
        double scale,
        Action<IReadOnlyList<MacroMousePoint>> apply)
    {
        InitializeComponent();
        _editor = editor;
        _apply = apply;
        RootLayout.DataContext = editor;
        SystemBackdrop = new MicaBackdrop();
        var appIconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "OpenSynapse.ico");
        if (File.Exists(appIconPath))
        {
            AppWindow.SetIcon(appIconPath);
        }

        ConfigurePresenter(scale);
        ApplyTitleBarColors();
        PositionOnOwnerDisplay(owner, scale);
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

    private void ConfigurePresenter(double scale)
    {
        if (AppWindow.Presenter is not OverlappedPresenter presenter)
        {
            return;
        }

        var workArea = DisplayArea
            .GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary)
            .WorkArea;
        presenter.PreferredMinimumWidth = Math.Min(
            (int)Math.Round(780 * scale),
            Math.Max(1, workArea.Width));
        presenter.PreferredMinimumHeight = Math.Min(
            (int)Math.Round(600 * scale),
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

    private void PositionOnOwnerDisplay(AppWindow owner, double scale)
    {
        var workArea = DisplayArea.GetFromWindowId(owner.Id, DisplayAreaFallback.Primary).WorkArea;
        var width = Math.Min((int)Math.Round(1000 * scale), workArea.Width);
        var height = Math.Min((int)Math.Round(760 * scale), workArea.Height);
        AppWindow.MoveAndResize(new RectInt32(
            workArea.X + Math.Max(0, (workArea.Width - width) / 2),
            workArea.Y + Math.Max(0, (workArea.Height - height) / 2),
            width,
            height));
    }

    internal void RefreshLocalization()
    {
        Localized.RefreshTree(RootLayout);
        Title = AppStrings.Text("MacroMousePathEditorTitle");
        _editor.RefreshLocalization();
    }

    private void AddPointClick(object sender, RoutedEventArgs e) => _editor.AddPoint();

    private void RemovePointClick(object sender, RoutedEventArgs e) => _editor.RemoveSelectedPoint();

    private void RemovePointRowClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: MousePathPointViewModel point })
        {
            _editor.SelectedPoint = point;
            _editor.RemoveSelectedPoint();
        }
    }

    private void ApplyClick(object sender, RoutedEventArgs e)
    {
        if (!_editor.Validate())
        {
            return;
        }

        _apply(_editor.CreateResult());
        Close();
    }

    private void CancelClick(object sender, RoutedEventArgs e) => Close();

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
