using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;
using OpenSynapse.Core.Macros;
using OpenSynapse.Windows.Devices;

namespace OpenSynapse.Windows.Input;

/// <summary>
/// Receives keyboard reports from Razer devices through the shared Raw Input
/// router. The low-level hook cannot tell which device sent a key; the Raw Input
/// device path can. Naga side plates get their stable 1-12 names, while every other
/// Razer key is exposed as a device-scoped trigger.
/// </summary>
internal sealed class RazerDeviceButtonInput : IDisposable
{
    private const ushort RiKeyBreak = 0x0001;
    private const ushort RiKeyE0 = 0x0002;

    private const uint FileShareReadWrite = 0x00000003;
    private const uint OpenExisting = 3;
    private const uint RazerVendorId = 0x1532;

    private readonly Action<MacroInput, bool> _handler;
    private readonly Dictionary<string, RazerDeviceInfo?> _devices =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, HashSet<int>> _pressed = new(StringComparer.OrdinalIgnoreCase);
    private IDisposable? _subscription;
    private bool _started;
    private bool _disposed;

    internal RazerDeviceButtonInput(Action<MacroInput, bool> handler)
    {
        _handler = handler ?? throw new ArgumentNullException(nameof(handler));
    }

    public void Start()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_started)
        {
            throw new InvalidOperationException("Razer device-button input is already running.");
        }

        // RawInputRouter owns the process's single keyboard registration. Registering
        // here directly would take the stream away from the Blade lighting adapter and
        // leave the keyboard's per-key lights dark.
        _subscription = RawInputRouter.Subscribe(ProcessFrame);
        _started = true;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _subscription?.Dispose();
        _subscription = null;
        _started = false;
    }

    private void ProcessFrame(RawInputFrame frame)
    {
        if (_disposed || frame.IsHid)
        {
            return;
        }

        var devicePath = frame.DevicePath;
        if (string.IsNullOrEmpty(devicePath) || GetDevice(devicePath) is not { } device)
        {
            return;
        }

        var sideButton = device.IsNaga
            ? RazerNagaSideButtons.FromKeyboard(frame.VirtualKey, frame.MakeCode)
            : 0;
        var isDown = (frame.Flags & RiKeyBreak) == 0;
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

        var control = 0x10000 | (frame.VirtualKey != 0 ? frame.VirtualKey : frame.MakeCode);
        if (control == 0x10000 || !UpdatePressed(devicePath, control, isDown))
        {
            return;
        }

        _handler(new MacroInput
        {
            Kind = MacroInputKind.DeviceButton,
            DeviceId = device.ProductId,
            DeviceName = device.DisplayName,
            VirtualKey = frame.VirtualKey == 0 ? 0xFF : frame.VirtualKey,
            ScanCode = frame.MakeCode,
            Extended = (frame.Flags & RiKeyE0) != 0,
        }, isDown);
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

    [StructLayout(LayoutKind.Sequential)]
    private struct HidAttributes
    {
        internal uint Size;
        internal ushort VendorId;
        internal ushort ProductId;
        internal ushort VersionNumber;
    }

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