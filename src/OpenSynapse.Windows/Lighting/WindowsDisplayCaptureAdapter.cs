using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Threading.Channels;
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

internal sealed class WindowsDisplayCaptureAdapter : ILightingInputAdapter
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
                    throw new InvalidOperationException("Ambient Awareness 已经启动。");
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
            worker = _worker ?? throw new InvalidOperationException("Ambient Awareness 尚未启动。");
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

    internal static AmbientCaptureFrame ReduceEdgeBand(
        ReadOnlySpan<byte> bgra,
        int width,
        int height,
        int stride,
        int edgeBandPixels,
        int outputWidth = BladeLightingLayout.LogicalColumns,
        int outputHeight = BladeLightingLayout.LogicalRows)
    {
        if (width <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(width));
        }
        if (height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(height));
        }
        if (stride < checked(width * 4) || bgra.Length < checked(stride * height))
        {
            throw new ArgumentOutOfRangeException(nameof(stride));
        }
        if (edgeBandPixels <= 0 || edgeBandPixels > Math.Min(width, height))
        {
            throw new ArgumentOutOfRangeException(nameof(edgeBandPixels));
        }
        if (outputWidth <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(outputWidth));
        }
        if (outputHeight <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(outputHeight));
        }

        var pixels = new RazerRgb[checked(outputWidth * outputHeight)];
        for (var row = 0; row < outputHeight; row++)
        {
            for (var column = 0; column < outputWidth; column++)
            {
                var topDistance = (row + 0.5) / outputHeight;
                var bottomDistance = 1 - topDistance;
                var leftDistance = (column + 0.5) / outputWidth;
                var rightDistance = 1 - leftDistance;
                var nearest = Math.Min(Math.Min(topDistance, bottomDistance), Math.Min(leftDistance, rightDistance));

                int x0;
                int x1;
                int y0;
                int y1;
                if (nearest == topDistance)
                {
                    (x0, x1) = Segment(column, outputWidth, width);
                    y0 = 0;
                    y1 = edgeBandPixels;
                }
                else if (nearest == bottomDistance)
                {
                    (x0, x1) = Segment(column, outputWidth, width);
                    y0 = height - edgeBandPixels;
                    y1 = height;
                }
                else if (nearest == leftDistance)
                {
                    x0 = 0;
                    x1 = edgeBandPixels;
                    (y0, y1) = Segment(row, outputHeight, height);
                }
                else
                {
                    x0 = width - edgeBandPixels;
                    x1 = width;
                    (y0, y1) = Segment(row, outputHeight, height);
                }

                pixels[row * outputWidth + column] = Average(bgra, stride, x0, x1, y0, y1);
            }
        }
        return new AmbientCaptureFrame(Array.AsReadOnly(pixels), outputWidth, outputHeight);
    }

    internal static RawDisplayFrame CopyPixels(SoftwareBitmap bitmap, int width, int height)
    {
        if (width <= 0 || height <= 0 || width > bitmap.PixelWidth || height > bitmap.PixelHeight)
        {
            throw new AmbientCaptureException(
                AmbientCaptureFailure.TopologyUnavailable,
                "Windows 返回了无效的显示器捕获尺寸。");
        }
        if (bitmap.BitmapPixelFormat != BitmapPixelFormat.Bgra8)
        {
            throw new AmbientCaptureException(
                AmbientCaptureFailure.CaptureFailed,
                "Windows 返回了非 BGRA8 的显示器捕获帧。");
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
                var edgeBand = Math.Clamp(
                    (int)Math.Ceiling(Math.Min(raw.Width, raw.Height) * _edgeBandFraction),
                    1,
                    Math.Min(raw.Width, raw.Height));
                writer.TryWrite(ReduceEdgeBand(
                    raw.Bgra,
                    raw.Width,
                    raw.Height,
                    raw.Stride,
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
                await worker.ConfigureAwait(false);
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
        throw new InvalidOperationException("显示器捕获已停止。");
    }

    private static (int Start, int End) Segment(int index, int segments, int length)
    {
        var start = (int)((long)index * length / segments);
        var end = Math.Max(start + 1, (int)((long)(index + 1) * length / segments));
        return (start, Math.Min(end, length));
    }

    private static RazerRgb Average(
        ReadOnlySpan<byte> bgra,
        int stride,
        int x0,
        int x1,
        int y0,
        int y1)
    {
        long red = 0;
        long green = 0;
        long blue = 0;
        var count = checked((x1 - x0) * (y1 - y0));
        for (var y = y0; y < y1; y++)
        {
            for (var x = x0; x < x1; x++)
            {
                var offset = y * stride + x * 4;
                blue += bgra[offset];
                green += bgra[offset + 1];
                red += bgra[offset + 2];
            }
        }
        return new RazerRgb(
            RoundedAverage(red, count),
            RoundedAverage(green, count),
            RoundedAverage(blue, count));
    }

    private static byte RoundedAverage(long value, int count) =>
        checked((byte)((value + count / 2) / count));

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

            return OpenInternalDisplay(borderless);
        }

        private static IDisplayCaptureSession OpenInternalDisplay(bool borderless)
        {
            if (!GraphicsCaptureSession.IsSupported())
            {
                throw new AmbientCaptureException(
                    AmbientCaptureFailure.Unsupported,
                    "当前 Windows 版本或显卡驱动不支持 Graphics Capture。");
            }

            string sourceName;
            try
            {
                sourceName = new WindowsInternalDisplayController().ResolveSourceName();
            }
            catch (Exception exception) when (exception is InvalidOperationException or Win32Exception)
            {
                throw new AmbientCaptureException(
                    AmbientCaptureFailure.TopologyUnavailable,
                    "无法唯一确定当前内置显示器。",
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
                    "Windows 拒绝了内置显示器捕获权限。",
                    exception);
            }
            catch (COMException exception)
            {
                throw new AmbientCaptureException(
                    AmbientCaptureFailure.CaptureFailed,
                    "Windows 无法创建内置显示器捕获项。",
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
                    "Windows 无法启动内置显示器捕获。",
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
                        "内置显示器捕获已因拓扑变化关闭。",
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
                            "读取内置显示器捕获帧失败。",
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

        private static nint FindMonitor(string sourceName)
        {
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
                    "Windows 无法枚举显示器。",
                    new Win32Exception(Marshal.GetLastWin32Error()));
            }
            if (matches.Count != 1)
            {
                throw new AmbientCaptureException(
                    AmbientCaptureFailure.TopologyUnavailable,
                    matches.Count == 0
                        ? "找不到内置显示器对应的 HMONITOR。"
                        : "内置显示器映射到多个 HMONITOR。");
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
                "内置显示器捕获项已关闭。"));

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
