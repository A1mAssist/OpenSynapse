using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Threading.Channels;
using OpenSynapse.Core.Displays;
using OpenSynapse.Windows.Displays;
using OpenSynapse.Windows.Protocols;
using global::Windows.Foundation.Metadata;
using global::Windows.Graphics.Capture;
using global::Windows.Graphics.DirectX;
using global::Windows.Graphics.DirectX.Direct3D11;
using global::Windows.Graphics.Imaging;
using global::Windows.Security.Authorization.AppCapabilityAccess;

namespace OpenSynapse.Windows.Lighting;

internal enum AmbientCaptureFailure
{
    Unsupported,
    PermissionDenied,
    TopologyUnavailable,
    CaptureFailed,
}

internal sealed class AmbientCaptureException(
    AmbientCaptureFailure failure,
    string message,
    Exception? innerException = null) : InvalidOperationException(message, innerException)
{
    public AmbientCaptureFailure Failure { get; } = failure;
}

internal sealed record AmbientCaptureFrame(
    IReadOnlyList<RazerRgb> Pixels,
    int Width,
    int Height);

internal sealed record RawDisplayFrame(
    byte[] Bgra,
    int Width,
    int Height,
    int Stride);

internal interface IDisplayCaptureSession : IAsyncDisposable
{
    ValueTask<RawDisplayFrame> ReadFrameAsync(CancellationToken cancellationToken);
}

internal sealed partial class WindowsDisplayCaptureAdapter : ILightingInputAdapter
{
    private const double DefaultEdgeBandFraction = 0.08;
    private readonly Func<CancellationToken, ValueTask<IDisplayCaptureSession>> _openSession;
    private readonly double _edgeBandFraction;
    private Channel<AmbientCaptureFrame> _frames = CreateFrameChannel();
    private readonly object _lifecycleGate = new();
    private CancellationTokenSource? _stop;
    private Task? _worker;
    private AmbientCaptureFrame? _latest;
    private int _disposed;

    internal WindowsDisplayCaptureAdapter()
        : this(WindowsGraphicsCaptureSession.OpenInternalDisplayAsync, DefaultEdgeBandFraction)
    {
    }

    internal WindowsDisplayCaptureAdapter(
        Func<IDisplayCaptureSession> openSession,
        double edgeBandFraction = DefaultEdgeBandFraction)
        : this(_ => ValueTask.FromResult(openSession()), edgeBandFraction)
    {
    }

    private WindowsDisplayCaptureAdapter(
        Func<CancellationToken, ValueTask<IDisplayCaptureSession>> openSession,
        double edgeBandFraction)
    {
        _openSession = openSession ?? throw new ArgumentNullException(nameof(openSession));
        if (double.IsNaN(edgeBandFraction) || edgeBandFraction is <= 0 or > 0.5)
        {
            throw new ArgumentOutOfRangeException(nameof(edgeBandFraction));
        }
        _edgeBandFraction = edgeBandFraction;
    }

    public async ValueTask StartAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        cancellationToken.ThrowIfCancellationRequested();
        var session = await _openSession(cancellationToken).ConfigureAwait(false);
        var sessionOwnedByWorker = false;
        try
        {
            lock (_lifecycleGate)
            {
                ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
                if (_worker is not null)
                {
                    throw new InvalidOperationException("Ambient Awareness is already running.");
                }

                _frames = CreateFrameChannel();
                _latest = null;
                _stop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                _worker = Task.Run(
                    () => CaptureAsync(session, _frames.Writer, _stop.Token),
                    CancellationToken.None);
                sessionOwnedByWorker = true;
            }
        }
        finally
        {
            if (!sessionOwnedByWorker)
            {
                await session.DisposeAsync().ConfigureAwait(false);
            }
        }
    }

    public ValueTask StopAsync() => new(StopCoreAsync());

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
        {
            await StopCoreAsync().ConfigureAwait(false);
        }
    }

    internal async ValueTask<AmbientCaptureFrame> ReadFrameAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        Task worker;
        lock (_lifecycleGate)
        {
            worker = _worker ?? throw new InvalidOperationException("Ambient Awareness is not running.");
        }
        if (worker.IsFaulted)
        {
            return await FaultedFrameAsync(worker).ConfigureAwait(false);
        }

        if (_frames.Reader.TryRead(out var frame))
        {
            while (_frames.Reader.TryRead(out var newer))
            {
                frame = newer;
            }
            return _latest = frame;
        }
        if (_latest is not null)
        {
            return _latest;
        }

        try
        {
            frame = await _frames.Reader.ReadAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (ChannelClosedException exception) when (exception.InnerException is not null)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(
                exception.InnerException).Throw();
            throw;
        }
        while (_frames.Reader.TryRead(out var newer))
        {
            frame = newer;
        }
        return _latest = frame;
    }

    internal static RawDisplayFrame CopyPixels(SoftwareBitmap bitmap, int width, int height)
    {
        if (width <= 0 || height <= 0 || width > bitmap.PixelWidth || height > bitmap.PixelHeight)
        {
            throw new AmbientCaptureException(
                AmbientCaptureFailure.TopologyUnavailable,
                "Windows returned invalid display capture dimensions.");
        }
        if (bitmap.BitmapPixelFormat != BitmapPixelFormat.Bgra8)
        {
            throw new AmbientCaptureException(
                AmbientCaptureFailure.CaptureFailed,
                "Windows returned a display capture frame that is not BGRA8.");
        }

        var stride = checked(width * 4);
        var bytes = new byte[checked(stride * height)];
        var destination = new global::Windows.Storage.Streams.Buffer(checked((uint)bytes.Length));
        bitmap.CopyToBuffer(destination);
        using var reader = global::Windows.Storage.Streams.DataReader.FromBuffer(destination);
        reader.ReadBytes(bytes);
        return new RawDisplayFrame(bytes, width, height, stride);
    }

    private async Task CaptureAsync(
        IDisplayCaptureSession session,
        ChannelWriter<AmbientCaptureFrame> writer,
        CancellationToken cancellationToken)
    {
        Exception? failure = null;
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var raw = await session.ReadFrameAsync(cancellationToken).ConfigureAwait(false);
                var bounds = CaptureRegion.ToPixelBounds(raw.Width, raw.Height);
                var edgeBand = Math.Clamp(
                    (int)Math.Ceiling(Math.Min(bounds.Width, bounds.Height) * _edgeBandFraction),
                    1,
                    Math.Min(bounds.Width, bounds.Height));
                writer.TryWrite(ReduceEdgeBand(
                    raw.Bgra,
                    raw.Width,
                    raw.Height,
                    raw.Stride,
                    bounds.X,
                    bounds.Y,
                    bounds.Width,
                    bounds.Height,
                    edgeBand));
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            failure = exception;
        }
        finally
        {
            try
            {
                await session.DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception disposeException)
            {
                failure = failure is null
                    ? disposeException
                    : new AggregateException(failure, disposeException);
            }
            finally
            {
                writer.TryComplete(failure);
            }
        }

        if (failure is not null)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
        }
    }

    private async Task StopCoreAsync()
    {
        Task? worker;
        CancellationTokenSource? stop;
        lock (_lifecycleGate)
        {
            worker = _worker;
            stop = _stop;
            stop?.Cancel();
        }

        try
        {
            if (worker is not null)
            {
                try
                {
                    await worker.ConfigureAwait(false);
                }
                catch (Exception) when (worker.IsFaulted)
                {
                    // The failure already surfaced through ReadFrameAsync; a
                    // stop must still clear the worker so capture can restart.
                }
            }
        }
        finally
        {
            lock (_lifecycleGate)
            {
                if (ReferenceEquals(_worker, worker))
                {
                    _worker = null;
                    _stop = null;
                    stop?.Dispose();
                }
            }
        }
    }

    private static Channel<AmbientCaptureFrame> CreateFrameChannel() =>
        Channel.CreateBounded<AmbientCaptureFrame>(new BoundedChannelOptions(1)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = true,
        });

    private static async ValueTask<AmbientCaptureFrame> FaultedFrameAsync(Task worker)
    {
        await worker.ConfigureAwait(false);
        throw new InvalidOperationException("Display capture has stopped.");
    }

    /// <summary>
    /// Desktop copy capture for systems where Graphics Capture is unavailable or
    /// denied. It samples the primary monitor at 25 fps through a scaled DIB
    /// section, which is enough resolution for a handful of LED zones.
    /// </summary>
    private sealed class GdiDesktopCaptureSession : IDisplayCaptureSession
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

    private sealed class WindowsGraphicsCaptureSession : IDisplayCaptureSession
    {
        private const string GraphicsCaptureItemRuntimeClass = "Windows.Graphics.Capture.GraphicsCaptureItem";
        private const uint D3d11CreateDeviceBgraSupport = 0x20;
        private const uint D3d11SdkVersion = 7;
        private const int D3dDriverTypeHardware = 1;
        private static readonly Guid GraphicsCaptureItemInteropId = new("3628E81B-3CAC-4C60-B7F4-23CE0E0C3356");
        private static readonly Guid GraphicsCaptureItemId = new("79C3F95B-31F7-4EC2-A464-632EF5D30760");
        private static readonly Guid DxgiDeviceId = new("54EC77FA-1377-44E6-8C32-88FD5F44C84C");
        private readonly IDirect3DDevice _device;
        private readonly GraphicsCaptureItem _item;
        private readonly Direct3D11CaptureFramePool _framePool;
        private readonly GraphicsCaptureSession _session;
        private readonly Channel<byte> _arrivals = Channel.CreateBounded<byte>(
            new BoundedChannelOptions(1)
            {
                FullMode = BoundedChannelFullMode.DropOldest,
                SingleReader = true,
                SingleWriter = false,
            });
        private global::Windows.Graphics.SizeInt32 _size;
        private int _disposed;

        private WindowsGraphicsCaptureSession(
            IDirect3DDevice device,
            GraphicsCaptureItem item,
            Direct3D11CaptureFramePool framePool,
            GraphicsCaptureSession session)
        {
            _device = device;
            _item = item;
            _framePool = framePool;
            _session = session;
            _size = item.Size;
            _framePool.FrameArrived += OnFrameArrived;
            _item.Closed += OnItemClosed;
        }

        internal static async ValueTask<IDisplayCaptureSession> OpenInternalDisplayAsync(
            CancellationToken cancellationToken)
        {
            var borderless = false;
            if (ApiInformation.IsTypePresent("Windows.Graphics.Capture.GraphicsCaptureAccess") &&
                ApiInformation.IsPropertyPresent(
                    "Windows.Graphics.Capture.GraphicsCaptureSession",
                    "IsBorderRequired"))
            {
                try
                {
                    borderless = await GraphicsCaptureAccess.RequestAccessAsync(
                            GraphicsCaptureAccessKind.Borderless)
                        .AsTask(cancellationToken)
                        .ConfigureAwait(false) == AppCapabilityAccessStatus.Allowed;
                }
                catch (Exception exception) when (
                    !cancellationToken.IsCancellationRequested &&
                    exception is COMException or UnauthorizedAccessException)
                {
                    // Windows keeps its capture border when borderless access is unavailable.
                }
            }

            try
            {
                return OpenInternalDisplay(borderless);
            }
            catch (Exception exception) when (
                !cancellationToken.IsCancellationRequested &&
                exception is AmbientCaptureException or UnauthorizedAccessException)
            {
                try
                {
                    return new GdiDesktopCaptureSession();
                }
                catch (Exception fallback) when (
                    fallback is AmbientCaptureException or Win32Exception or InvalidOperationException)
                {
                    throw new AmbientCaptureException(
                        AmbientCaptureFailure.CaptureFailed,
                        "Windows denied screen capture and the desktop copy fallback failed.",
                        new AggregateException(exception, fallback));
                }
            }
        }

        private static IDisplayCaptureSession OpenInternalDisplay(bool borderless)
        {
            if (!GraphicsCaptureSession.IsSupported())
            {
                throw new AmbientCaptureException(
                    AmbientCaptureFailure.Unsupported,
                    "The current Windows version or graphics driver does not support Graphics Capture.");
            }

            string sourceName;
            try
            {
                sourceName = new WindowsInternalDisplayController().ResolveSourceName();
            }
            catch (NoActiveInternalDisplayException)
            {
                // Desktops and clamshell laptops have no internal panel. Ambient
                // Awareness still has to sample something, so follow the primary
                // monitor instead of reporting that capture is unavailable.
                sourceName = ResolvePrimarySourceName();
            }
            catch (Exception exception) when (exception is InvalidOperationException or Win32Exception)
            {
                throw new AmbientCaptureException(
                    AmbientCaptureFailure.TopologyUnavailable,
                    "Could not uniquely identify the current internal display.",
                    exception);
            }

            var monitor = FindMonitor(sourceName);
            GraphicsCaptureItem item;
            try
            {
                item = CreateCaptureItem(monitor);
            }
            catch (COMException exception) when (exception.HResult == unchecked((int)0x80070005))
            {
                throw new AmbientCaptureException(
                    AmbientCaptureFailure.PermissionDenied,
                    "Windows denied internal display capture permission.",
                    exception);
            }
            catch (UnauthorizedAccessException exception)
            {
                // CreateForMonitor reports E_ACCESSDENIED through CreateForMonitor's
                // HRESULT path, which .NET maps to UnauthorizedAccessException.
                throw new AmbientCaptureException(
                    AmbientCaptureFailure.PermissionDenied,
                    "Windows denied internal display capture permission.",
                    exception);
            }
            catch (COMException exception)
            {
                throw new AmbientCaptureException(
                    AmbientCaptureFailure.CaptureFailed,
                    "Windows could not create an internal display capture item.",
                    exception);
            }

            IDirect3DDevice? device = null;
            Direct3D11CaptureFramePool? framePool = null;
            GraphicsCaptureSession? session = null;
            var itemOwnedByResult = false;
            try
            {
                device = CreateDirect3DDevice();
                framePool = Direct3D11CaptureFramePool.CreateFreeThreaded(
                    device,
                    DirectXPixelFormat.B8G8R8A8UIntNormalized,
                    2,
                    item.Size);
                session = framePool.CreateCaptureSession(item);
                session.IsCursorCaptureEnabled = false;
                if (borderless)
                {
                    try
                    {
                        session.IsBorderRequired = false;
                    }
                    catch (Exception exception) when (
                        exception is COMException or UnauthorizedAccessException)
                    {
                        // Permission can change between the access request and session creation.
                    }
                }
                var result = new WindowsGraphicsCaptureSession(device, item, framePool, session);
                itemOwnedByResult = true;
                device = null;
                framePool = null;
                session = null;
                try
                {
                    result._session.StartCapture();
                    return result;
                }
                catch
                {
                    result.DisposeAsync().AsTask().GetAwaiter().GetResult();
                    throw;
                }
            }
            catch (AmbientCaptureException)
            {
                throw;
            }
            catch (Exception exception) when (exception is COMException or InvalidOperationException)
            {
                throw new AmbientCaptureException(
                    AmbientCaptureFailure.CaptureFailed,
                    "Windows could not start internal display capture.",
                    exception);
            }
            finally
            {
                session?.Dispose();
                framePool?.Dispose();
                (device as IDisposable)?.Dispose();
                if (!itemOwnedByResult)
                {
                    ReleaseWinRtObject(item);
                }
            }
        }

        public async ValueTask<RawDisplayFrame> ReadFrameAsync(CancellationToken cancellationToken)
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
            while (true)
            {
                try
                {
                    _ = await _arrivals.Reader.ReadAsync(cancellationToken).ConfigureAwait(false);
                }
                catch (ChannelClosedException exception)
                {
                    throw new AmbientCaptureException(
                        AmbientCaptureFailure.TopologyUnavailable,
                        "Internal display capture closed because the display topology changed.",
                        exception.InnerException ?? exception);
                }

                Direct3D11CaptureFrame? latest = null;
                while (_framePool.TryGetNextFrame() is { } candidate)
                {
                    latest?.Dispose();
                    latest = candidate;
                }
                if (latest is null)
                {
                    continue;
                }

                using (latest)
                {
                    var contentSize = latest.ContentSize;
                    try
                    {
                        using var bitmap = await SoftwareBitmap.CreateCopyFromSurfaceAsync(
                            latest.Surface,
                            BitmapAlphaMode.Ignore).AsTask(cancellationToken).ConfigureAwait(false);
                        var raw = CopyPixels(bitmap, contentSize.Width, contentSize.Height);
                        if (contentSize.Width != _size.Width || contentSize.Height != _size.Height)
                        {
                            _framePool.Recreate(
                                _device,
                                DirectXPixelFormat.B8G8R8A8UIntNormalized,
                                2,
                                contentSize);
                            _size = contentSize;
                        }
                        return raw;
                    }
                    catch (COMException exception)
                    {
                        throw new AmbientCaptureException(
                            AmbientCaptureFailure.CaptureFailed,
                            "Failed to read an internal display capture frame.",
                            exception);
                    }
                }
            }
        }

        public ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                _framePool.FrameArrived -= OnFrameArrived;
                _item.Closed -= OnItemClosed;
                _arrivals.Writer.TryComplete();
                _session.Dispose();
                _framePool.Dispose();
                ReleaseWinRtObject(_item);
                (_device as IDisposable)?.Dispose();
            }
            return ValueTask.CompletedTask;
        }

        private const uint MonitorInfoPrimary = 0x00000001;

        /// <summary>
        /// Fallback capture source when the machine has no active internal panel.
        /// </summary>
        private static string ResolvePrimarySourceName()
        {
            string? primary = null;
            // A callback that returns false ends the enumeration, which also makes
            // EnumDisplayMonitors report false, so only the found name matters here.
            _ = EnumDisplayMonitors(0, 0, (monitor, _, _, _) =>
            {
                var info = new MonitorInfoEx
                {
                    Size = checked((uint)Marshal.SizeOf<MonitorInfoEx>()),
                    DeviceName = string.Empty,
                };
                if (GetMonitorInfo(monitor, ref info) && (info.Flags & MonitorInfoPrimary) != 0)
                {
                    primary = info.DeviceName.TrimEnd('\0');
                    return false;
                }

                return true;
            }, 0);
            if (primary is null)
            {
                throw new AmbientCaptureException(
                    AmbientCaptureFailure.TopologyUnavailable,
                    "Windows did not report a primary monitor to capture.");
            }

            return primary;
        }

        private static nint FindMonitor(string sourceName)        {
            var matches = new List<nint>();
            if (!EnumDisplayMonitors(0, 0, (monitor, _, _, _) =>
            {
                var info = new MonitorInfoEx
                {
                    Size = checked((uint)Marshal.SizeOf<MonitorInfoEx>()),
                    DeviceName = string.Empty,
                };
                if (GetMonitorInfo(monitor, ref info) &&
                    StringComparer.OrdinalIgnoreCase.Equals(info.DeviceName.TrimEnd('\0'), sourceName))
                {
                    matches.Add(monitor);
                }
                return true;
            }, 0))
            {
                throw new AmbientCaptureException(
                    AmbientCaptureFailure.TopologyUnavailable,
                    "Windows could not enumerate displays.",
                    new Win32Exception(Marshal.GetLastWin32Error()));
            }
            if (matches.Count != 1)
            {
                throw new AmbientCaptureException(
                    AmbientCaptureFailure.TopologyUnavailable,
                    matches.Count == 0
                        ? "No HMONITOR was found for the internal display."
                        : "The internal display maps to multiple HMONITOR handles.");
            }
            return matches[0];
        }

        private static GraphicsCaptureItem CreateCaptureItem(nint monitor)
        {
            using var factory = WinRT.ActivationFactory.Get(
                GraphicsCaptureItemRuntimeClass,
                GraphicsCaptureItemInteropId);
            var vtable = Marshal.ReadIntPtr(factory.ThisPtr);
            var createForMonitor = Marshal.GetDelegateForFunctionPointer<CreateForMonitorDelegate>(
                Marshal.ReadIntPtr(vtable, IntPtr.Size * 4));
            var itemId = GraphicsCaptureItemId;
            ThrowIfFailed(createForMonitor(factory.ThisPtr, monitor, ref itemId, out var itemPointer));
            try
            {
                return GraphicsCaptureItem.FromAbi(itemPointer);
            }
            finally
            {
                _ = Marshal.Release(itemPointer);
            }
        }

        private static IDirect3DDevice CreateDirect3DDevice()
        {
            ThrowIfFailed(D3D11CreateDevice(
                0,
                D3dDriverTypeHardware,
                0,
                D3d11CreateDeviceBgraSupport,
                0,
                0,
                D3d11SdkVersion,
                out var device,
                out _,
                out var context));
            nint dxgiDevice = 0;
            nint inspectable = 0;
            try
            {
                var dxgiDeviceId = DxgiDeviceId;
                ThrowIfFailed(Marshal.QueryInterface(device, in dxgiDeviceId, out dxgiDevice));
                ThrowIfFailed(CreateDirect3D11DeviceFromDXGIDevice(dxgiDevice, out inspectable));
                return WinRT.MarshalInterface<IDirect3DDevice>.FromAbi(inspectable);
            }
            finally
            {
                if (inspectable != 0)
                {
                    _ = Marshal.Release(inspectable);
                }
                if (dxgiDevice != 0)
                {
                    _ = Marshal.Release(dxgiDevice);
                }
                if (context != 0)
                {
                    _ = Marshal.Release(context);
                }
                if (device != 0)
                {
                    _ = Marshal.Release(device);
                }
            }
        }

        private void OnFrameArrived(Direct3D11CaptureFramePool sender, object args) =>
            _arrivals.Writer.TryWrite(0);

        private void OnItemClosed(GraphicsCaptureItem sender, object args) =>
            _arrivals.Writer.TryComplete(new AmbientCaptureException(
                AmbientCaptureFailure.TopologyUnavailable,
                "The display capture item has closed."));

        private static void ThrowIfFailed(int result)
        {
            if (result < 0)
            {
                Marshal.ThrowExceptionForHR(result);
            }
        }

        private static void ReleaseWinRtObject(object value)
        {
            if (value is global::WinRT.IWinRTObject winrtObject)
            {
                winrtObject.NativeObject.Dispose();
            }
        }

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int CreateForMonitorDelegate(
            nint @this,
            nint monitor,
            ref Guid iid,
            out nint item);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate bool MonitorEnumCallback(
            nint monitor,
            nint deviceContext,
            nint rectangle,
            nint data);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct MonitorInfoEx
        {
            public uint Size;
            public Rect Monitor;
            public Rect Work;
            public uint Flags;

            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
            public string DeviceName;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct Rect
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool EnumDisplayMonitors(
            nint deviceContext,
            nint clip,
            MonitorEnumCallback callback,
            nint data);

        [DllImport("user32.dll", EntryPoint = "GetMonitorInfoW", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetMonitorInfo(nint monitor, ref MonitorInfoEx info);

        [DllImport("d3d11.dll")]
        private static extern int D3D11CreateDevice(
            nint adapter,
            int driverType,
            nint software,
            uint flags,
            nint featureLevels,
            uint featureLevelCount,
            uint sdkVersion,
            out nint device,
            out int featureLevel,
            out nint immediateContext);

        [DllImport("d3d11.dll")]
        private static extern int CreateDirect3D11DeviceFromDXGIDevice(
            nint dxgiDevice,
            out nint graphicsDevice);
    }

}
