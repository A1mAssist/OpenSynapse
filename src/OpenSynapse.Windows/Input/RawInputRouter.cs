using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;

namespace OpenSynapse.Windows.Input;

/// <summary>One decoded WM_INPUT frame; keyboard frames carry the scan code, HID frames the raw report.</summary>
internal readonly record struct RawInputFrame(
    string DevicePath,
    bool IsHid,
    ushort MakeCode,
    ushort Flags,
    ushort VirtualKey,
    byte[]? Report);

/// <summary>
/// The single owner of this process's keyboard Raw Input registration. Windows keeps
/// one target window per usage page and usage, so a second registration silently takes
/// the stream away from the first: the macro layer's keyboard sink used to disconnect
/// the Blade lighting adapter from every key press. Consumers subscribe here.
/// </summary>
internal static class RawInputRouter
{
    private const uint WmInput = 0x00FF;
    private const uint WmQuit = 0x0012;
    private const uint RidInput = 0x10000003;
    private const uint RidiDeviceName = 0x20000007;
    private const uint RimTypeKeyboard = 1;
    private const uint RimTypeHid = 2;
    private const uint RidevInputSink = 0x00000100;
    private const uint RidevRemove = 0x00000001;
    private const uint RidevPageOnly = 0x00000020;
    private const int StartTimeoutMilliseconds = 10_000;
    private static readonly nint MessageOnlyWindow = new(-3);

    private static readonly object Gate = new();
    private static readonly List<(int Id, Action<RawInputFrame> Handler)> Subscribers = [];
    private static Host? _host;
    private static int _nextId;

    internal static IDisposable Subscribe(Action<RawInputFrame> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        lock (Gate)
        {
            _host ??= Host.Start();
            var id = ++_nextId;
            Subscribers.Add((id, handler));
            return new Token(id);
        }
    }

    private static void Remove(int id)
    {
        Host? host = null;
        lock (Gate)
        {
            Subscribers.RemoveAll(entry => entry.Id == id);
            if (Subscribers.Count == 0)
            {
                host = _host;
                _host = null;
            }
        }

        host?.Dispose();
    }

    private static void Dispatch(RawInputFrame frame)
    {
        Action<RawInputFrame>[] handlers;
        lock (Gate)
        {
            handlers = Subscribers.Select(entry => entry.Handler).ToArray();
        }

        foreach (var handler in handlers)
        {
            try
            {
                handler(frame);
            }
            catch
            {
                // One consumer failing must not starve the others.
            }
        }
    }

    private sealed class Token(int id) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                Remove(id);
            }
        }
    }

    private sealed class Host : IDisposable
    {
        private readonly ManualResetEventSlim _ready = new(false);
        private readonly WindowProc _windowProc;
        private readonly Dictionary<nint, string> _devicePaths = [];
        private Thread? _thread;
        private nint _window;
        private nint _previousWindowProc;
        private uint _threadId;
        private Exception? _startupError;
        private int _disposed;

        private Host() => _windowProc = WindowProcedure;

        internal static Host Start()
        {
            var host = new Host();
            var thread = new Thread(host.Run)
            {
                IsBackground = true,
                Name = "OpenSynapse raw input router",
            };
            thread.SetApartmentState(ApartmentState.STA);
            host._thread = thread;
            thread.Start();

            if (!host._ready.Wait(StartTimeoutMilliseconds))
            {
                host.Dispose();
                throw new Win32Exception(1460, "The raw input router did not start in time.");
            }

            if (host._startupError is { } error)
            {
                host.Dispose();
                throw new Win32Exception(0, error.Message);
            }

            return host;
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
            {
                return;
            }

            var threadId = Volatile.Read(ref _threadId);
            if (threadId != 0)
            {
                _ = PostThreadMessage(threadId, WmQuit, 0, 0);
            }

            var thread = _thread;
            if (thread is not null && thread != Thread.CurrentThread)
            {
                _ = thread.Join(TimeSpan.FromMilliseconds(500));
            }
        }

        private void Run()
        {
            var instance = GetModuleHandle(null);
            try
            {
                _threadId = GetCurrentThreadId();
                _window = CreateWindowEx(
                    0, "STATIC", string.Empty, 0, 0, 0, 0, 0,
                    MessageOnlyWindow, 0, instance, 0);
                if (_window == 0)
                {
                    throw new Win32Exception(Marshal.GetLastWin32Error());
                }

                _previousWindowProc = SetWindowLongPtr(
                    _window, -4, Marshal.GetFunctionPointerForDelegate(_windowProc));
                if (_previousWindowProc == 0)
                {
                    throw new Win32Exception(Marshal.GetLastWin32Error());
                }

                RegisterInputs(removing: false);
                _ready.Set();

                while (GetMessage(out var message, 0, 0, 0) > 0)
                {
                    _ = DispatchMessage(in message);
                }
            }
            catch (Exception exception)
            {
                _startupError = exception;
                _ready.Set();
            }
            finally
            {
                try
                {
                    RegisterInputs(removing: true);
                }
                catch
                {
                }

                if (_window != 0)
                {
                    _ = SetWindowLongPtr(_window, -4, _previousWindowProc);
                    _ = DestroyWindow(_window);
                    _window = 0;
                }
            }
        }

        private static void RegisterInputs(bool removing)
        {
            RawInputDevice[] devices =
            [
                new()
                {
                    UsagePage = 0x01,
                    Usage = 0,
                    Flags = removing ? RidevRemove | RidevPageOnly : RidevInputSink | RidevPageOnly,
                },
                new()
                {
                    UsagePage = 0x0C,
                    Usage = 0,
                    Flags = removing ? RidevRemove | RidevPageOnly : RidevInputSink | RidevPageOnly,
                },
            ];
            if (!RegisterRawInputDevices(
                    devices,
                    checked((uint)devices.Length),
                    checked((uint)Marshal.SizeOf<RawInputDevice>())))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error());
            }
        }

        private nint WindowProcedure(nint window, uint message, nint wParam, nint lParam)
        {
            if (message == WmInput)
            {
                try
                {
                    ProcessInput(lParam);
                }
                catch
                {
                    // A malformed report must not take down the message loop.
                }
            }

            return DefWindowProc(window, message, wParam, lParam);
        }

        private void ProcessInput(nint handle)
        {
            var headerSize = checked((uint)Marshal.SizeOf<RawInputHeader>());
            uint size = 0;
            if (GetRawInputData(handle, RidInput, 0, ref size, headerSize) != 0 ||
                size == 0 || size > 64 * 1024)
            {
                return;
            }

            var buffer = Marshal.AllocHGlobal(checked((int)size));
            try
            {
                if (GetRawInputData(handle, RidInput, buffer, ref size, headerSize) != size)
                {
                    return;
                }

                var header = Marshal.PtrToStructure<RawInputHeader>(buffer);
                if (header.Type is not (RimTypeKeyboard or RimTypeHid))
                {
                    return;
                }

                var devicePath = GetDevicePath(header.Device);
                if (string.IsNullOrEmpty(devicePath))
                {
                    return;
                }

                if (header.Type == RimTypeKeyboard)
                {
                    var keyboard = Marshal.PtrToStructure<RawKeyboard>(nint.Add(buffer, checked((int)headerSize)));
                    Dispatch(new RawInputFrame(devicePath, false, keyboard.MakeCode, keyboard.Flags, keyboard.VirtualKey, null));
                    return;
                }

                if (size < headerSize + 8)
                {
                    return;
                }

                var data = nint.Add(buffer, checked((int)headerSize));
                var reportSize = Marshal.ReadInt32(data);
                var reportCount = Marshal.ReadInt32(data, 4);
                if (reportSize <= 0 || reportCount <= 0)
                {
                    return;
                }

                if (checked(reportSize * reportCount) > size - headerSize - 8)
                {
                    return;
                }

                for (var index = 0; index < reportCount; index++)
                {
                    var report = new byte[reportSize];
                    Marshal.Copy(nint.Add(data, 8 + (index * reportSize)), report, 0, reportSize);
                    Dispatch(new RawInputFrame(devicePath, true, 0, 0, 0, report));
                }
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }

        private string GetDevicePath(nint device)
        {
            if (_devicePaths.TryGetValue(device, out var cached))
            {
                return cached;
            }

            uint characterCount = 0;
            if (GetRawInputDeviceInfo(device, RidiDeviceName, null, ref characterCount) == uint.MaxValue ||
                characterCount == 0)
            {
                return _devicePaths[device] = string.Empty;
            }

            var name = new StringBuilder(checked((int)characterCount));
            return _devicePaths[device] =
                GetRawInputDeviceInfo(device, RidiDeviceName, name, ref characterCount) == uint.MaxValue
                    ? string.Empty
                    : name.ToString();
        }

        private delegate nint WindowProc(nint window, uint message, nint wParam, nint lParam);

        [StructLayout(LayoutKind.Sequential)]
        private readonly struct RawInputHeader
        {
            internal readonly uint Type;
            internal readonly uint Size;
            internal readonly nint Device;
            internal readonly nuint WParam;
        }

        [StructLayout(LayoutKind.Sequential)]
        private readonly struct RawKeyboard
        {
            internal readonly ushort MakeCode;
            internal readonly ushort Flags;
            internal readonly ushort Reserved;
            internal readonly ushort VirtualKey;
            internal readonly uint Message;
            internal readonly uint ExtraInformation;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct RawInputDevice
        {
            internal ushort UsagePage;
            internal ushort Usage;
            internal uint Flags;
            internal nint Target;
        }

        [StructLayout(LayoutKind.Sequential)]
        private readonly struct NativeMessage
        {
            internal readonly nint Window;
            internal readonly uint Message;
            internal readonly nuint WParam;
            internal readonly nint LParam;
            internal readonly uint Time;
            internal readonly int X;
            internal readonly int Y;
            internal readonly uint Private;
        }

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern nint CreateWindowEx(
            uint extendedStyle, string className, string windowName, uint style,
            int x, int y, int width, int height,
            nint parent, nint menu, nint instance, nint parameter);

        [DllImport("user32.dll", SetLastError = true, EntryPoint = "SetWindowLongPtrW")]
        private static extern nint SetWindowLongPtr(nint window, int index, nint value);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool DestroyWindow(nint window);

        [DllImport("user32.dll")]
        private static extern nint DefWindowProc(nint window, uint message, nint wParam, nint lParam);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool RegisterRawInputDevices(
            [In] RawInputDevice[] devices, uint deviceCount, uint structureSize);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint GetRawInputData(nint rawInput, uint command, nint data, ref uint size, uint headerSize);

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern uint GetRawInputDeviceInfo(nint device, uint command, StringBuilder? data, ref uint size);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern int GetMessage(out NativeMessage message, nint window, uint minimum, uint maximum);

        [DllImport("user32.dll")]
        private static extern nint DispatchMessage(in NativeMessage message);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool PostThreadMessage(uint threadId, uint message, nuint wParam, nint lParam);

        [DllImport("kernel32.dll")]
        private static extern uint GetCurrentThreadId();

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        private static extern nint GetModuleHandle(string? moduleName);
    }
}