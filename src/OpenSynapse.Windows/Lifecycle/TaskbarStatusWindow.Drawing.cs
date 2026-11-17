using System.Runtime.InteropServices;
using Microsoft.Win32;
using OpenSynapse.Core.Sensors;
using static OpenSynapse.Windows.Lifecycle.TaskbarStatusNative;

namespace OpenSynapse.Windows.Lifecycle;

public sealed partial class TaskbarStatusWindow
{
    private static readonly string[] BundledDisplayFonts =
    [
        Path.Combine("Assets", "Fonts", "TitilliumWeb-Regular.ttf"),
        Path.Combine("Assets", "Fonts", "TitilliumWeb-SemiBold.ttf"),
    ];
    private static bool _displayFontsLoaded;
    private static bool _displayFontsAvailable;
    private static bool _displayFontLogged;

    private static bool EnsureDisplayFonts()
    {
        if (_displayFontsLoaded) return _displayFontsAvailable;
        _displayFontsLoaded = true;
        foreach (var relativePath in BundledDisplayFonts)
        {
            var path = Path.Combine(AppContext.BaseDirectory, relativePath);
            if (!File.Exists(path) ||
                AddFontResourceExW(path, FrPrivate, IntPtr.Zero) == 0)
            {
                return false;
            }
        }

        _displayFontsAvailable = true;
        return _displayFontsAvailable;
    }

    private void CreateFonts(StatusSurface surface)
    {
        DeleteSurfaceFonts(surface);
        var scale = surface.Dpi / 96.0;
        var bundled = EnsureDisplayFonts();
        // The bundled TTF registers as family "Titillium Web SemiBold" with a
        // Regular style; asking for "Titillium Web" + 600 makes GDI synthesize
        // bold from the Regular face.
        var displayFamily = bundled ? "Titillium Web SemiBold" : "Segoe UI Variable Text";
        var displayWeight = bundled ? FwNormal : FwSemibold;
        var fontSize = surface.Vertical ? 13 : 14;
        surface.BatteryFont = CreateFontW(
            -(int)Math.Round(fontSize * scale), 0, 0, 0, displayWeight,
            0, 0, 0, DefaultCharset, 0, 0, NonAntialiasedQuality, DefaultPitch,
            displayFamily);
        if (!_displayFontLogged)
        {
            _displayFontLogged = true;
            _log?.Invoke($"taskbar battery font: {displayFamily}/{displayWeight}");
        }

        surface.IconFont = CreateFontW(
            -(int)Math.Round(13 * scale), 0, 0, 0, FwNormal,
            0, 0, 0, DefaultCharset, 0, 0, AntialiasedQuality, DefaultPitch,
            "Segoe MDL2 Assets");
        if (surface.BatteryFont == IntPtr.Zero)
        {
            surface.BatteryFont = CreateFontW(
                -(int)Math.Round(fontSize * scale), 0, 0, 0, FwSemibold,
                0, 0, 0, DefaultCharset, 0, 0, NonAntialiasedQuality, DefaultPitch,
                "Segoe UI Variable Text");
        }

        if (surface.IconFont == IntPtr.Zero)
        {
            surface.IconFont = CreateFontW(
                -(int)Math.Round(13 * scale), 0, 0, 0, FwNormal,
                0, 0, 0, DefaultCharset, 0, 0, AntialiasedQuality, DefaultPitch,
                "Segoe UI Symbol");
        }
    }

    private void DeleteSurfaceFonts(StatusSurface surface)
    {
        DeleteFont(ref surface.BatteryFont);
        DeleteFont(ref surface.IconFont);
    }

    private int MeasureWidth(StatusSurface surface, int count)
    {
        var scale = surface.Dpi / 96.0;
        var dc = GetDC(surface.Window);
        if (dc == IntPtr.Zero) return (int)Math.Round(BaseWidthDip * scale);
        try
        {
            var width = PaddingDip * 2 * scale;
            for (var index = 0; index < count && index < _snapshot.Batteries.Count; index++)
            {
                if (index > 0) width += DeviceGapDip * scale;
                var battery = _snapshot.Batteries[index];
                if (!string.IsNullOrWhiteSpace(battery.Icon))
                {
                    width += (IconBadgeDip + BatteryIconGapDip) * scale;
                }
                width += MeasureText(
                    dc, surface.BatteryFont,
                    TaskbarStatusPolicy.FormatBattery(battery.Percent));
                if (battery.IsCharging == true)
                {
                    width += ChargingGapDip * scale + BoltWidthDip * scale;
                }
            }

            return (int)Math.Ceiling(width);
        }
        finally
        {
            ReleaseDC(surface.Window, dc);
        }
    }

    private static int MeasureText(IntPtr dc, IntPtr font, string text)
    {
        if (string.IsNullOrEmpty(text)) return 0;
        var previous = SelectObject(dc, font);
        try
        {
            return GetTextExtentPoint32W(dc, text, text.Length, out var size)
                ? size.Cx
                : text.Length * 7;
        }
        finally
        {
            SelectObject(dc, previous);
        }
    }

    private int MeasureTextWidth(StatusSurface surface, string text)
    {
        var dc = GetDC(surface.Window);
        if (dc == IntPtr.Zero) return text.Length * 7;
        try
        {
            return MeasureText(dc, surface.BatteryFont, text);
        }
        finally
        {
            ReleaseDC(surface.Window, dc);
        }
    }

    private void Draw(IntPtr window)
    {
        if (!_surfacesByWindow.TryGetValue(window, out var surface)) return;
        var dc = GetDC(window);
        if (dc == IntPtr.Zero) return;
        try
        {
            if (!GetClientRect(window, out var client) ||
                client.Width <= 0 ||
                client.Height <= 0)
            {
                return;
            }

            // GDI paints the background before the text. Without an off-screen
            // buffer that intermediate state can reach the screen on every update.
            var memory = CreateCompatibleDC(dc);
            var bitmap = memory == IntPtr.Zero
                ? IntPtr.Zero
                : CreateCompatibleBitmap(dc, client.Width, client.Height);
            if (memory == IntPtr.Zero || bitmap == IntPtr.Zero)
            {
                if (bitmap != IntPtr.Zero) DeleteObject(bitmap);
                if (memory != IntPtr.Zero) DeleteDC(memory);
                Draw(dc, client, surface);
                return;
            }

            var previous = SelectObject(memory, bitmap);
            try
            {
                Draw(memory, client, surface);
                BitBlt(
                    dc, 0, 0, client.Width, client.Height,
                    memory, 0, 0, Srccopy);
            }
            finally
            {
                SelectObject(memory, previous);
                DeleteObject(bitmap);
                DeleteDC(memory);
            }
        }
        finally
        {
            ReleaseDC(window, dc);
        }
    }

    private void Draw(IntPtr dc, Rect client, StatusSurface surface)
    {
        var scale = surface.Dpi / 96.0;
        // The window is layered with black as its color key, so only the badge and
        // text survive; the taskbar background shows through the padding.
        var primary = _themeDark ? Rgb(255, 255, 255) : Rgb(1, 1, 1);
        var backgroundBrush = CreateSolidBrush(0);
        FillRect(dc, ref client, backgroundBrush);
        DeleteObject(backgroundBrush);
        SetBkMode(dc, Transparent);

        var count = surface.VisibleBatteryCount > 0
            ? surface.VisibleBatteryCount
            : Math.Min(_snapshot.Batteries.Count, TaskbarStatusPolicy.MaximumBatteryCount);
        if (surface.Vertical)
        {
            DrawVertical(dc, client, surface, scale, count, primary);
        }
        else
        {
            DrawHorizontal(dc, client, surface, scale, count, primary);
        }
    }

    private void DrawHorizontal(
        IntPtr dc,
        Rect client,
        StatusSurface surface,
        double scale,
        int count,
        uint primary)
    {
        var x = PaddingDip * scale;
        var previousFont = SelectObject(dc, surface.BatteryFont);
        try
        {
            for (var index = 0; index < count && index < _snapshot.Batteries.Count; index++)
            {
                var battery = _snapshot.Batteries[index];
                if (index > 0) x += DeviceGapDip * scale;
                if (!string.IsNullOrWhiteSpace(battery.Icon))
                {
                    DrawIconBadge(
                        dc, x, client.Height, scale, battery.Icon, surface.IconFont);
                    x += (IconBadgeDip + BatteryIconGapDip) * scale;
                }

                var percent = TaskbarStatusPolicy.FormatBattery(battery.Percent);
                SetTextColor(dc, primary);
                var batteryWidth = MeasureText(dc, surface.BatteryFont, percent);
                var batteryRect = new Rect
                {
                    Left = (int)Math.Round(x),
                    Top = 0,
                    Right = (int)Math.Round(x + batteryWidth + 2),
                    Bottom = client.Bottom,
                };
                DrawTextW(
                    dc, percent, percent.Length, ref batteryRect,
                    DtSingleLine | DtVCenter | DtNoPrefix);
                x += batteryWidth;

                if (battery.IsCharging == true)
                {
                    x += ChargingGapDip * scale;
                    var boltTop = (client.Height - BoltHeightDip * scale) / 2;
                    DrawBolt(dc, x, boltTop, scale);
                    x += BoltWidthDip * scale;
                }
            }
        }
        finally
        {
            SelectObject(dc, previousFont);
        }
    }

    private void DrawVertical(
        IntPtr dc,
        Rect client,
        StatusSurface surface,
        double scale,
        int count,
        uint primary)
    {
        var rowHeight = VerticalRowHeightDip * scale;
        var padding = VerticalPaddingDip * scale;
        var previousFont = SelectObject(dc, surface.BatteryFont);
        try
        {
            for (var index = 0; index < count && index < _snapshot.Batteries.Count; index++)
            {
                var battery = _snapshot.Batteries[index];
                var top = index * rowHeight;
                var bottom = top + rowHeight;
                var percent = TaskbarStatusPolicy.FormatBattery(battery.Percent);
                // A vertical taskbar is too narrow for the inline bolt; use the
                // same green as the bolt so charging still reads at a glance.
                SetTextColor(dc, battery.IsCharging == true
                    ? Rgb(68, 214, 44)
                    : primary);
                var textRect = new Rect
                {
                    Left = (int)Math.Round(padding),
                    Top = (int)Math.Round(top),
                    Right = (int)Math.Round(client.Right - padding),
                    Bottom = (int)Math.Round(bottom),
                };
                DrawTextW(
                    dc, percent, percent.Length, ref textRect,
                    DtSingleLine | DtVCenter | DtCenter | DtNoPrefix);
            }
        }
        finally
        {
            SelectObject(dc, previousFont);
        }
    }

    private void DrawIconBadge(
        IntPtr dc,
        double x,
        int height,
        double scale,
        string glyph,
        IntPtr iconFont)
    {
        var size = IconBadgeDip * scale;
        var left = (int)Math.Round(x);
        var top = (int)Math.Round((height - size) / 2);
        var right = (int)Math.Round(x + size);
        var bottom = (int)Math.Round(top + size);
        var radius = (int)Math.Round(IconBadgeRadiusDip * 2 * scale);
        var background = _themeDark ? Rgb(27, 54, 33) : Rgb(226, 244, 222);
        var foreground = _themeDark ? Rgb(68, 214, 44) : Rgb(37, 127, 23);

        var brush = CreateSolidBrush(background);
        var previousBrush = SelectObject(dc, brush);
        var previousPen = SelectObject(dc, GetStockObject(NullPen));
        try
        {
            RoundRect(dc, left, top, right, bottom, radius, radius);
        }
        finally
        {
            SelectObject(dc, previousPen);
            SelectObject(dc, previousBrush);
            DeleteObject(brush);
        }

        SetTextColor(dc, foreground);
        var previousFont = SelectObject(dc, iconFont);
        try
        {
            var iconRect = new Rect { Left = left, Top = top, Right = right, Bottom = bottom };
            DrawTextW(
                dc,
                glyph,
                glyph.Length,
                ref iconRect,
                DtSingleLine | DtVCenter | DtCenter | DtNoPrefix);
        }
        finally
        {
            SelectObject(dc, previousFont);
        }
    }

    private static void DrawBolt(IntPtr dc, double x, double top, double scale)
    {
        var width = BoltWidthDip * scale;
        var boltHeight = BoltHeightDip * scale;
        var points = new[]
        {
            new Point { X = (int)(x + width * 0.45), Y = (int)top },
            new Point { X = (int)(x + width * 0.05), Y = (int)(top + boltHeight * 0.55) },
            new Point { X = (int)(x + width * 0.40), Y = (int)(top + boltHeight * 0.55) },
            new Point { X = (int)(x + width * 0.25), Y = (int)(top + boltHeight) },
            new Point { X = (int)(x + width * 0.95), Y = (int)(top + boltHeight * 0.40) },
            new Point { X = (int)(x + width * 0.60), Y = (int)(top + boltHeight * 0.40) },
        };
        var brush = CreateSolidBrush(Rgb(68, 214, 44));
        var previousBrush = SelectObject(dc, brush);
        var previousPen = SelectObject(dc, GetStockObject(NullPen));
        try
        {
            Polygon(dc, points, points.Length);
        }
        finally
        {
            SelectObject(dc, previousPen);
            SelectObject(dc, previousBrush);
            DeleteObject(brush);
        }
    }

    private IntPtr HostWindowProcedure(
        IntPtr window, uint message, UIntPtr wParam, IntPtr lParam)
    {
        if (message == _taskbarCreatedMessage)
        {
            DestroyAllSurfaces();
            EnsureAttached();
            return IntPtr.Zero;
        }

        switch (message)
        {
            case WmTimer:
                EnsureAttached();
                return IntPtr.Zero;
            case WmDisplayChange:
            case WmDpiChanged:
                ResetLayouts();
                EnsureAttached();
                InvalidateAll();
                return IntPtr.Zero;
            case WmSettingChange:
                var setting = lParam == IntPtr.Zero
                    ? null
                    : Marshal.PtrToStringUni(lParam);
                if (setting is null or "TraySettings" or "ImmersiveColorSet")
                {
                    _themeDark = ReadThemeDark();
                    ResetLayouts();
                    EnsureAttached();
                    InvalidateAll();
                }
                return IntPtr.Zero;
            case WmThemeChanged:
                _themeDark = ReadThemeDark();
                InvalidateAll();
                return IntPtr.Zero;
            default:
                return DefWindowProcW(window, message, wParam, lParam);
        }
    }

    private IntPtr StatusWindowProcedure(
        IntPtr window, uint message, UIntPtr wParam, IntPtr lParam)
    {
        if (!_surfacesByWindow.TryGetValue(window, out var surface))
        {
            return DefWindowProcW(window, message, wParam, lParam);
        }

        switch (message)
        {
            case WmEraseBkgnd:
                return new IntPtr(1);
            case WmPaint:
                Draw(window);
                ValidateRect(window, IntPtr.Zero);
                return IntPtr.Zero;
            case WmPrintClient:
                if (GetClientRect(window, out var printClient))
                {
                    Draw(
                        new IntPtr(unchecked((long)wParam.ToUInt64())),
                        printClient,
                        surface);
                }
                return IntPtr.Zero;
            case WmDpiChanged:
            case WmDpiChangedAfterParent:
                var parent = GetParent(window);
                surface.Dpi = (int)GetDpiForWindow(parent != IntPtr.Zero ? parent : window);
                if (surface.Dpi <= 0) surface.Dpi = 96;
                CreateFonts(surface);
                surface.LastRect = default;
                RepositionAndShow(surface);
                InvalidateRect(window, IntPtr.Zero, false);
                return IntPtr.Zero;
            case WmDisplayChange:
            case WmSettingChange:
            case WmThemeChanged:
                _themeDark = ReadThemeDark();
                surface.LastRect = default;
                RepositionAndShow(surface);
                InvalidateRect(window, IntPtr.Zero, false);
                return IntPtr.Zero;
            default:
                return DefWindowProcW(window, message, wParam, lParam);
        }
    }

    private static bool ReadThemeDark()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("SystemUsesLightTheme") is not int value || value == 0;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            return true;
        }
    }

    private static void DeleteFont(ref IntPtr font)
    {
        if (font == IntPtr.Zero) return;
        DeleteObject(font);
        font = IntPtr.Zero;
    }

    [DllImport("gdi32.dll")]
    private static extern IntPtr GetStockObject(int index);
}
