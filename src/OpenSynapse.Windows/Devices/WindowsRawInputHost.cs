using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.Windows.Devices;

/// <summary>
/// Opt-in WM_INPUT host for the Blade internal keyboard collection. It only
/// observes raw input; suppression and output injection stay in the caller.
/// </summary>
public sealed class WindowsRawInputHost : IDisposable
{
    private const uint WmInput = 0x00FF;
    private const uint RidInput = 0x10000003;
    private const uint RidiDeviceName = 0x20000007;
    private const uint RidevInputSink = 0x00000100;
    private const uint RidevRemove = 0x00000001;
    private const int GwlpWndProc = -4;

    private readonly nint _windowHandle;
    private readonly BladeRawInputEventDecoder _decoder;
    private readonly Action<IReadOnlyList<BladeMappingInputEvent>> _eventHandler;
    private readonly WndProc _wndProc;
    private nint _previousWndProc;
    private bool _started;
    private bool _disposed;
    private string? _lastError;

    public WindowsRawInputHost(
        nint windowHandle,
        BladeRawInputEventDecoder decoder,
        Action<IReadOnlyList<BladeMappingInputEvent>> eventHandler)
    {
        if (windowHandle == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(windowHandle));
        }

        _windowHandle = windowHandle;
        _decoder = decoder ?? throw new ArgumentNullException(nameof(decoder));
        _eventHandler = eventHandler ?? throw new ArgumentNullException(nameof(eventHandler));
        _wndProc = WindowProcedure;
    }

    public string? LastError => Volatile.Read(ref _lastError);

    public void Start()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_started)
        {
            throw new InvalidOperationException("Raw Input 宿主已经启动。");
        }

        var devices = new[]
        {
            new RawInputDevice
            {
                UsagePage = 0x01,
                Usage = 0x06,
                Flags = RidevInputSink,
                Target = _windowHandle,
            },
        };
        if (!RegisterRawInputDevices(devices, 1, (uint)Marshal.SizeOf<RawInputDevice>()))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "无法注册 Blade Raw Input 键盘设备。");
        }

        var procedure = Marshal.GetFunctionPointerForDelegate(_wndProc);
        _previousWndProc = SetWindowLongPtr(_windowHandle, GwlpWndProc, procedure);
        if (_previousWndProc == 0)
        {
            RegisterRawInputDevices(
                [new RawInputDevice { UsagePage = 0x01, Usage = 0x06, Flags = RidevRemove }],
                1,
                (uint)Marshal.SizeOf<RawInputDevice>());
            throw new Win32Exception(Marshal.GetLastWin32Error(), "无法安装 Blade Raw Input 窗口回调。");
        }

        _started = true;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_started)
        {
            SetWindowLongPtr(_windowHandle, GwlpWndProc, _previousWndProc);
            RegisterRawInputDevices(
                [new RawInputDevice { UsagePage = 0x01, Usage = 0x06, Flags = RidevRemove }],
                1,
                (uint)Marshal.SizeOf<RawInputDevice>());
            _decoder.Reset();
            _started = false;
        }
    }

    private nint WindowProcedure(nint hwnd, uint message, nint wParam, nint lParam)
    {
        if (message == WmInput)
        {
            try
            {
                ProcessInput(lParam);
            }
            catch (Exception exception)
            {
                Volatile.Write(ref _lastError, exception.Message);
            }
        }

        return CallWindowProc(_previousWndProc, hwnd, message, wParam, lParam);
    }

    private void ProcessInput(nint rawInputHandle)
    {
        uint size = 0;
        var headerSize = (uint)(IntPtr.Size == 8 ? 24 : 16);
        if (GetRawInputData(rawInputHandle, RidInput, 0, ref size, headerSize) == uint.MaxValue ||
            size == 0 || size > 64 * 1024)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "无法读取 Blade Raw Input 大小。");
        }

        var rawInput = new byte[size];
        var buffer = Marshal.AllocHGlobal((int)size);
        try
        {
            var actual = GetRawInputData(
                rawInputHandle,
                RidInput,
                buffer,
                ref size,
                headerSize);
            if (actual == uint.MaxValue)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "无法读取 Blade Raw Input 数据。");
            }

            Marshal.Copy(buffer, rawInput, 0, (int)Math.Min(actual, (uint)rawInput.Length));
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }

        var device = IntPtr.Size == 8
            ? new nint(BitConverter.ToInt64(rawInput, 8))
            : new nint(BitConverter.ToInt32(rawInput, 8));
        var devicePath = GetDevicePath(device);
        var events = _decoder.Process(devicePath, rawInput);
        if (events.Count > 0)
        {
            _eventHandler(events);
        }
    }

    private static string GetDevicePath(nint device)
    {
        uint size = 0;
        if (GetRawInputDeviceInfo(device, RidiDeviceName, null, ref size) == uint.MaxValue || size == 0)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "无法读取 Blade Raw Input 设备路径大小。");
        }

        var path = new StringBuilder((int)size);
        if (GetRawInputDeviceInfo(device, RidiDeviceName, path, ref size) == uint.MaxValue)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "无法读取 Blade Raw Input 设备路径。");
        }

        return path.ToString();
    }

    private delegate nint WndProc(nint hwnd, uint message, nint wParam, nint lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct RawInputDevice
    {
        internal ushort UsagePage;
        internal ushort Usage;
        internal uint Flags;
        internal nint Target;
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RegisterRawInputDevices(
        RawInputDevice[] devices,
        uint deviceCount,
        uint deviceSize);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetRawInputData(
        nint rawInput,
        uint command,
        nint data,
        ref uint size,
        uint headerSize);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern uint GetRawInputDeviceInfo(
        nint device,
        uint command,
        StringBuilder? data,
        ref uint size);

    [DllImport("user32.dll", SetLastError = true, EntryPoint = "SetWindowLongPtrW")]
    private static extern nint SetWindowLongPtr(nint windowHandle, int index, nint newValue);

    [DllImport("user32.dll")]
    private static extern nint CallWindowProc(
        nint previousWndProc,
        nint windowHandle,
        uint message,
        nint wParam,
        nint lParam);
}
