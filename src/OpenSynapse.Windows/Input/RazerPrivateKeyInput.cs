using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using OpenSynapse.Core.Macros;
using OpenSynapse.Windows.Devices;

namespace OpenSynapse.Windows.Input;

/// <summary>
/// Reads Razer report 4 (or the wireless slave report 8) from the private HID
/// collections. These key slots are not HID usages and never reach Raw Input.
/// A failed read is non-fatal; normal keyboard and mouse triggers keep working.
/// </summary>
internal sealed class RazerPrivateKeyInput : IDisposable
{
    private const ushort RazerVendorId = 0x1532;
    private const byte RazerKeyReportId = 4;
    private const byte SlaveKeyReportId = 8;
    private const byte DevicePickUpKey = 93;
    private const byte DevicePutDownKey = 94;

    private readonly Action<MacroInput, bool> _handler;
    private readonly CancellationTokenSource _stop = new();
    private readonly Dictionary<string, HashSet<int>> _pressed =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly List<FileStream> _streams = [];
    private readonly object _gate = new();
    private Task? _completion;
    private bool _started;
    private bool _disposed;

    internal RazerPrivateKeyInput(Action<MacroInput, bool> handler)
    {
        _handler = handler ?? throw new ArgumentNullException(nameof(handler));
    }

    public void Start()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_started)
        {
            throw new InvalidOperationException("Razer private key input is already running.");
        }

        _started = true;
        _completion = Task.Run(() => RunAsync(_stop.Token));
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _stop.Cancel();
        DisposeStreams();

        try
        {
            _completion?.Wait(TimeSpan.FromMilliseconds(500));
        }
        catch
        {
        }
        _stop.Dispose();
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                lock (_gate)
                {
                    _pressed.Clear();
                }
                await ReadAvailableAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch
            {
                // Private key reports are an optional extension to Raw Input.
            }

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
        }
    }

    private async Task ReadAvailableAsync(CancellationToken cancellationToken)
    {
        var interfaces = await WindowsHidDiscovery.FindVendorInterfacesAsync(
            RazerVendorId,
            cancellationToken).ConfigureAwait(false);
        var candidates = interfaces
            .Where(IsPrivateCollection)
            .GroupBy(device => device.DevicePath, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First());
        var readers = new List<Task>();
        try
        {
            foreach (var device in candidates)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (_disposed)
                {
                    break;
                }

                try
                {
                    var stream = OpenReadStream(device);
                    lock (_gate)
                    {
                        if (_disposed)
                        {
                            stream.Dispose();
                            break;
                        }
                        _streams.Add(stream);
                    }
                    readers.Add(ReadLoopAsync(device, stream, cancellationToken));
                }
                catch
                {
                    // A collection may be present but not readable. Skip it and
                    // keep the other collections on the same device available.
                }
            }

            if (readers.Count > 0)
            {
                await Task.WhenAll(readers).ConfigureAwait(false);
            }
        }
        finally
        {
            DisposeStreams();
        }
    }

    private async Task ReadLoopAsync(
        WindowsHidDiscovery.HidInterfaceDescriptor device,
        FileStream stream,
        CancellationToken cancellationToken)
    {
        var buffer = new byte[Math.Max(16, (int)device.InputReportByteLength)];
        var keys = new HashSet<byte>();
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var length = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
                if (length == 0)
                {
                    break;
                }

                if (length < 2 || buffer[0] is not (RazerKeyReportId or SlaveKeyReportId))
                {
                    continue;
                }

                var current = new HashSet<byte>();
                for (var index = 1; index < length; index++)
                {
                    if (buffer[index] != 0)
                    {
                        current.Add(buffer[index]);
                    }
                }

                foreach (var key in keys.Except(current).Order())
                {
                    Publish(device, key, isDown: false);
                }
                foreach (var key in current.Except(keys).Order())
                {
                    Publish(device, key, isDown: true);
                }
                keys = current;
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch
        {
            // HID collections can disappear on wireless reconnects. Keep the
            // other readers alive; the hook is detached and reattached later.
        }
    }

    private void Publish(
        WindowsHidDiscovery.HidInterfaceDescriptor device,
        byte key,
        bool isDown)
    {
        // These are dock/undock notifications, not physical buttons.
        if (key is DevicePickUpKey or DevicePutDownKey)
        {
            return;
        }

        lock (_gate)
        {
            if (!_pressed.TryGetValue(device.PhysicalDeviceKey, out var keys))
            {
                keys = [];
                _pressed[device.PhysicalDeviceKey] = keys;
            }

            if (isDown ? !keys.Add(key) : !keys.Remove(key))
            {
                return;
            }
        }

        _handler(new MacroInput
        {
            Kind = MacroInputKind.RazerKey,
            DeviceId = device.ProductId,
            DeviceName = string.IsNullOrWhiteSpace(device.ProductName)
                ? $"Razer {device.ProductId:X4}"
                : device.ProductName,
            RazerKeyCode = key,
        }, isDown);
    }

    private static bool IsPrivateCollection(WindowsHidDiscovery.HidInterfaceDescriptor device) =>
        device.VendorId == RazerVendorId &&
        device.ProductId != 0 &&
        device.InputReportByteLength > 0 &&
        device.UsagePage is 0x0000 or 0x0080 &&
        // Blade keyboards publish every M-key, Fn shortcut and the microphone indicator
        // as report 4 on MI_01 Col04/Col05. The Product 710 filter runtime owns that
        // stream, and a second reader takes the reports away from it.
        !IsBladeVendorEventCollection(device.DevicePath);

    private static bool IsBladeVendorEventCollection(string path) =>
        path.Contains("&MI_01&COL04#", StringComparison.OrdinalIgnoreCase) ||
        path.Contains("&MI_01&COL05#", StringComparison.OrdinalIgnoreCase);

    private void DisposeStreams()
    {
        lock (_gate)
        {
            foreach (var stream in _streams)
            {
                stream.Dispose();
            }
            _streams.Clear();
        }
    }

    private FileStream OpenReadStream(WindowsHidDiscovery.HidInterfaceDescriptor device)
    {
        var handle = NativeMethods.CreateFile(
            device.DevicePath,
            NativeMethods.GenericRead,
            NativeMethods.FileShareRead | NativeMethods.FileShareWrite,
            IntPtr.Zero,
            NativeMethods.OpenExisting,
            NativeMethods.FileFlagOverlapped,
            IntPtr.Zero);
        if (handle.IsInvalid)
        {
            var error = Marshal.GetLastWin32Error();
            handle.Dispose();
            throw new Win32Exception(error, "Could not open a Razer private HID collection.");
        }

        return new FileStream(
            handle,
            FileAccess.Read,
            Math.Max(16, (int)device.InputReportByteLength),
            isAsync: true);
    }

    private static class NativeMethods
    {
        internal const uint GenericRead = 0x80000000;
        internal const uint FileShareRead = 0x00000001;
        internal const uint FileShareWrite = 0x00000002;
        internal const uint OpenExisting = 3;
        internal const uint FileFlagOverlapped = 0x40000000;

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
