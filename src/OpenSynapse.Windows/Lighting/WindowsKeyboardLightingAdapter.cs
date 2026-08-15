using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Channels;

namespace OpenSynapse.Windows.Lighting;

internal readonly record struct RawKeyboardObservation(
    string DeviceName,
    ushort ScanCode,
    ushort Flags,
    ushort VirtualKey);

internal readonly record struct RawHidObservation(
    string DeviceName,
    byte[] Report);

internal sealed class WindowsKeyboardLightingAdapter : ILightingInputAdapter
{
    internal const uint KeyUpFlag = 0x01;
    internal const uint ExtendedFlag = 0x02;
    private const uint WmInput = 0x00FF;
    private const uint WmQuit = 0x0012;
    private const uint RidInput = 0x10000003;
    private const uint RidiDeviceName = 0x20000007;
    private const uint RimTypeKeyboard = 1;
    private const uint RimTypeHid = 2;
    private const uint RidevInputSink = 0x00000100;
    private const uint RidevRemove = 0x00000001;
    private const uint RidevPageOnly = 0x00000020;
    private static readonly nint MessageOnlyWindow = new(-3);
    private static readonly TimeSpan Debounce = TimeSpan.FromMilliseconds(20);
    private static readonly IReadOnlyDictionary<(uint ScanCode, bool Extended), (int Row, int Column)> ScanCodeMap =
        CreateScanCodeMap();
    private static readonly IReadOnlyDictionary<byte, (int Row, int Column)> RazerKeyMap =
        new Dictionary<byte, (int Row, int Column)>
        {
            [0x03] = (3, 15),
            [0xD3] = (4, 15),
            [0xD4] = (5, 15),
        };

    private readonly Channel<QuickLightingKeyEvent> _events = Channel.CreateBounded<QuickLightingKeyEvent>(
        new BoundedChannelOptions(64)
        {
            SingleReader = true,
            SingleWriter = true,
            FullMode = BoundedChannelFullMode.DropOldest,
        });
    private readonly Dictionary<(uint ScanCode, bool Extended), TimeSpan> _lastByKey = [];
    private readonly HashSet<byte> _pressedRazerKeys = [];
    private readonly Dictionary<nint, (bool IsBlade, string Name)> _devices = [];
    private readonly Action<RawKeyboardObservation>? _observer;
    private readonly Action<RawHidObservation>? _hidObserver;
    private readonly WindowProc _windowProc;
    private readonly Stopwatch _clock = new();
    private readonly TaskCompletionSource _started = NewCompletion();
    private readonly TaskCompletionSource _stopped = NewCompletion();
    private readonly string _windowClassName = $"OpenSynapse.RawKeyboard.{Guid.NewGuid():N}";
    private Thread? _thread;
    private nint _window;
    private ushort _windowClass;
    private uint _threadId;
    private Exception? _failure;
    private int _stopRequested;
    private int _disposed;

    internal WindowsKeyboardLightingAdapter(
        Action<RawKeyboardObservation>? observer = null,
        Action<RawHidObservation>? hidObserver = null)
    {
        _observer = observer;
        _hidObserver = hidObserver;
        _windowProc = HandleWindowMessage;
    }

    public async ValueTask StartAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        if (_thread is null)
        {
            _thread = new Thread(RunMessageLoop)
            {
                IsBackground = true,
                Name = "OpenSynapse Blade raw keyboard input",
            };
            _thread.Start();
        }

        await _started.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask StopAsync()
    {
        if (Interlocked.Exchange(ref _stopRequested, 1) == 0)
        {
            var threadId = Volatile.Read(ref _threadId);
            if (threadId != 0)
            {
                _ = PostThreadMessage(threadId, WmQuit, 0, 0);
            }
        }

        if (_thread is not null)
        {
            await _stopped.Task.ConfigureAwait(false);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
        {
            await StopAsync().ConfigureAwait(false);
        }
    }

    internal void DrainTo(ICollection<QuickLightingKeyEvent> destination)
    {
        ArgumentNullException.ThrowIfNull(destination);
        while (_events.Reader.TryRead(out var item))
        {
            destination.Add(item);
        }
        if (_failure is not null)
        {
            ExceptionDispatchInfo.Capture(_failure).Throw();
        }
    }

    internal bool TryTranslate(
        uint scanCode,
        uint flags,
        TimeSpan at,
        out QuickLightingKeyEvent keyEvent)
    {
        keyEvent = default;
        var physicalKey = (scanCode, (flags & ExtendedFlag) != 0);
        if ((flags & KeyUpFlag) != 0 ||
            !ScanCodeMap.TryGetValue(physicalKey, out var position) ||
            !BladeLightingLayout.TryGetDevicePosition(
                position.Row, position.Column, out _, out _))
        {
            return false;
        }

        if (_lastByKey.TryGetValue(physicalKey, out var previous) && at - previous < Debounce)
        {
            return false;
        }

        _lastByKey[physicalKey] = at;
        keyEvent = new QuickLightingKeyEvent(position.Row, position.Column, at);
        return true;
    }

    internal void TranslateRazerKeyReport(
        ReadOnlySpan<byte> report,
        TimeSpan at,
        ICollection<QuickLightingKeyEvent> destination)
    {
        ArgumentNullException.ThrowIfNull(destination);
        if (report.Length == 0 || report[0] != 0x04)
        {
            return;
        }

        var current = new HashSet<byte>();
        foreach (var key in report[1..])
        {
            if (key != 0)
            {
                current.Add(key);
            }
        }
        foreach (var key in current)
        {
            if (!_pressedRazerKeys.Contains(key) && RazerKeyMap.TryGetValue(key, out var position))
            {
                destination.Add(new QuickLightingKeyEvent(position.Row, position.Column, at));
            }
        }
        _pressedRazerKeys.Clear();
        _pressedRazerKeys.UnionWith(current);
    }

    internal static bool IsBladeKeyboardDevice(string deviceName) =>
        deviceName.Contains("VID_1532&PID_02C6", StringComparison.OrdinalIgnoreCase);

    private void RunMessageLoop()
    {
        var instance = GetModuleHandle(null);
        try
        {
            _threadId = GetCurrentThreadId();
            var windowClass = new WindowClass
            {
                Size = checked((uint)Marshal.SizeOf<WindowClass>()),
                Instance = instance,
                WindowProc = _windowProc,
                ClassName = _windowClassName,
            };
            _windowClass = RegisterClassEx(in windowClass);
            if (_windowClass == 0)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error());
            }

            _window = CreateWindowEx(
                0,
                _windowClassName,
                _windowClassName,
                0,
                0,
                0,
                0,
                0,
                MessageOnlyWindow,
                0,
                instance,
                0);
            if (_window == 0)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error());
            }

            RegisterInputs(_window, removing: false);
            _clock.Restart();
            _started.TrySetResult();

            while (true)
            {
                var result = GetMessage(out var message, 0, 0, 0);
                if (result == 0)
                {
                    break;
                }
                if (result < 0)
                {
                    throw new Win32Exception(Marshal.GetLastWin32Error());
                }
                _ = TranslateMessage(in message);
                _ = DispatchMessage(in message);
            }
        }
        catch (Exception exception)
        {
            _failure ??= exception;
            _started.TrySetException(exception);
        }
        finally
        {
            if (_window != 0)
            {
                try
                {
                    RegisterInputs(0, removing: true);
                }
                catch
                {
                }
                _ = DestroyWindow(_window);
                _window = 0;
            }
            if (_windowClass != 0)
            {
                _ = UnregisterClass(_windowClassName, instance);
                _windowClass = 0;
            }
            _events.Writer.TryComplete(_failure);
            _stopped.TrySetResult();
        }
    }

    private nint HandleWindowMessage(nint window, uint message, nuint wParam, nint lParam)
    {
        if (message == WmInput)
        {
            try
            {
                ProcessRawInput(lParam);
            }
            catch (Exception exception)
            {
                _failure ??= exception;
                _ = PostThreadMessage(_threadId, WmQuit, 0, 0);
            }
        }
        return DefWindowProc(window, message, wParam, lParam);
    }

    private void ProcessRawInput(nint handle)
    {
        var headerSize = checked((uint)Marshal.SizeOf<RawInputHeader>());
        uint size = 0;
        if (GetRawInputData(handle, RidInput, 0, ref size, headerSize) != 0 || size == 0)
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
            var input = Marshal.PtrToStructure<RawInput>(buffer);
            var device = GetDevice(input.Header.Device);
            if (!device.IsBlade)
            {
                return;
            }

            if (input.Header.Type == RimTypeHid)
            {
                ProcessRawHid(buffer, headerSize, size, device.Name);
                return;
            }
            if (input.Header.Type != RimTypeKeyboard)
            {
                return;
            }

            _observer?.Invoke(new RawKeyboardObservation(
                device.Name,
                input.Keyboard.MakeCode,
                input.Keyboard.Flags,
                input.Keyboard.VirtualKey));
            if (TryTranslate(
                input.Keyboard.MakeCode,
                input.Keyboard.Flags,
                _clock.Elapsed,
                out var keyEvent))
            {
                _events.Writer.TryWrite(keyEvent);
            }
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private void ProcessRawHid(nint buffer, uint headerSize, uint totalSize, string deviceName)
    {
        if (totalSize < headerSize + 8)
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
        var byteCount = checked(reportSize * reportCount);
        if (byteCount > totalSize - headerSize - 8)
        {
            return;
        }
        var reports = new byte[byteCount];
        Marshal.Copy(nint.Add(data, 8), reports, 0, reports.Length);
        for (var index = 0; index < reportCount; index++)
        {
            var report = reports.AsSpan(index * reportSize, reportSize);
            _hidObserver?.Invoke(new RawHidObservation(deviceName, report.ToArray()));
            if (report.Length > 0 && report[0] == 0x04)
            {
                var keyEvents = new List<QuickLightingKeyEvent>(1);
                TranslateRazerKeyReport(report, _clock.Elapsed, keyEvents);
                foreach (var keyEvent in keyEvents)
                {
                    _events.Writer.TryWrite(keyEvent);
                }
            }
        }
    }

    private (bool IsBlade, string Name) GetDevice(nint handle)
    {
        if (_devices.TryGetValue(handle, out var cached))
        {
            return cached;
        }

        uint characterCount = 0;
        if (GetRawInputDeviceInfo(handle, RidiDeviceName, null, ref characterCount) == uint.MaxValue ||
            characterCount == 0)
        {
            return _devices[handle] = (false, string.Empty);
        }
        var name = new StringBuilder(checked((int)characterCount));
        if (GetRawInputDeviceInfo(handle, RidiDeviceName, name, ref characterCount) == uint.MaxValue)
        {
            return _devices[handle] = (false, string.Empty);
        }
        var value = name.ToString();
        return _devices[handle] = (IsBladeKeyboardDevice(value), value);
    }

    private static void RegisterInputs(nint target, bool removing)
    {
        RawInputDevice[] devices =
        [
            new()
            {
                UsagePage = 0x01,
                Usage = 0,
                Flags = removing ? RidevRemove | RidevPageOnly : RidevInputSink | RidevPageOnly,
                Target = target,
            },
            new()
            {
                UsagePage = 0x0C,
                Usage = 0,
                Flags = removing ? RidevRemove | RidevPageOnly : RidevInputSink | RidevPageOnly,
                Target = target,
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

    private static IReadOnlyDictionary<(uint ScanCode, bool Extended), (int Row, int Column)> CreateScanCodeMap()
    {
        var map = new Dictionary<(uint, bool), (int, int)>
        {
            [(0x01, false)] = (0, 0),
            [(0x57, false)] = (0, 11), [(0x58, false)] = (0, 12),
            [(0x37, true)] = (0, 13), [(0x53, true)] = (0, 14),
            [(0x29, false)] = (1, 0), [(0x02, false)] = (1, 1), [(0x03, false)] = (1, 2), [(0x04, false)] = (1, 3),
            [(0x05, false)] = (1, 4), [(0x06, false)] = (1, 5), [(0x07, false)] = (1, 6), [(0x08, false)] = (1, 7),
            [(0x09, false)] = (1, 8), [(0x0A, false)] = (1, 9), [(0x0B, false)] = (1, 10), [(0x0C, false)] = (1, 11),
            [(0x0D, false)] = (1, 12), [(0x0E, false)] = (1, 14), [(0x49, true)] = (1, 15),
            [(0x0F, false)] = (2, 0), [(0x10, false)] = (2, 1), [(0x11, false)] = (2, 2), [(0x12, false)] = (2, 3),
            [(0x13, false)] = (2, 4), [(0x14, false)] = (2, 5), [(0x15, false)] = (2, 6), [(0x16, false)] = (2, 7),
            [(0x17, false)] = (2, 8), [(0x18, false)] = (2, 9), [(0x19, false)] = (2, 10), [(0x1A, false)] = (2, 11),
            [(0x1B, false)] = (2, 12), [(0x2B, false)] = (2, 14), [(0x51, true)] = (2, 15),
            [(0x3A, false)] = (3, 0), [(0x1E, false)] = (3, 1), [(0x1F, false)] = (3, 2), [(0x20, false)] = (3, 3),
            [(0x21, false)] = (3, 4), [(0x22, false)] = (3, 5), [(0x23, false)] = (3, 6), [(0x24, false)] = (3, 7),
            [(0x25, false)] = (3, 8), [(0x26, false)] = (3, 9), [(0x27, false)] = (3, 10), [(0x28, false)] = (3, 11),
            [(0x1C, false)] = (3, 14),
            [(0x2A, false)] = (4, 0), [(0x2C, false)] = (4, 2), [(0x2D, false)] = (4, 3), [(0x2E, false)] = (4, 4),
            [(0x2F, false)] = (4, 5), [(0x30, false)] = (4, 6), [(0x31, false)] = (4, 7), [(0x32, false)] = (4, 8),
            [(0x33, false)] = (4, 9), [(0x34, false)] = (4, 10), [(0x35, false)] = (4, 11), [(0x36, false)] = (4, 14),
            [(0x1D, false)] = (5, 0), [(0x5B, true)] = (5, 2), [(0x38, false)] = (5, 3), [(0x39, false)] = (5, 6),
            [(0x38, true)] = (5, 9), [(0x5D, true)] = (5, 11), [(0x1D, true)] = (5, 12),
            [(0x4B, true)] = (5, 13), [(0x48, true)] = (5, 14), [(0x4D, true)] = (5, 15), [(0x50, true)] = (6, 13),
        };
        for (uint scanCode = 0x3B; scanCode <= 0x44; scanCode++)
        {
            map[(scanCode, false)] = (0, checked((int)(scanCode - 0x3B + 1)));
        }
        return map;
    }

    private static TaskCompletionSource NewCompletion() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private delegate nint WindowProc(nint window, uint message, nuint wParam, nint lParam);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WindowClass
    {
        internal uint Size;
        internal uint Style;
        internal WindowProc WindowProc;
        internal int ClassExtra;
        internal int WindowExtra;
        internal nint Instance;
        internal nint Icon;
        internal nint Cursor;
        internal nint Background;
        internal string? MenuName;
        internal string ClassName;
        internal nint SmallIcon;
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
    private readonly struct RawInput
    {
        internal readonly RawInputHeader Header;
        internal readonly RawKeyboard Keyboard;
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

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern ushort RegisterClassEx(in WindowClass windowClass);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool UnregisterClass(string className, nint instance);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
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

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyWindow(nint window);

    [DllImport("user32.dll")]
    private static extern nint DefWindowProc(nint window, uint message, nuint wParam, nint lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterRawInputDevices(
        [In] RawInputDevice[] devices,
        uint deviceCount,
        uint structureSize);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetRawInputData(
        nint rawInput,
        uint command,
        nint data,
        ref uint size,
        uint headerSize);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint GetRawInputDeviceInfo(
        nint device,
        uint command,
        StringBuilder? data,
        ref uint size);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int GetMessage(out NativeMessage message, nint window, uint minimum, uint maximum);

    [DllImport("user32.dll")]
    private static extern bool TranslateMessage(in NativeMessage message);

    [DllImport("user32.dll")]
    private static extern nint DispatchMessage(in NativeMessage message);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool PostThreadMessage(uint threadId, uint message, nuint wParam, nint lParam);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern nint GetModuleHandle(string? moduleName);
}
