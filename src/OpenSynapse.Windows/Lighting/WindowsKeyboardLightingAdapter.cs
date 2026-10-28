using System.Diagnostics;
using System.Threading.Channels;
using OpenSynapse.Windows.Input;
using OpenSynapse.Windows.Protocols;

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
    private static readonly TimeSpan Debounce = TimeSpan.FromMilliseconds(20);
    private static readonly IReadOnlyDictionary<(uint ScanCode, bool Extended), (int Row, int Column)> ScanCodeMap =
        CreateScanCodeMap();
    private static readonly IReadOnlyDictionary<byte, (int Row, int Column)> RazerKeyMap =
        new Dictionary<byte, (int Row, int Column)>
        {
            [0x0A] = (1, 15),
            [0x0B] = (2, 15),
            [0x03] = (3, 15),
            [0xD3] = (4, 15),
            [0xD4] = (5, 15),
        };

    private readonly Channel<QuickLightingKeyEvent> _events = Channel.CreateBounded<QuickLightingKeyEvent>(
        new BoundedChannelOptions(64)
        {
            SingleReader = true,
            SingleWriter = false,
            FullMode = BoundedChannelFullMode.DropOldest,
        });
    private readonly Dictionary<(uint ScanCode, bool Extended), TimeSpan> _lastByKey = [];
    private readonly HashSet<byte> _pressedRazerKeys = [];
    private readonly Action<RawKeyboardObservation>? _observer;
    private readonly Action<RawHidObservation>? _hidObserver;
    private readonly Stopwatch _clock = new();
    private IDisposable? _subscription;
    private int _disposed;

    internal WindowsKeyboardLightingAdapter(
        Action<RawKeyboardObservation>? observer = null,
        Action<RawHidObservation>? hidObserver = null)
    {
        _observer = observer;
        _hidObserver = hidObserver;
    }

    public ValueTask StartAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        // The router owns the process's single keyboard Raw Input registration; a
        // second registration would take the stream away from the macro layer.
        _subscription ??= RawInputRouter.Subscribe(ProcessFrame);
        if (!_clock.IsRunning)
        {
            _clock.Restart();
        }

        return ValueTask.CompletedTask;
    }

    public ValueTask StopAsync()
    {
        _subscription?.Dispose();
        _subscription = null;
        return ValueTask.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
        {
            _subscription?.Dispose();
            _subscription = null;
            _events.Writer.TryComplete();
        }

        return ValueTask.CompletedTask;
    }

    internal void DrainTo(ICollection<QuickLightingKeyEvent> destination)
    {
        ArgumentNullException.ThrowIfNull(destination);
        while (_events.Reader.TryRead(out var item))
        {
            destination.Add(item);
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

    internal void ObserveMappingInput(BladeMappingInputEvent input)
    {
        if (!input.IsDown)
        {
            return;
        }

        (int Row, int Column) position;
        var found = false;
        if (input.Kind == BladeMappingInputKind.RazerKey && (uint)input.Code <= byte.MaxValue)
        {
            found = RazerKeyMap.TryGetValue((byte)input.Code, out position);
        }
        else if (input.Kind == BladeMappingInputKind.Keyboard && input.Code >= 0)
        {
            found = ScanCodeMap.TryGetValue(((uint)input.Code, input.Extended), out position);
        }
        else
        {
            position = default;
        }
        if (found && BladeLightingLayout.TryGetDevicePosition(position.Row, position.Column, out _, out _))
        {
            _events.Writer.TryWrite(new QuickLightingKeyEvent(position.Row, position.Column, _clock.Elapsed));
        }
    }

    internal static bool IsBladeKeyboardDevice(string deviceName) =>
        deviceName.Contains("VID_1532&PID_02C6", StringComparison.OrdinalIgnoreCase);

    private void ProcessFrame(RawInputFrame frame)
    {
        if (Volatile.Read(ref _disposed) != 0 || !IsBladeKeyboardDevice(frame.DevicePath))
        {
            return;
        }

        if (frame.IsHid)
        {
            if (frame.Report is not { } report)
            {
                return;
            }

            _hidObserver?.Invoke(new RawHidObservation(frame.DevicePath, report));
            if (report.Length > 0 && report[0] == 0x04)
            {
                var keyEvents = new List<QuickLightingKeyEvent>(1);
                TranslateRazerKeyReport(report, _clock.Elapsed, keyEvents);
                foreach (var keyEvent in keyEvents)
                {
                    _events.Writer.TryWrite(keyEvent);
                }
            }

            return;
        }

        _observer?.Invoke(new RawKeyboardObservation(
            frame.DevicePath,
            frame.MakeCode,
            frame.Flags,
            frame.VirtualKey));
        if (TryTranslate(frame.MakeCode, frame.Flags, _clock.Elapsed, out var translated))
        {
            _events.Writer.TryWrite(translated);
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
            [(0x5E, true)] = (0, 15),
            // Direction keys use the logical 7 x 16 canvas, not the Chroma 6 x 22 source columns.
            [(0x4B, true)] = (5, 12), [(0x48, true)] = (5, 13), [(0x4D, true)] = (5, 14), [(0x50, true)] = (6, 13),
        };
        for (uint scanCode = 0x3B; scanCode <= 0x44; scanCode++)
        {
            map[(scanCode, false)] = (0, checked((int)(scanCode - 0x3B + 1)));
        }
        return map;
    }
}