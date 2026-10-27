using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;
using OpenSynapse.Core.Macros;
using OpenSynapse.Windows.Devices;

namespace OpenSynapse.Windows.Input;

/// <summary>
/// Receives keyboard reports from Razer devices through Raw Input. The
/// low-level hook cannot tell which device sent a key; the Raw Input device
/// path can. Naga side plates get their stable 1-12 names, while every other
/// Razer key is exposed as a device-scoped trigger.
/// </summary>
internal sealed class RazerDeviceButtonInput : IDisposable
{
    private const uint WmInput = 0x00FF;
    private const uint RidInput = 0x10000003;
    private const uint RidiDeviceName = 0x20000007;
    private const uint RidevInputSink = 0x00000100;
    private const uint RidevRemove = 0x00000001;
    private const uint RimTypeKeyboard = 1;
    private const ushort RiKeyBreak = 0x0001;
    private const ushort RiKeyE0 = 0x0002;
    private const int GwlpWndProc = -4;
    private const uint WsPopup = 0x80000000;

    private const uint FileShareReadWrite = 0x00000003;
    private const uint OpenExisting = 3;
    private const uint RazerVendorId = 0x1532;

    private readonly Action<MacroInput, bool> _handler;
    private readonly Dictionary<string, RazerDeviceInfo?> _devices =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, HashSet<int>> _pressed = new(StringComparer.OrdinalIgnoreCase);
    private readonly WndProc _windowProc;
    private nint _window;
    private nint _previousWindowProc;
    private bool _started;
    private bool _disposed;

    internal RazerDeviceButtonInput(Action<MacroInput, bool> handler)
    {
        _handler = handler ?? throw new ArgumentNullException(nameof(handler));
        _windowProc = WindowProcedure;
    }

    public void Start()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_started)
        {
            throw new InvalidOperationException("Razer device-button input is already running.");
        }

        var instance = GetModuleHandle(null);
        _window = CreateWindowEx(
            0,
            "STATIC",
            string.Empty,
            WsPopup,
            0,
            0,
            0,
            0,
            0,
            0,
            instance,
            0);
        if (_window == 0)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }

        _previousWindowProc = SetWindowLongPtr(
            _window,
            GwlpWndProc,
            Marshal.GetFunctionPointerForDelegate(_windowProc));
        if (_previousWindowProc == 0)
        {
            var error = Marshal.GetLastWin32Error();
            DestroyWindow(_window);
            _window = 0;
            throw new Win32Exception(error);
        }

        var device = new RawInputDevice
        {
            UsagePage = 0x01,
            Usage = 0x06,
            Flags = RidevInputSink,
            Target = _window,
        };
        if (!RegisterRawInputDevices(
                [device],
                1,
                checked((uint)Marshal.SizeOf<RawInputDevice>())))
        {
            var error = Marshal.GetLastWin32Error();
            SetWindowLongPtr(_window, GwlpWndProc, _previousWindowProc);
            DestroyWindow(_window);
            _window = 0;
            throw new Win32Exception(error);
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
        if (!_started)
        {
            return;
        }

        var device = new RawInputDevice
        {
            UsagePage = 0x01,
            Usage = 0x06,
            Flags = RidevRemove,
        };
        _ = RegisterRawInputDevices(
            [device],
            1,
            checked((uint)Marshal.SizeOf<RawInputDevice>()));
        if (_window != 0)
        {
            _ = SetWindowLongPtr(_window, GwlpWndProc, _previousWindowProc);
            _ = DestroyWindow(_window);
        }

        _window = 0;
        _previousWindowProc = 0;
        _started = false;
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
                // A malformed report must not take down the hook message loop.
            }
        }

        return DefWindowProc(window, message, wParam, lParam);
    }

    private void ProcessInput(nint handle)
    {
        if (_disposed)
        {
            return;
        }

        var headerSize = checked((uint)Marshal.SizeOf<RawInputHeader>());
        uint size = 0;
        if (GetRawInputData(handle, RidInput, 0, ref size, headerSize) != 0 ||
            size == 0 ||
            size > 64 * 1024)
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
            if (header.Type != RimTypeKeyboard)
            {
                return;
            }

            var devicePath = GetDevicePath(header.Device);
            if (string.IsNullOrEmpty(devicePath) || GetDevice(devicePath) is not { } device)
            {
                return;
            }

            var keyboard = Marshal.PtrToStructure<RawKeyboard>(
                nint.Add(buffer, checked((int)headerSize)));
            var sideButton = device.IsNaga
                ? RazerNagaSideButtons.FromKeyboard(
                    keyboard.VirtualKey,
                    keyboard.MakeCode)
                : 0;
            var isDown = (keyboard.Flags & RiKeyBreak) == 0;
            if (sideButton != 0)
            {
                if (!UpdatePressed(devicePath, sideButton, isDown))
                {
                    return;
                }

                _handler(new MacroInput
                {
                    Kind = MacroInputKind.NagaSideButton,
                    SideButton = sideButton,
                }, isDown);
                return;
            }

            var control = 0x10000 | (keyboard.VirtualKey != 0
                ? keyboard.VirtualKey
                : keyboard.MakeCode);
            if (control == 0x10000 || !UpdatePressed(devicePath, control, isDown))
            {
                return;
            }

            _handler(new MacroInput
            {
                Kind = MacroInputKind.DeviceButton,
                DeviceId = device.ProductId,
                DeviceName = device.DisplayName,
                VirtualKey = keyboard.VirtualKey == 0 ? 0xFF : keyboard.VirtualKey,
                ScanCode = keyboard.MakeCode,
                Extended = (keyboard.Flags & RiKeyE0) != 0,
            }, isDown);
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private bool UpdatePressed(string devicePath, int sideButton, bool isDown)
    {
        if (!_pressed.TryGetValue(devicePath, out var keys))
        {
            keys = [];
            _pressed[devicePath] = keys;
        }

        return isDown ? keys.Add(sideButton) : keys.Remove(sideButton);
    }

    private RazerDeviceInfo? GetDevice(string devicePath)
    {
        if (_devices.TryGetValue(devicePath, out var known))
        {
            return known;
        }

        var device = ProbeDevice(devicePath);
        _devices[devicePath] = device;
        return device;
    }

    private static RazerDeviceInfo? ProbeDevice(string devicePath)
    {
        var parsed = DeviceIdParser.TryParse(
            devicePath,
            out var parsedVendorId,
            out var parsedProductId) &&
            parsedVendorId == RazerVendorId &&
            parsedProductId != 0;

        using var handle = CreateFile(
            devicePath,
            0,
            FileShareReadWrite,
            0,
            OpenExisting,
            0,
            0);
        if (handle.IsInvalid)
        {
            return parsed
                ? new RazerDeviceInfo(
                    parsedProductId,
                    null,
                    RazerNagaSideButtons.IsNaga(
                        parsedVendorId,
                        parsedProductId,
                        null))
                : null;
        }

        var attributes = new HidAttributes
        {
            Size = checked((uint)Marshal.SizeOf<HidAttributes>()),
        };
        if (!HidD_GetAttributes(handle, ref attributes) ||
            attributes.VendorId != RazerVendorId)
        {
            return null;
        }

        var product = new byte[256];
        var productName = HidD_GetProductString(handle, product, product.Length)
            ? DecodeHidString(product)
            : null;
        return new RazerDeviceInfo(
            attributes.ProductId,
            productName,
            RazerNagaSideButtons.IsNaga(
                attributes.VendorId,
                attributes.ProductId,
                productName));
    }

    private static string DecodeHidString(byte[] buffer)
    {
        var value = Encoding.Unicode.GetString(buffer);
        var terminator = value.IndexOf('\0');
        return (terminator >= 0 ? value[..terminator] : value).Trim();
    }

    private static string GetDevicePath(nint device)
    {
        uint size = 0;
        if (GetRawInputDeviceInfo(device, RidiDeviceName, null, ref size) == uint.MaxValue ||
            size == 0)
        {
            return string.Empty;
        }

        var path = new StringBuilder(checked((int)size));
        return GetRawInputDeviceInfo(device, RidiDeviceName, path, ref size) == uint.MaxValue
            ? string.Empty
            : path.ToString();
    }

    private delegate nint WndProc(nint window, uint message, nint wParam, nint lParam);

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
    private struct HidAttributes
    {
        internal uint Size;
        internal ushort VendorId;
        internal ushort ProductId;
        internal ushort VersionNumber;
    }

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern nint CreateWindowEx(
        uint extendedStyle,
        string className,
        string windowName,
        uint style,
        int x,
        int y,
        int width,
        int height,
        nint parent,
        nint menu,
        nint instance,
        nint parameter);

    [DllImport("user32.dll", SetLastError = true, EntryPoint = "SetWindowLongPtrW")]
    private static extern nint SetWindowLongPtr(nint window, int index, nint value);

    [DllImport("user32.dll")]
    private static extern nint DefWindowProc(nint window, uint message, nint wParam, nint lParam);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyWindow(nint window);

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

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern nint GetModuleHandle(string? moduleName);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFile(
        string fileName,
        uint desiredAccess,
        uint shareMode,
        nint securityAttributes,
        uint creationDisposition,
        uint flagsAndAttributes,
        nint templateFile);

    [DllImport("hid.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool HidD_GetAttributes(
        SafeFileHandle device,
        ref HidAttributes attributes);

    [DllImport("hid.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool HidD_GetProductString(
        SafeFileHandle device,
        [Out] byte[] buffer,
        int bufferLength);

    private readonly record struct RazerDeviceInfo(
        ushort ProductId,
        string? ProductName,
        bool IsNaga)
    {
        internal string DisplayName => string.IsNullOrWhiteSpace(ProductName)
            ? $"Razer {ProductId:X4}"
            : ProductName;
    }
}
