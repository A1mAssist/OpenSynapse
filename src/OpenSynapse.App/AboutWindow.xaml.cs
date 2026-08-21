using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using System.Reflection;
using Windows.Graphics;
using Windows.UI;

namespace OpenSynapse.App;

public sealed partial class AboutWindow : Window
{
    public AboutWindow()
    {
        InitializeComponent();
        SystemBackdrop = new MicaBackdrop();
        var appIconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "OpenSynapse.ico");
        if (File.Exists(appIconPath))
        {
            AppWindow.SetIcon(appIconPath);
        }

        ApplyTitleBarColors();
        RootLayout.Loaded += (_, _) => ResizeForCurrentDisplay();
        RefreshLocalization();
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

    private void ResizeForCurrentDisplay()
    {
        var workArea = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary).WorkArea;
        var scale = RootLayout.XamlRoot?.RasterizationScale ?? 1d;
        var width = Math.Min((int)Math.Round(520 * scale), workArea.Width);
        var height = Math.Min((int)Math.Round(600 * scale), workArea.Height);
        AppWindow.MoveAndResize(new RectInt32(
            workArea.X + Math.Max(0, (workArea.Width - width) / 2),
            workArea.Y + Math.Max(0, (workArea.Height - height) / 2),
            width,
            height));
    }

    internal void RefreshLocalization()
    {
        Localized.RefreshTree(RootLayout);
        Title = AppStrings.Format("AboutWindowTitle", "关于 OpenSynapse");
        var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "--";
        VersionText.Text = AppStrings.Format("AboutVersion", "版本 {0}", version);
    }
}
