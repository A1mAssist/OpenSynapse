using System.ComponentModel;
using System.Runtime.InteropServices;

namespace OpenSynapse.Windows.Lighting;

/// <summary>
/// Desktop copy capture for systems where Graphics Capture is unavailable or
/// denied. It samples the primary monitor at 25 fps through a scaled DIB
/// section, which is enough resolution for a handful of LED zones.
/// </summary>
internal sealed class GdiDesktopCaptureSession : IDisplayCaptureSession
{
    private const int FrameDelayMilliseconds = 40;
    private const int MaxSampleWidth = 640;
    private const uint Srccopy = 0x00CC0020;
    // CAPTUREBLT must stay off: it pulls layered windows into the copy and makes
    // the screen cursor flicker on every captured frame.
    private const int Halftone = 4;
    private const uint DibRgbColors = 0;
    private readonly nint _screen;
    private readonly nint _memory;
    private readonly nint _bitmap;
    private readonly nint _previousBitmap;
    private readonly nint _bits;
    private readonly int _width;
    private readonly int _height;
    private readonly int _stride;
    private readonly int _sourceWidth;
    private readonly int _sourceHeight;
    private bool _disposed;

    internal GdiDesktopCaptureSession()
    {
        _sourceWidth = GetSystemMetrics(SystemMetricCxScreen);
        _sourceHeight = GetSystemMetrics(SystemMetricCyScreen);
        if (_sourceWidth <= 0 || _sourceHeight <= 0)
        {
            throw new AmbientCaptureException(
                AmbientCaptureFailure.Unsupported,
                "Windows did not report a primary display size to capture.");
        }

        var scale = Math.Min(1d, MaxSampleWidth / (double)_sourceWidth);
        _width = Math.Max(32, (int)Math.Round(_sourceWidth * scale));
        _height = Math.Max(18, (int)Math.Round(_sourceHeight * scale));
        _stride = checked(_width * 4);

        _screen = GetDC(0);
        if (_screen == 0)
        {
            throw new AmbientCaptureException(
                AmbientCaptureFailure.CaptureFailed,
                "Windows did not provide a screen device context.",
                new Win32Exception(Marshal.GetLastWin32Error()));
        }

        _memory = CreateCompatibleDC(_screen);
        var header = new BitmapInfoHeader
        {
            Size = checked((uint)Marshal.SizeOf<BitmapInfoHeader>()),
            Width = _width,
            Height = -_height,
            Planes = 1,
            BitCount = 32,
            SizeImage = checked((uint)(_stride * _height)),
        };
        _bitmap = CreateDIBSection(_screen, ref header, DibRgbColors, out _bits, 0, 0);
        if (_memory == 0 || _bitmap == 0 || _bits == 0)
        {
            var error = Marshal.GetLastWin32Error();
            Release();
            throw new AmbientCaptureException(
                AmbientCaptureFailure.CaptureFailed,
                "Windows could not allocate the desktop copy buffer.",
                new Win32Exception(error));
        }

        _previousBitmap = SelectObject(_memory, _bitmap);
        SetStretchBltMode(_memory, Halftone);
    }

    public async ValueTask<RawDisplayFrame> ReadFrameAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!StretchBlt(
                _memory,
                0,
                0,
                _width,
                _height,
                _screen,
                0,
                0,
                _sourceWidth,
                _sourceHeight,
                Srccopy))
        {
            throw new AmbientCaptureException(
                AmbientCaptureFailure.CaptureFailed,
                "Windows refused to copy the desktop for Ambient Awareness.",
                new Win32Exception(Marshal.GetLastWin32Error()));
        }

        var bytes = new byte[_stride * _height];
        Marshal.Copy(_bits, bytes, 0, bytes.Length);
        await Task.Delay(FrameDelayMilliseconds, cancellationToken).ConfigureAwait(false);
        return new RawDisplayFrame(bytes, _width, _height, _stride);
    }

    public ValueTask DisposeAsync()
    {
        if (!_disposed)
        {
            _disposed = true;
            Release();
        }

        return ValueTask.CompletedTask;
    }

    private void Release()
    {
        if (_previousBitmap != 0 && _memory != 0)
        {
            _ = SelectObject(_memory, _previousBitmap);
        }

        if (_bitmap != 0) _ = DeleteObject(_bitmap);
        if (_memory != 0) _ = DeleteDC(_memory);
        if (_screen != 0) _ = ReleaseDC(0, _screen);
    }

    private const int SystemMetricCxScreen = 0;
    private const int SystemMetricCyScreen = 1;

    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapInfoHeader
    {
        public uint Size;
        public int Width;
        public int Height;
        public ushort Planes;
        public ushort BitCount;
        public uint Compression;
        public uint SizeImage;
        public int XPelsPerMeter;
        public int YPelsPerMeter;
        public uint ClrUsed;
        public uint ClrImportant;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int GetSystemMetrics(int index);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern nint GetDC(nint window);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int ReleaseDC(nint window, nint deviceContext);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern nint CreateCompatibleDC(nint deviceContext);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern int DeleteDC(nint deviceContext);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern nint CreateDIBSection(
        nint deviceContext,
        ref BitmapInfoHeader header,
        uint usage,
        out nint bits,
        nint section,
        uint offset);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern nint SelectObject(nint deviceContext, nint graphicsObject);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern int DeleteObject(nint graphicsObject);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern int SetStretchBltMode(nint deviceContext, int mode);

    [DllImport("gdi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool StretchBlt(
        nint destination,
        int destinationX,
        int destinationY,
        int destinationWidth,
        int destinationHeight,
        nint source,
        int sourceX,
        int sourceY,
        int sourceWidth,
        int sourceHeight,
        uint operation);
}
