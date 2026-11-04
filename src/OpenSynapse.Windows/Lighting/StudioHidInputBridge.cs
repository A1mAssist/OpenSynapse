using System.Diagnostics;
using OpenSynapse.Core.Lighting;
using OpenSynapse.Windows.Devices;
using OpenSynapse.Windows.Input;

namespace OpenSynapse.Windows.Lighting;

/// <summary>
/// Maps Raw Input keyboard scan codes onto the manifest input table of active
/// Studio HID targets. Only the proven format is bridged: Chroma manifests
/// describe "kbd" inputs as [scan code, extended flag], which is exactly what
/// RawInputRouter reports for RIM_TYPEKEYBOARD. Standard mouse frames are not
/// delivered by the router, and Razer HID reports ("dkm"/0x04) have no proven
/// manifest encoding, so both stay outside this bridge.
/// </summary>
internal sealed class StudioHidInputBridge : IDisposable
{
    private const ushort KeyUpFlag = 0x01;
    private const string KeyboardInputType = "kbd";

    private readonly StudioHidOutputRouter _router;
    private readonly Func<double> _nowSeconds;
    private readonly HashSet<(string DevicePath, ushort ScanCode, ushort Flags)> _pressed = [];
    private readonly object _gate = new();
    private IDisposable? _subscription;
    private int _disposed;

    internal StudioHidInputBridge(StudioHidOutputRouter router, Func<double>? nowSeconds = null)
    {
        _router = router ?? throw new ArgumentNullException(nameof(router));
        var clock = Stopwatch.StartNew();
        _nowSeconds = nowSeconds ?? (() => clock.Elapsed.TotalSeconds);
    }

    /// <summary>Attaches to the process-wide Raw Input router; repeated calls are idempotent.</summary>
    internal void Attach()
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
            _subscription ??= RawInputRouter.Subscribe(frame => ProcessFrame(frame));
        }
    }

    /// <summary>
    /// Applies one frame. Returns the number of layer clocks the router touched.
    /// Key-down frames are deduplicated until the matching key-up; key-up frames
    /// are forwarded as <see cref="StudioTriggerInput.IsDown"/> false and ignored
    /// by the trigger.
    /// </summary>
    internal int ProcessFrame(RawInputFrame frame)
    {
        if (Volatile.Read(ref _disposed) != 0 || frame.IsHid ||
            string.IsNullOrWhiteSpace(frame.DevicePath))
        {
            return 0;
        }

        var flags = (ushort)(frame.Flags & ~KeyUpFlag);
        var isDown = (frame.Flags & KeyUpFlag) == 0;
        var key = (frame.DevicePath, frame.MakeCode, flags);
        if (isDown)
        {
            if (!_pressed.Add(key)) return 0;
        }
        else
        {
            _pressed.Remove(key);
        }

        var connection = FindConnection(frame.DevicePath);
        if (connection is null) return 0;

        var layout = _router.GetLayout(connection.InstanceId);
        if (layout is null) return 0;

        var position = FindInput(layout, frame.MakeCode, flags);
        if (position is null) return 0;

        return _router.HandleInput(
            connection.InstanceId,
            new StudioTriggerInput(position.Value.Row, position.Value.Column, isDown),
            _nowSeconds());
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        lock (_gate)
        {
            _subscription?.Dispose();
            _subscription = null;
        }
    }

    private RazerHidDeviceConnection? FindConnection(string devicePath)
    {
        var physicalKey = WindowsHidDiscovery.GetPhysicalDeviceKey(devicePath);
        foreach (var connection in _router.GetTargets())
        {
            if (connection.DevicePath is not { } targetPath ||
                string.IsNullOrWhiteSpace(targetPath) ||
                !string.Equals(
                    WindowsHidDiscovery.GetPhysicalDeviceKey(targetPath),
                    physicalKey,
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            return connection;
        }

        return null;
    }

    private static (int Row, int Column)? FindInput(
        StudioLedLayout layout,
        ushort scanCode,
        ushort flags)
    {
        // The code-only FindInput index collides on keys such as 29 and 56,
        // so the manifest's extended flag must be part of the match.
        (int Row, int Column)? match = null;
        foreach (var input in layout.Inputs)
        {
            if (!string.Equals(input.InputType, KeyboardInputType, StringComparison.OrdinalIgnoreCase) ||
                input.InputCode != scanCode ||
                input.InputModifier != flags)
            {
                continue;
            }

            if (match is { } existing &&
                (existing.Row != input.MatrixRow || existing.Column != input.MatrixCol))
            {
                return null;
            }

            match = (input.MatrixRow, input.MatrixCol);
        }

        return match;
    }
}
