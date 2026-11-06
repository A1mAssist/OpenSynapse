using System.ComponentModel;
using System.Runtime.InteropServices;
using OpenSynapse.Core.Macros;
using OpenSynapse.Windows.Devices;
using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.Windows.Input;

/// <summary>
/// Sends macro keystrokes through RzCommon's SubmitInput IOCTL, the same
/// driver path Razer Synapse uses. Text, pointer and wheel steps stay on the
/// fallback sink because this IOCTL only carries keyboard frames.
/// </summary>
public sealed class RazerFilterMacroInputSink : IMacroInputSink, IAsyncDisposable
{
    private const uint MapVkToVscEx = 4;

    private readonly IMacroInputSink _fallback;
    private readonly Func<CancellationToken, ValueTask<IRazerFilterDriverChannel?>> _open;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Dictionary<(ushort ScanCode, bool Extended), MacroInput> _pressed = [];
    private IRazerFilterDriverChannel? _driver;
    private bool _driverUnavailable;
    private bool _disposed;

    public RazerFilterMacroInputSink(IMacroInputSink fallback)
        : this(fallback, OpenDriverAsync)
    {
    }

    internal RazerFilterMacroInputSink(
        IMacroInputSink fallback,
        Func<CancellationToken, ValueTask<IRazerFilterDriverChannel?>> open)
    {
        _fallback = fallback ?? throw new ArgumentNullException(nameof(fallback));
        _open = open ?? throw new ArgumentNullException(nameof(open));
    }

    public async Task SendAsync(MacroStep step, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(step);
        if (step.Kind == MacroStepKind.Delay)
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

    public Task SendMouseMoveAsync(
        MacroMouseMoveMode mode,
        int x,
        int y,
        CancellationToken cancellationToken = default) =>
        _fallback.SendMouseMoveAsync(mode, x, y, cancellationToken);

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
            DisposeDriverLocked();
        }
        finally
        {
            _gate.Release();
        }
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
            if (_disposed || _driverUnavailable)
            {
                return false;
            }

            var driver = await EnsureDriverAsync(cancellationToken).ConfigureAwait(false);
            if (driver is null)
            {
                return false;
            }

            try
            {
                driver.WriteControl(
                    RazerFilterInputProtocol.SubmitInput,
                    RazerFilterInputProtocol.CreateKeyboardInput(scanCode, isDown, extended));
            }
            catch (Exception exception) when (
                exception is IOException or Win32Exception or
                    InvalidOperationException or ObjectDisposedException)
            {
                // The endpoint vanished or the driver rejected the frame. Drop
                // the keys this sink still holds, then let the caller replay
                // the current event through the fallback path.
                _driverUnavailable = true;
                DisposeDriverLocked();
                await ReleasePressedThroughFallbackAsync(cancellationToken).ConfigureAwait(false);
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

    private async ValueTask<IRazerFilterDriverChannel?> EnsureDriverAsync(
        CancellationToken cancellationToken)
    {
        if (_driver is not null || _driverUnavailable)
        {
            return _driver;
        }

        try
        {
            _driver = await _open(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (
            exception is IOException or Win32Exception or UnauthorizedAccessException or
                InvalidOperationException or NotSupportedException)
        {
            // RazerFilterInputHost also treats a missing endpoint as a normal
            // condition. Macro playback must keep working without Razer's driver.
            _driverUnavailable = true;
        }

        if (_driver is null)
        {
            _driverUnavailable = true;
        }
        return _driver;
    }

    private void ReleasePressedLocked()
    {
        if (_driver is { } driver)
        {
            foreach (var key in _pressed.Keys)
            {
                try
                {
                    driver.WriteControl(
                        RazerFilterInputProtocol.SubmitInput,
                        RazerFilterInputProtocol.CreateKeyboardInput(
                            key.ScanCode,
                            isDown: false,
                            key.Extended));
                }
                catch (Exception exception) when (
                    exception is IOException or Win32Exception or
                        InvalidOperationException or ObjectDisposedException)
                {
                    DisposeDriverLocked();
                    break;
                }
            }
        }

        _pressed.Clear();
    }

    private async Task ReleasePressedThroughFallbackAsync(CancellationToken cancellationToken)
    {
        var pressed = _pressed.Values.ToArray();
        _pressed.Clear();
        foreach (var input in pressed)
        {
            try
            {
                await _fallback.SendAsync(
                    new MacroStep
                    {
                        Kind = MacroStepKind.Input,
                        Input = input,
                        IsDown = false,
                    },
                    cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                // Best effort. The original driver failure is the actionable one.
            }
        }
    }

    private void DisposeDriverLocked()
    {
        _driver?.Dispose();
        _driver = null;
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
        CancellationToken cancellationToken)
    {
        var path = await RazerFilterEndpointDiscovery.DiscoverAsync(
            RazerFilterEndpointDiscovery.Product710ProductId,
            cancellationToken).ConfigureAwait(false);
        if (path is not null)
        {
            return new WindowsRazerFilterDriverChannel(path);
        }

        foreach (var candidate in await RazerFilterEndpointDiscovery
                     .DiscoverAllAsync(cancellationToken).ConfigureAwait(false))
        {
            try
            {
                return new WindowsRazerFilterDriverChannel(candidate);
            }
            catch (Exception exception) when (
                exception is IOException or Win32Exception or UnauthorizedAccessException)
            {
                // Another client may hold the endpoint; try the next one.
            }
        }

        return null;
    }

    [DllImport("user32.dll")]
    private static extern uint MapVirtualKey(uint code, uint mapType);
}
