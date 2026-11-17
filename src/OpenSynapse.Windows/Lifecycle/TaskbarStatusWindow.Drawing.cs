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

    private void CreateFonts()
    {
        DeleteFont(ref _batteryFont);
        DeleteFont(ref _iconFont);
        var scale = _dpi / 96.0;
        var bundled = EnsureDisplayFonts();
        // The bundled TTF registers as family "Titillium Web SemiBold" with a
        // Regular style; asking for "Titillium Web" + 600 makes GDI synthesize
        // bold from the Regular face.
        var displayFamily = bundled ? "Titillium Web SemiBold" : "Segoe UI Variable Text";
        var displayWeight = bundled ? FwNormal : FwSemibold;
        _batteryFont = CreateFontW(
            -(int)Math.Round(14 * scale), 0, 0, 0, displayWeight,
            0, 0, 0, DefaultCharset, 0, 0, NonAntialiasedQuality, DefaultPitch,
            displayFamily);
        if (!_displayFontLogged)
        {
            _displayFontLogged = true;
            _log?.Invoke($"taskbar battery font: {displayFamily}/{displayWeight}");
        }

        _iconFont = CreateFontW(
            -(int)Math.Round(13 * scale), 0, 0, 0, FwNormal,
            0, 0, 0, DefaultCharset, 0, 0, AntialiasedQuality, DefaultPitch,
            "Segoe MDL2 Assets");
        if (_batteryFont == IntPtr.Zero)
        {
            _batteryFont = CreateFontW(
                -(int)Math.Round(14 * scale), 0, 0, 0, FwSemibold,
                0, 0, 0, DefaultCharset, 0, 0, NonAntialiasedQuality, DefaultPitch,
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

    private int MeasureWidth(int count)
    {
        var scale = _dpi / 96.0;
        var dc = GetDC(_statusWindow);
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
                width += MeasureText(dc, _batteryFont,
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
        // The window is layered with black as its color key, so only the badge and
        // text survive; the taskbar background shows through the padding.
        var primary = _themeDark ? Rgb(255, 255, 255) : Rgb(1, 1, 1);
        var backgroundBrush = CreateSolidBrush(0);
        FillRect(dc, ref client, backgroundBrush);
        DeleteObject(backgroundBrush);
        SetBkMode(dc, Transparent);

        var count = _visibleBatteryCount > 0
            ? _visibleBatteryCount
            : Math.Min(_snapshot.Batteries.Count, TaskbarStatusPolicy.MaximumBatteryCount);
        var x = PaddingDip * scale;
        var previousFont = SelectObject(dc, _batteryFont);
        for (var index = 0; index < count && index < _snapshot.Batteries.Count; index++)
        {
            var battery = _snapshot.Batteries[index];
            if (index > 0) x += DeviceGapDip * scale;
            if (!string.IsNullOrWhiteSpace(battery.Icon))
            {
                DrawIconBadge(dc, x, client.Height, scale, battery.Icon);
                x += (IconBadgeDip + BatteryIconGapDip) * scale;
            }

            var percent = TaskbarStatusPolicy.FormatBattery(battery.Percent);
            SetTextColor(dc, primary);
            var batteryWidth = MeasureText(dc, _batteryFont, percent);
            var batteryRect = new Rect
            {
                Left = (int)Math.Round(x),
                Top = 0,
                Right = (int)Math.Round(x + batteryWidth + 2),
                Bottom = client.Bottom,
            };
            DrawTextW(dc, percent, percent.Length, ref batteryRect, DtSingleLine | DtVCenter | DtNoPrefix);
            x += batteryWidth;

            if (battery.IsCharging == true)
            {
                x += ChargingGapDip * scale;
                DrawBolt(dc, x, client.Height, scale);
                x += BoltWidthDip * scale;
            }
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
        if (message == _taskbarCreatedMessage)
        {
            _attachFailures = 0;
            _nextAttachAttempt = 0;
            DestroyStatusWindow();
            _taskbarWindow = IntPtr.Zero;
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
                _lastStatusRect = default;
                EnsureAttached();
                InvalidateStatusWindow();
                return IntPtr.Zero;
            case WmSettingChange:
                var setting = lParam == IntPtr.Zero
                    ? null
                    : Marshal.PtrToStringUni(lParam);
                if (setting is null or "TraySettings" or "ImmersiveColorSet")
                {
                    _themeDark = ReadThemeDark();
                    _lastStatusRect = default;
                    EnsureAttached();
                    InvalidateStatusWindow();
                }
                return IntPtr.Zero;
            case WmThemeChanged:
                _themeDark = ReadThemeDark();
                InvalidateStatusWindow();
                return IntPtr.Zero;
            default:
                return DefWindowProcW(window, message, wParam, lParam);
        }
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
            case WmDpiChanged:
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

    private void InvalidateStatusWindow()
    {
        if (_statusWindow != IntPtr.Zero)
        {
            InvalidateRect(_statusWindow, IntPtr.Zero, false);
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
