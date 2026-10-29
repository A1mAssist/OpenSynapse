using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using OpenSynapse.App.ViewModels;
using OpenSynapse.Core.Devices;
using System.Runtime.InteropServices;
using Windows.Graphics;
using Windows.UI;

namespace OpenSynapse.App;

/// <summary>
/// Hosts the fan-curve editor in its own window so the fan card stays a short control picker
/// instead of growing a tall editor inline.
/// </summary>
public sealed partial class BladeFanCurveWindow : Window
{
    private const int ShowWindowRestore = 9;

    private readonly MainViewModel _viewModel;

    public BladeFanCurveWindow(AppWindow owner, MainViewModel viewModel, double scale)
    {
        InitializeComponent();
        _viewModel = viewModel;
        RootLayout.DataContext = viewModel;
        WindowAppearance.Apply(this);
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

        presenter.PreferredMinimumWidth = (int)Math.Round(620 * scale);
        presenter.PreferredMinimumHeight = (int)Math.Round(560 * scale);
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
        var width = Math.Min((int)Math.Round(840 * scale), workArea.Width);
        var height = Math.Min((int)Math.Round(760 * scale), workArea.Height);
        AppWindow.MoveAndResize(new RectInt32(
            workArea.X + Math.Max(0, (workArea.Width - width) / 2),
            workArea.Y + Math.Max(0, (workArea.Height - height) / 2),
            width,
            height));
    }

    private void RefreshLocalization()
    {
        Localized.RefreshTree(RootLayout);
        Title = AppStrings.Text("BladeFanModeCurve");
    }

    private async void ApplyBladeFanCurveClick(object sender, RoutedEventArgs e) =>
        await _viewModel.Fan.ApplyBladeFanCurveEditorAsync(CancellationToken.None);

    private void AddBladeFanCurveTierClick(object sender, RoutedEventArgs e) =>
        _viewModel.Fan.AddBladeFanCurvePoint();

    private void ResetBladeFanCurveClick(object sender, RoutedEventArgs e) =>
        _viewModel.Fan.ResetBladeFanCurveToDefault();

    private void RemoveBladeFanCurveTierClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: BladeFanCurvePointViewModel point })
        {
        _viewModel.Fan.BladeFanCurveEditor.Select(point);
        _viewModel.Fan.RemoveBladeFanCurvePoint();
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
