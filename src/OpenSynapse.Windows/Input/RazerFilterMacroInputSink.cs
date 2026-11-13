using System.ComponentModel;
using System.Globalization;
using System.Runtime.InteropServices;
using OpenSynapse.Core.Devices;
using OpenSynapse.Core.Macros;
using OpenSynapse.Windows.Devices;
using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.Windows.Input;

/// <summary>
/// Sends macro keyboard and mouse input through RzCommon's SubmitInput IOCTL,
/// the same driver path Razer Synapse uses. Text stays on the fallback sink.
/// </summary>
public sealed class RazerFilterMacroInputSink : IMacroInputSink, IAsyncDisposable
{
    private const uint MapVkToVscEx = 4;
    private const int MouseMaximumValue = 65_536;
    private const int MouseWheelStep = 120;
    private const int SmXVirtualScreen = 76;
    private const int SmYVirtualScreen = 77;
    private const int SmCxVirtualScreen = 78;
    private const int SmCyVirtualScreen = 79;

    private static readonly ushort[] MouseDownFlags = [0, 1, 4, 16, 64, 256];
    private static readonly ushort[] MouseUpFlags = [0, 2, 8, 32, 128, 512];

    private const long RetryMilliseconds = 5_000;

    private readonly IMacroInputSink _fallback;
    private readonly Func<bool, CancellationToken, ValueTask<IRazerFilterDriverChannel?>> _open;
    private readonly Action<string>? _log;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Dictionary<(ushort ScanCode, bool Extended), MacroInput> _pressed = [];
    private readonly HashSet<int> _pressedMouseButtons = [];
    private IRazerFilterDriverChannel? _driver;
    private IRazerFilterDriverChannel? _mouseDriver;
    private bool _driverUnavailable;
    private bool _mouseDriverUnavailable;
    private long _driverRetryAtTicks;
    private long _mouseDriverRetryAtTicks;
    private bool _disposed;

    public RazerFilterMacroInputSink(IMacroInputSink fallback, Action<string>? log = null)
        : this(fallback, (mouse, cancellationToken) => OpenDriverAsync(mouse, log, cancellationToken), log)
    {
    }

    internal RazerFilterMacroInputSink(
        IMacroInputSink fallback,
        Func<bool, CancellationToken, ValueTask<IRazerFilterDriverChannel?>> open,
        Action<string>? log = null)
    {
        _fallback = fallback ?? throw new ArgumentNullException(nameof(fallback));
        _open = open ?? throw new ArgumentNullException(nameof(open));
        _log = log;
    }

    public async Task SendAsync(MacroStep step, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(step);
        if (step.Kind == MacroStepKind.Delay)
        {
            return;
        }

        if (step.Input is { Kind: MacroInputKind.MouseButton } mouse &&
            await TrySendMouseButtonAsync(mouse, step.IsDown, cancellationToken).ConfigureAwait(false))
        {
            return;
        }

        if (step.Input is { Kind: MacroInputKind.MouseWheel } wheel &&
            await TrySendMouseWheelAsync(wheel, cancellationToken).ConfigureAwait(false))
        {
            return;
        }

        if (step.Input is { Kind: MacroInputKind.Keyboard } input &&
            await TrySendKeyboardAsync(input, step.IsDown, cancellationToken).ConfigureAwait(false))
        {
            return;
        }

        await _fallback.SendAsync(step, cancellationToken).ConfigureAwait(false);
    }

    public Task SendTextAsync(string text, CancellationToken cancellationToken = default) =>
        _fallback.SendTextAsync(text, cancellationToken);

    public async Task SendMouseMoveAsync(
        MacroMouseMoveMode mode,
        int x,
        int y,
        CancellationToken cancellationToken = default)
    {
        if (await TrySendMouseMoveAsync(mode, x, y, cancellationToken).ConfigureAwait(false))
        {
            return;
        }

        await _fallback.SendMouseMoveAsync(mode, x, y, cancellationToken).ConfigureAwait(false);
    }

    public async Task ReleaseAllAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ReleasePressedLocked();
        }
        finally
        {
            _gate.Release();
        }

        // Whatever the driver refused to release stays in _pressed, including the case
        // where it failed during this release pass and _driverUnavailable was never set.
        await ReleasePressedThroughFallbackAsync(CancellationToken.None).ConfigureAwait(false);
        await ReleaseMouseThroughFallbackAsync(CancellationToken.None).ConfigureAwait(false);
        await _fallback.ReleaseAllAsync(cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            ReleasePressedLocked();
            DisposeDriverLocked(mouse: false);
            DisposeDriverLocked(mouse: true);
        }
        finally
        {
            _gate.Release();
        }

        await ReleaseMouseThroughFallbackAsync(CancellationToken.None).ConfigureAwait(false);
    }

    private async Task<bool> TrySendKeyboardAsync(
        MacroInput input,
        bool isDown,
        CancellationToken cancellationToken)
    {
        if (!TryGetScanCode(input, out var scanCode, out var extended))
        {
            return false;
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!await TryWriteDriverLockedAsync(
                    RazerFilterInputProtocol.CreateKeyboardInput(scanCode, isDown, extended),
                    mouse: false,
                    cancellationToken).ConfigureAwait(false))
            {
                return false;
            }

            if (isDown)
            {
                _pressed[(scanCode, extended)] = input;
            }
            else
            {
                _pressed.Remove((scanCode, extended));
            }
            return true;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<bool> TrySendMouseButtonAsync(
        MacroInput input,
        bool isDown,
        CancellationToken cancellationToken)
    {
        var button = input.MouseButton;
        if (button is < 1 or > 5)
        {
            return false;
        }

        var flags = isDown ? MouseDownFlags[button] : MouseUpFlags[button];
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!await TryWriteDriverLockedAsync(
                    RazerFilterInputProtocol.CreateMouseInput(0, flags, 0, 0),
                    mouse: true,
                    cancellationToken).ConfigureAwait(false))
            {
                return false;
            }

            if (isDown)
            {
                _pressedMouseButtons.Add(button);
            }
            else
            {
                _pressedMouseButtons.Remove(button);
            }
            return true;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<bool> TrySendMouseWheelAsync(
        MacroInput input,
        CancellationToken cancellationToken)
    {
        if (input.WheelHorizontal || input.WheelDelta == 0)
        {
            return false;
        }

        // shortcut: one frame per 120 units, capped at 20 frames; larger
        // deltas are truncated rather than producing an unbounded burst.
        var steps = Math.Clamp(
            Math.Abs(input.WheelDelta) / MouseWheelStep,
            1,
            20);
        var wheel = input.WheelDelta > 0 ? MouseWheelStep : -MouseWheelStep;
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            for (var index = 0; index < steps; index++)
            {
                if (await TryWriteDriverLockedAsync(
                        RazerFilterInputProtocol.CreateMouseInput(0, 0x0400, 0, 0, wheel),
                        mouse: true,
                        cancellationToken).ConfigureAwait(false))
                {
                    continue;
                }

                if (index == 0)
                {
                    return false;
                }

                // The driver already delivered some notches. Send only the
                // rest through the fallback so a mid-scroll failure does not
                // duplicate the whole wheel event.
                await _fallback.SendAsync(
                    new MacroStep
                    {
                        Kind = MacroStepKind.Input,
                        Input = new MacroInput
                        {
                            Kind = MacroInputKind.MouseWheel,
                            WheelDelta = (input.WheelDelta > 0 ? 1 : -1) *
                                (steps - index) * MouseWheelStep,
                        },
                    },
                    cancellationToken).ConfigureAwait(false);
                return true;
            }

            return true;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<bool> TrySendMouseMoveAsync(
        MacroMouseMoveMode mode,
        int x,
        int y,
        CancellationToken cancellationToken)
    {
        int maximumValue;
        int moveX;
        int moveY;
        switch (mode)
        {
            case MacroMouseMoveMode.Absolute:
                if (!TryNormalizeAbsolutePoint(x, y, out moveX, out moveY))
                {
                    return false;
                }
                maximumValue = MouseMaximumValue;
                break;

            case MacroMouseMoveMode.Relative:
                maximumValue = 0;
                moveX = x;
                moveY = y;
                break;

            case MacroMouseMoveMode.ForegroundRelative:
                if (!WindowsMacroInputSink.TryResolveForegroundPoint(
                        x,
                        y,
                        out var screenX,
                        out var screenY) ||
                    !TryNormalizeAbsolutePoint(screenX, screenY, out moveX, out moveY))
                {
                    return false;
                }
                maximumValue = MouseMaximumValue;
                break;

            default:
                return false;
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await TryWriteDriverLockedAsync(
                RazerFilterInputProtocol.CreateMouseInput(
                    maximumValue,
                    0,
                    moveX,
                    moveY),
                mouse: true,
                cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<bool> TryWriteDriverLockedAsync(
        byte[] payload,
        bool mouse,
        CancellationToken cancellationToken)
    {
        if (_disposed || (mouse ? _mouseDriverUnavailable : _driverUnavailable))
        {
            return false;
        }

        var driver = await EnsureDriverAsync(mouse, cancellationToken).ConfigureAwait(false);
        if (driver is null)
        {
            return false;
        }

        try
        {
            driver.WriteControl(RazerFilterInputProtocol.SubmitInput, payload);
            return true;
        }
        catch (Exception exception) when (
            exception is IOException or Win32Exception or
                InvalidOperationException or ObjectDisposedException)
        {
            // The endpoint vanished or the driver rejected the frame. Drop
            // the keys this sink still holds, then let the caller replay the
            // current event through the fallback path.
            if (mouse)
            {
                _mouseDriverUnavailable = true;
                DisposeDriverLocked(mouse: true);
                await ReleaseMouseThroughFallbackAsync(CancellationToken.None).ConfigureAwait(false);
            }
            else
            {
                _driverUnavailable = true;
                DisposeDriverLocked(mouse: false);
                await ReleasePressedThroughFallbackAsync(CancellationToken.None).ConfigureAwait(false);
            }
            return false;
        }
    }

    private static bool TryNormalizeAbsolutePoint(
        int x,
        int y,
        out int normalizedX,
        out int normalizedY)
    {
        normalizedX = 0;
        normalizedY = 0;
        var left = GetSystemMetrics(SmXVirtualScreen);
        var top = GetSystemMetrics(SmYVirtualScreen);
        var width = GetSystemMetrics(SmCxVirtualScreen);
        var height = GetSystemMetrics(SmCyVirtualScreen);
        if (width <= 1 || height <= 1)
        {
            return false;
        }

        var clampedX = Math.Clamp(x, left, left + width - 1);
        var clampedY = Math.Clamp(y, top, top + height - 1);
        normalizedX = 1 + (int)((long)(clampedX - left) * 65_535 / (width - 1));
        normalizedY = 1 + (int)((long)(clampedY - top) * 65_535 / (height - 1));
        return true;
    }

    private async ValueTask<IRazerFilterDriverChannel?> EnsureDriverAsync(
        bool mouse,
        CancellationToken cancellationToken)
    {
        var now = Environment.TickCount64;
        if (mouse)
        {
            if (_mouseDriver is not null || _mouseDriverUnavailable)
            {
                return _mouseDriver;
            }

            if (now < _mouseDriverRetryAtTicks)
            {
                return null;
            }
        }
        else
        {
            if (_driver is not null || _driverUnavailable)
            {
                return _driver;
            }

            if (now < _driverRetryAtTicks)
            {
                return null;
            }
        }

        try
        {
            var driver = await _open(mouse, cancellationToken).ConfigureAwait(false);
            if (mouse)
            {
                _mouseDriver = driver;
            }
            else
            {
                _driver = driver;
            }
        }
        catch (Exception exception) when (
            exception is IOException or Win32Exception or UnauthorizedAccessException or
                InvalidOperationException or NotSupportedException)
        {
            // RazerFilterInputHost also treats a missing endpoint as a normal
            // condition. Macro playback must keep working without Razer's driver.
            if (mouse)
            {
                _mouseDriverUnavailable = true;
            }
            else
            {
                _driverUnavailable = true;
            }
        }

        // No suitable endpoint is a normal state, not a permanent failure: the user
        // may install or repair the driver while the app is running, so retry later
        // instead of falling back to SendInput until the next restart.
        if (mouse)
        {
            if (_mouseDriver is null)
            {
                _mouseDriverRetryAtTicks = Environment.TickCount64 + RetryMilliseconds;
            }
            return _mouseDriver;
        }

        if (_driver is null)
        {
            _driverRetryAtTicks = Environment.TickCount64 + RetryMilliseconds;
        }
        return _driver;
    }

    private void ReleasePressedLocked()
    {
        if (_driver is { } driver)
        {
            foreach (var key in _pressed.Keys.ToArray())
            {
                try
                {
                    driver.WriteControl(
                        RazerFilterInputProtocol.SubmitInput,
                        RazerFilterInputProtocol.CreateKeyboardInput(
                            key.ScanCode,
                            isDown: false,
                            key.Extended));
                    _pressed.Remove(key);
                }
                catch (Exception exception) when (
                    exception is IOException or Win32Exception or
                        InvalidOperationException or ObjectDisposedException)
                {
                    DisposeDriverLocked(mouse: false);
                    break;
                }
            }
        }

        if (_mouseDriver is { } mouseDriver)
        {
            foreach (var button in _pressedMouseButtons.ToArray())
            {
                try
                {
                    mouseDriver.WriteControl(
                        RazerFilterInputProtocol.SubmitInput,
                        RazerFilterInputProtocol.CreateMouseInput(
                            0,
                            MouseUpFlags[button],
                            0,
                            0));
                    _pressedMouseButtons.Remove(button);
                }
                catch (Exception exception) when (
                    exception is IOException or Win32Exception or
                        InvalidOperationException or ObjectDisposedException)
                {
                    DisposeDriverLocked(mouse: true);
                    break;
                }
            }
        }
    }

    private async Task ReleasePressedThroughFallbackAsync(CancellationToken cancellationToken)
    {
        var pressed = _pressed.Values.ToArray();
        foreach (var input in pressed)
        {
            await _fallback.SendAsync(
                new MacroStep
                {
                    Kind = MacroStepKind.Input,
                    Input = input,
                    IsDown = false,
                },
                cancellationToken).ConfigureAwait(false);
            if (TryGetScanCode(input, out var scanCode, out var extended))
            {
                _pressed.Remove((scanCode, extended));
            }
        }
    }

    private async Task ReleaseMouseThroughFallbackAsync(CancellationToken cancellationToken)
    {
        var buttons = _pressedMouseButtons.ToArray();
        foreach (var button in buttons)
        {
            await _fallback.SendAsync(
                new MacroStep
                {
                    Kind = MacroStepKind.Input,
                    Input = new MacroInput
                    {
                        Kind = MacroInputKind.MouseButton,
                        MouseButton = button,
                    },
                    IsDown = false,
                },
                cancellationToken).ConfigureAwait(false);
            _pressedMouseButtons.Remove(button);
        }
    }

    private void DisposeDriverLocked(bool mouse)
    {
        if (mouse)
        {
            _mouseDriver?.Dispose();
            _mouseDriver = null;
        }
        else
        {
            _driver?.Dispose();
            _driver = null;
        }
    }

    private static bool TryGetScanCode(
        MacroInput input,
        out ushort scanCode,
        out bool extended)
    {
        extended = input.Extended;
        if (input.ScanCode > 0)
        {
            scanCode = (ushort)(input.ScanCode & 0xFF);
            extended |= input.ScanCode > 0xFF;
            return true;
        }

        scanCode = 0;
        if (input.VirtualKey is <= 0 or > 0xFF)
        {
            return false;
        }

        var mapped = MapVirtualKey((uint)input.VirtualKey, MapVkToVscEx);
        if (mapped == 0)
        {
            return false;
        }

        scanCode = (ushort)(mapped & 0xFF);
        extended |= (mapped & 0xFF00) == 0xE000;
        return scanCode != 0;
    }

    private static async ValueTask<IRazerFilterDriverChannel?> OpenDriverAsync(
        bool mouse,
        Action<string>? log,
        CancellationToken cancellationToken)
    {
        if (mouse)
        {
            // A mouse event must go to a mouse's own RZCONTROL endpoint. The
            // Blade endpoint accepts the frame but does not inject a mouse.
            foreach (var candidate in await RazerFilterEndpointDiscovery
                         .DiscoverAllAsync(cancellationToken).ConfigureAwait(false))
            {
                if (!IsMouseEndpoint(candidate))
                {
                    continue;
                }

                try
                {
                    log?.Invoke($"macro mouse endpoint={candidate}");
                    return new WindowsRazerFilterDriverChannel(candidate);
                }
                catch (Exception exception) when (
                    exception is IOException or Win32Exception or UnauthorizedAccessException)
                {
                    // Another client may hold the endpoint; try the next one.
                    log?.Invoke($"macro mouse endpoint busy={candidate}: {exception.Message}");
                }
            }

            log?.Invoke("macro mouse endpoint=none (sendinput)");
            return null;
        }

        var path = await RazerFilterEndpointDiscovery.DiscoverAsync(
            RazerFilterEndpointDiscovery.Product710ProductId,
            cancellationToken).ConfigureAwait(false);
        if (path is not null)
        {
            log?.Invoke($"macro keyboard endpoint={path}");
            return new WindowsRazerFilterDriverChannel(path);
        }

        // RzCommon injects into whichever device owns the endpoint, so a keyboard
        // frame only works on a keyboard endpoint whose keyboard collections carry
        // the filter. A mouse endpoint accepts the frame and injects nothing, and a
        // silent success here used to suppress the SendInput fallback.
        foreach (var candidate in await RazerFilterEndpointDiscovery
                     .DiscoverAllAsync(cancellationToken).ConfigureAwait(false))
        {
            if (!IsKeyboardEndpoint(candidate) ||
                !TryReadEndpointProductId(candidate, out var productId))
            {
                log?.Invoke($"macro keyboard endpoint skipped={candidate} reason=not-keyboard");
                continue;
            }

            if (!RazerFilterInfBinder.InspectAttachment([productId]).IsAttached)
            {
                log?.Invoke($"macro keyboard endpoint skipped={candidate} reason=filter-missing");
                continue;
            }

            try
            {
                log?.Invoke($"macro keyboard endpoint={candidate}");
                return new WindowsRazerFilterDriverChannel(candidate);
            }
            catch (Exception exception) when (
                exception is IOException or Win32Exception or UnauthorizedAccessException)
            {
                // Another client may hold the endpoint; try the next one.
                log?.Invoke($"macro keyboard endpoint busy={candidate}: {exception.Message}");
            }
        }

        log?.Invoke("macro keyboard endpoint=none (sendinput)");
        return null;
    }

    private static bool IsMouseEndpoint(string path) =>
        TryReadEndpointProductId(path, out var productId) &&
        CategoryOf(productId) == DeviceCategory.Mouse;

    internal static bool IsKeyboardEndpoint(string path) =>
        TryReadEndpointProductId(path, out var productId) &&
        CategoryOf(productId) == DeviceCategory.Keyboard;

    private static DeviceCategory? CategoryOf(ushort productId) =>
        RazerHidDeviceCatalog.BuiltIn.Find(0x1532, productId)?.Category
        ?? RazerDeviceRegistry.BuiltIn.Find(0x1532, productId)?.Category;

    private static bool TryReadEndpointProductId(string path, out ushort productId)
    {
        productId = 0;
        var index = path.IndexOf("PID_", StringComparison.OrdinalIgnoreCase);
        return index >= 0 &&
            index + 8 <= path.Length &&
            ushort.TryParse(
                path.AsSpan(index + 4, 4),
                NumberStyles.HexNumber,
                CultureInfo.InvariantCulture,
                out productId);
    }

    [DllImport("user32.dll")]
    private static extern uint MapVirtualKey(uint code, uint mapType);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int index);
}
