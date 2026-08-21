using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using OpenSynapse.Core.Devices;
using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.Windows.Devices;

/// <summary>
/// Reads Product 710 hardware-event HID collections directly. This host does
/// not enable Software Mode or suppress the original keyboard input.
/// </summary>
public sealed class BladeHardwareEventHidHost : IAsyncDisposable
{
    internal const int ReportLength = 100;
    private const ushort RazerVendorId = 0x1532;
    private const ushort Product710Id = 0x02C6;
    private const string Col04PathFragment = "&MI_01&Col04#";
    private const string Col05PathFragment = "&MI_01&Col05#";

    private readonly Action<IReadOnlyList<BladeMappingInputEvent>> _eventHandler;
    private readonly Action<string, ReadOnlyMemory<byte>>? _reportHandler;
    private readonly BladeRazerKeyReportDecoder _decoder = new();
    private readonly CancellationTokenSource _stop = new();
    private readonly List<FileStream> _streams = [];
    private Task? _completion;
    private bool _started;
    private bool _disposed;

    public BladeHardwareEventHidHost(
        Action<IReadOnlyList<BladeMappingInputEvent>> eventHandler,
        Action<string, ReadOnlyMemory<byte>>? reportHandler = null)
    {
        _eventHandler = eventHandler ?? throw new ArgumentNullException(nameof(eventHandler));
        _reportHandler = reportHandler;
    }

    public Task Completion => _completion ?? Task.CompletedTask;

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_started)
        {
            throw new InvalidOperationException("Blade hardware-event HID 宿主已经启动。");
        }

        var snapshot = await WindowsHidDiscovery.DiscoverAllAsync(cancellationToken).ConfigureAwait(false);
        var endpoints = SelectProduct710Endpoints(snapshot.Devices);
        if (!endpoints.Any(static endpoint => IsCollection(endpoint.Id, Col04PathFragment)))
        {
            throw new InvalidOperationException("未找到 Blade Product 710 MI_01 Col04 HID collection。");
        }

        try
        {
            foreach (var endpoint in endpoints)
            {
                _streams.Add(OpenReadStream(endpoint.Id));
            }
        }
        catch
        {
            DisposeStreams();
            throw;
        }

        _started = true;
        var reads = endpoints
            .Zip(_streams, (endpoint, stream) => ReadLoopAsync(endpoint.Id, stream, _stop.Token))
            .ToArray();
        _completion = Task.WhenAll(reads);
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _stop.Cancel();
        try
        {
            await Completion.ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested)
        {
        }
        finally
        {
            DisposeStreams();
            _stop.Dispose();
        }

        IReadOnlyList<BladeMappingInputEvent> releases;
        lock (_decoder)
        {
            releases = _decoder.Reset();
        }
        if (releases.Count > 0)
        {
            _eventHandler(releases);
        }
    }

    internal static IReadOnlyList<DeviceDescriptor> SelectProduct710Endpoints(
        IEnumerable<DeviceDescriptor> devices)
    {
        ArgumentNullException.ThrowIfNull(devices);
        return devices
            .Where(static device =>
                device.VendorId == RazerVendorId &&
                device.ProductId == Product710Id &&
                (IsCollection(device.Id, Col04PathFragment) ||
                 IsCollection(device.Id, Col05PathFragment)))
            .ToArray();
    }

    internal IReadOnlyList<BladeMappingInputEvent> ProcessReport(
        string devicePath,
        ReadOnlySpan<byte> report)
    {
        if (!IsCollection(devicePath, Col04PathFragment) ||
            report.Length == 0 ||
            report[0] != BladeRazerKeyReportDecoder.ReportId)
        {
            return [];
        }

        lock (_decoder)
        {
            return _decoder.Process(report);
        }
    }

    private async Task ReadLoopAsync(
        string devicePath,
        FileStream stream,
        CancellationToken cancellationToken)
    {
        var buffer = new byte[ReportLength];
        try
        {
            while (true)
            {
                var length = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
                if (length == 0)
                {
                    throw new EndOfStreamException("Blade hardware-event HID collection 已断开。");
                }

                var report = buffer.AsMemory(0, length).ToArray();
                _reportHandler?.Invoke(devicePath, report);
                var events = ProcessReport(devicePath, report);
                if (events.Count > 0)
                {
                    _eventHandler(events);
                }
            }
        }
        catch
        {
            _stop.Cancel();
            throw;
        }
    }

    private static FileStream OpenReadStream(string devicePath)
    {
        var handle = NativeMethods.CreateFile(
            devicePath,
            NativeMethods.GENERIC_READ,
            NativeMethods.FILE_SHARE_READ | NativeMethods.FILE_SHARE_WRITE,
            IntPtr.Zero,
            NativeMethods.OPEN_EXISTING,
            NativeMethods.FILE_FLAG_OVERLAPPED,
            IntPtr.Zero);
        if (handle.IsInvalid)
        {
            var error = Marshal.GetLastWin32Error();
            handle.Dispose();
            throw new Win32Exception(error, "无法打开 Blade hardware-event HID collection。");
        }

        return new FileStream(handle, FileAccess.Read, ReportLength, isAsync: true);
    }

    private static bool IsCollection(string path, string fragment) =>
        path.Contains(fragment, StringComparison.OrdinalIgnoreCase);

    private void DisposeStreams()
    {
        foreach (var stream in _streams)
        {
            stream.Dispose();
        }
        _streams.Clear();
    }

    private static class NativeMethods
    {
        internal const uint GENERIC_READ = 0x80000000;
        internal const uint FILE_SHARE_READ = 0x00000001;
        internal const uint FILE_SHARE_WRITE = 0x00000002;
        internal const uint OPEN_EXISTING = 3;
        internal const uint FILE_FLAG_OVERLAPPED = 0x40000000;

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        internal static extern SafeFileHandle CreateFile(
            string fileName,
            uint desiredAccess,
            uint shareMode,
            IntPtr securityAttributes,
            uint creationDisposition,
            uint flagsAndAttributes,
            IntPtr templateFile);
    }
}
