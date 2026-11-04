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
        DeleteFont(ref _performanceFont);
        DeleteFont(ref _cjkFont);
        var scale = _dpi / 96.0;
        var displayFamily = EnsureDisplayFonts() ? "Titillium Web" : "Segoe UI Variable Text";
        _batteryFont = CreateFontW(
            -(int)Math.Round(14 * scale), 0, 0, 0, FwSemibold,
            0, 0, 0, DefaultCharset, 0, 0, AntialiasedQuality, DefaultPitch,
            displayFamily);
        _performanceFont = CreateFontW(
            -(int)Math.Round(13 * scale), 0, 0, 0, FwNormal,
            0, 0, 0, DefaultCharset, 0, 0, AntialiasedQuality, DefaultPitch,
            displayFamily);
        _cjkFont = CreateFontW(
            -(int)Math.Round(13 * scale), 0, 0, 0, FwNormal,
            0, 0, 0, Gb2312Charset, 0, 0, AntialiasedQuality, DefaultPitch,
            "Microsoft YaHei UI");
        if (_batteryFont == IntPtr.Zero)
        {
            _batteryFont = CreateFontW(
                -(int)Math.Round(14 * scale), 0, 0, 0, FwSemibold,
                0, 0, 0, DefaultCharset, 0, 0, AntialiasedQuality, DefaultPitch,
                "Segoe UI Variable Text");
        }
        if (_performanceFont == IntPtr.Zero)
        {
            _performanceFont = CreateFontW(
                -(int)Math.Round(13 * scale), 0, 0, 0, FwNormal,
                0, 0, 0, DefaultCharset, 0, 0, AntialiasedQuality, DefaultPitch,
                "Segoe UI Variable Text");
        }
        if (_cjkFont == IntPtr.Zero)
        {
            _cjkFont = CreateFontW(
                -(int)Math.Round(13 * scale), 0, 0, 0, FwNormal,
                0, 0, 0, Gb2312Charset, 0, 0, AntialiasedQuality, DefaultPitch,
                "Microsoft YaHei");
        }
    }

    private void CreateTooltipWindow()
    {
        _tooltipWindow = CreateWindowExW(
            WsExTopmost,
            "tooltips_class32",
            string.Empty,
            WsPopup | unchecked((int)TtsAlwaysTip) | unchecked((int)TtsNoPrefix),
            0, 0, 0, 0,
            IntPtr.Zero,
            IntPtr.Zero,
            GetModuleHandleW(null),
            IntPtr.Zero);
        if (_tooltipWindow == IntPtr.Zero)
        {
            _log?.Invoke("taskbar-status: tooltip creation failed.");
            return;
        }

        SetWindowPos(
            _tooltipWindow,
            new IntPtr(-1),
            0, 0, 0, 0,
            SwpNoMove | SwpNoSize | SwpNoActivate);
        SendMessageW(
            _tooltipWindow,
            TtmSetMaxTipWidth,
            new IntPtr((int)Math.Round(300 * (_dpi / 96.0))),
            IntPtr.Zero);
    }

    private void UpdateTooltip()
    {
        if (_tooltipWindow == IntPtr.Zero || _statusWindow == IntPtr.Zero) return;
        FreeTooltipText();
        _tooltipTextPointer = Marshal.StringToHGlobalUni(_tooltipText);
        var info = new ToolInfo
        {
            Size = (uint)Marshal.SizeOf<ToolInfo>(),
            Flags = TtfIdishwnd | TtfSubclass | TtfTransparent,
            Window = _statusWindow,
            Id = (UIntPtr)(ulong)_statusWindow.ToInt64(),
            Instance = GetModuleHandleW(null),
            Text = _tooltipTextPointer,
        };
        SendMessageW(
            _tooltipWindow,
            _tooltipAdded ? TtmUpdateTipText : TtmAddTool,
            IntPtr.Zero,
            ref info);
        _tooltipAdded = true;
    }

    private void FreeTooltipText()
    {
        if (_tooltipTextPointer == IntPtr.Zero) return;
        Marshal.FreeHGlobal(_tooltipTextPointer);
        _tooltipTextPointer = IntPtr.Zero;
    }

    private int MeasureWidth(bool includePerformance)
    {
        var scale = _dpi / 96.0;
        var dc = GetDC(_statusWindow);
        if (dc == IntPtr.Zero) return (int)Math.Round(BaseWidthDip * scale);
        try
        {
            var width = PaddingDip * 2 * scale + MeasureText(dc, _batteryFont,
                TaskbarStatusPolicy.FormatBattery(_snapshot.BatteryPercent));
            if (_snapshot.IsCharging == true)
            {
                width += 4 * scale + BoltWidthDip * scale;
            }
            if (includePerformance && !string.IsNullOrWhiteSpace(_snapshot.PerformanceMode))
            {
                width += PerformanceGapDip * scale +
                    MeasureMixedText(dc, _performanceFont, _cjkFont, _snapshot.PerformanceMode);
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

    private static int MeasureMixedText(
        IntPtr dc, IntPtr latinFont, IntPtr cjkFont, string text)
    {
        var width = 0;
        foreach (var run in SplitTextRuns(text))
        {
            width += MeasureText(dc, run.Cjk ? cjkFont : latinFont, run.Text);
        }
        return width;
    }

    private static void DrawMixedText(
        IntPtr dc,
        IntPtr latinFont,
        IntPtr cjkFont,
        string text,
        ref Rect rect,
        uint flags)
    {
        var previous = SelectObject(dc, latinFont);
        var x = rect.Left;
        try
        {
            foreach (var run in SplitTextRuns(text))
            {
                var font = run.Cjk ? cjkFont : latinFont;
                SelectObject(dc, font);
                var width = MeasureText(dc, font, run.Text);
                var runRect = new Rect
                {
                    Left = x,
                    Top = rect.Top,
                    Right = x + width,
                    Bottom = rect.Bottom,
                };
                DrawTextW(dc, run.Text, run.Text.Length, ref runRect, flags);
                x += width;
            }
        }
        finally
        {
            SelectObject(dc, previous);
        }
    }

    internal static IEnumerable<(string Text, bool Cjk)> SplitTextRuns(string text)
    {
        if (string.IsNullOrEmpty(text)) yield break;
        var start = 0;
        var cjk = IsCjk(text[0]);
        for (var index = 1; index < text.Length; index++)
        {
            var next = IsCjk(text[index]);
            if (next == cjk) continue;
            yield return (text[start..index], cjk);
            start = index;
            cjk = next;
        }
        yield return (text[start..], cjk);
    }

    private static bool IsCjk(char value) =>
        value is >= '\u2E80' and <= '\u9FFF' or
        >= '\uF900' and <= '\uFAFF' or
        >= '\uFF00' and <= '\uFFEF';

    private void Draw(IntPtr window)
    {
        var dc = GetDC(window);
        if (dc == IntPtr.Zero) return;
        try
        {
            if (!GetClientRect(window, out var client)) return;
            var scale = _dpi / 96.0;
            var background = _themeDark ? Rgb(32, 32, 32) : Rgb(243, 243, 243);
            var primary = _themeDark ? Rgb(255, 255, 255) : Rgb(0, 0, 0);
            var secondary = _themeDark ? Rgb(176, 176, 176) : Rgb(102, 102, 102);
            var backgroundBrush = CreateSolidBrush(background);
            FillRect(dc, ref client, backgroundBrush);
            DeleteObject(backgroundBrush);
            SetBkMode(dc, Transparent);

            var x = PaddingDip * scale;
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
                x += 4 * scale;
                DrawBolt(dc, x, client.Height, scale);
                x += BoltWidthDip * scale;
            }

            if (_showPerformance && !string.IsNullOrWhiteSpace(_snapshot.PerformanceMode))
            {
                x += PerformanceGapDip * scale;
                SetTextColor(dc, secondary);
                var performanceRect = new Rect
                {
                    Left = (int)Math.Round(x),
                    Top = 0,
                    Right = client.Right - (int)Math.Round(PaddingDip * scale),
                    Bottom = client.Bottom,
                };
                DrawMixedText(
                    dc,
                    _performanceFont,
                    _cjkFont,
                    _snapshot.PerformanceMode,
                    ref performanceRect,
                    DtSingleLine | DtVCenter | DtNoPrefix);
            }

            SelectObject(dc, previousFont);
        }
        finally
        {
            ReleaseDC(window, dc);
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
            case WmLButtonUp:
                Activated?.Invoke();
                return IntPtr.Zero;
            case WmEraseBkgnd:
                return new IntPtr(1);
            case WmPaint:
                Draw(window);
                ValidateRect(window, IntPtr.Zero);
                return IntPtr.Zero;
            case WmDpiChangedAfterParent:
                _dpi = (int)GetDpiForWindow(window);
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
