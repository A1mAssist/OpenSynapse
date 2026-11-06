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

    private static bool EnsureDisplayFonts()
    {
        if (_displayFontsLoaded) return _displayFontsAvailable;
        _displayFontsLoaded = true;
        foreach (var relativePath in BundledDisplayFonts)
        {
            var path = Path.Combine(AppContext.BaseDirectory, relativePath);
            if (File.Exists(path) &&
                AddFontResourceExW(path, FrPrivate, IntPtr.Zero) > 0)
            {
                _displayFontsAvailable = true;
            }
        }
        return _displayFontsAvailable;
    }

    private void CreateFonts()
    {
        DeleteFont(ref _batteryFont);
        DeleteFont(ref _iconFont);
        var scale = _dpi / 96.0;
        var displayFamily = EnsureDisplayFonts() ? "Titillium Web" : "Segoe UI Variable Text";
        _batteryFont = CreateFontW(
            -(int)Math.Round(14 * scale), 0, 0, 0, FwSemibold,
            0, 0, 0, DefaultCharset, 0, 0, AntialiasedQuality, DefaultPitch,
            displayFamily);

        _iconFont = CreateFontW(
            -(int)Math.Round(13 * scale), 0, 0, 0, FwNormal,
            0, 0, 0, DefaultCharset, 0, 0, AntialiasedQuality, DefaultPitch,
            "Segoe MDL2 Assets");
        if (_batteryFont == IntPtr.Zero)
        {
            _batteryFont = CreateFontW(
                -(int)Math.Round(14 * scale), 0, 0, 0, FwSemibold,
                0, 0, 0, DefaultCharset, 0, 0, AntialiasedQuality, DefaultPitch,
                "Segoe UI Variable Text");
        }

        if (_iconFont == IntPtr.Zero)
        {
            _iconFont = CreateFontW(
                -(int)Math.Round(13 * scale), 0, 0, 0, FwNormal,
                0, 0, 0, DefaultCharset, 0, 0, AntialiasedQuality, DefaultPitch,
                "Segoe UI Symbol");
        }
    }

    private int MeasureWidth()
    {
        var scale = _dpi / 96.0;
        var dc = GetDC(_statusWindow);
        if (dc == IntPtr.Zero) return (int)Math.Round(BaseWidthDip * scale);
        try
        {
            var width = PaddingDip * 2 * scale;
            if (!string.IsNullOrWhiteSpace(_snapshot.BatteryIcon))
            {
                width += (IconBadgeDip + BatteryIconGapDip) * scale;
            }
            width += MeasureText(dc, _batteryFont,
                TaskbarStatusPolicy.FormatBattery(_snapshot.BatteryPercent));
            if (_snapshot.IsCharging == true)
            {
                width += ChargingGapDip * scale + BoltWidthDip * scale;
            }

            return (int)Math.Ceiling(width);
        }
        finally
        {
            ReleaseDC(_statusWindow, dc);
        }
    }

    private static int MeasureText(IntPtr dc, IntPtr font, string text)
    {
        if (string.IsNullOrEmpty(text)) return 0;
        var previous = SelectObject(dc, font);
        try
        {
            return GetTextExtentPoint32W(dc, text, text.Length, out var size) ? size.Cx : text.Length * 7;
        }
        finally
        {
            SelectObject(dc, previous);
        }
    }

    private void Draw(IntPtr window)
    {
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
                Draw(dc, client);
                return;
            }

            var previous = SelectObject(memory, bitmap);
            try
            {
                Draw(memory, client);
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

    private void Draw(IntPtr dc, Rect client)
    {
        var scale = _dpi / 96.0;
        var background = _themeDark ? Rgb(36, 36, 36) : Rgb(243, 243, 243);
        var primary = _themeDark ? Rgb(255, 255, 255) : Rgb(0, 0, 0);
        var backgroundBrush = CreateSolidBrush(background);
        FillRect(dc, ref client, backgroundBrush);
        DeleteObject(backgroundBrush);
        SetBkMode(dc, Transparent);

        var x = PaddingDip * scale;
        if (!string.IsNullOrWhiteSpace(_snapshot.BatteryIcon))
        {
            DrawIconBadge(dc, x, client.Height, scale, _snapshot.BatteryIcon);
            x += (IconBadgeDip + BatteryIconGapDip) * scale;
        }

        var battery = TaskbarStatusPolicy.FormatBattery(_snapshot.BatteryPercent);
        var previousFont = SelectObject(dc, _batteryFont);
        SetTextColor(dc, primary);
        var batteryWidth = MeasureText(dc, _batteryFont, battery);
        var batteryRect = new Rect
        {
            Left = (int)Math.Round(x),
            Top = 0,
            Right = (int)Math.Round(x + batteryWidth + 2),
            Bottom = client.Bottom,
        };
        DrawTextW(dc, battery, battery.Length, ref batteryRect, DtSingleLine | DtVCenter | DtNoPrefix);
        x += batteryWidth;

        if (_snapshot.IsCharging == true)
        {
            x += ChargingGapDip * scale;
            DrawBolt(dc, x, client.Height, scale);
            x += BoltWidthDip * scale;
        }


        SelectObject(dc, previousFont);
    }

    private void DrawIconBadge(IntPtr dc, double x, int height, double scale, string glyph)
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
        var previousFont = SelectObject(dc, _iconFont);
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

    private static void DrawBolt(IntPtr dc, double x, int height, double scale)
    {
        var width = BoltWidthDip * scale;
        var boltHeight = BoltHeightDip * scale;
        var top = (height - boltHeight) / 2;
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
        if (message == _taskbarCreatedMessage || message == WmTimer)
        {
            if (message == _taskbarCreatedMessage) _attachFailed = false;
            EnsureAttached();
            return IntPtr.Zero;
        }
        return DefWindowProcW(window, message, wParam, lParam);
    }

    private IntPtr StatusWindowProcedure(
        IntPtr window, uint message, UIntPtr wParam, IntPtr lParam)
    {
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
                    Draw(new IntPtr(unchecked((long)wParam.ToUInt64())), printClient);
                }
                return IntPtr.Zero;
            case WmDpiChangedAfterParent:
                var parent = GetParent(window);
                _dpi = (int)GetDpiForWindow(parent != IntPtr.Zero ? parent : window);
                if (_dpi <= 0) _dpi = 96;
                CreateFonts();
                _lastStatusRect = default;
                RepositionAndShow();
                InvalidateRect(window, IntPtr.Zero, false);
                return IntPtr.Zero;
            case WmDisplayChange:
            case WmSettingChange:
            case WmThemeChanged:
                _themeDark = ReadThemeDark();
                _lastStatusRect = default;
                RepositionAndShow();
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
