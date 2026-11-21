using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace OpenSynapse.Windows.Devices;

public sealed class WindowsTouchpadController
{
    private const string StatusKeyPath =
        @"SOFTWARE\Microsoft\Windows\CurrentVersion\PrecisionTouchPad\Status";
    private const string EnabledValueName = "Enabled";
    private const int DefaultReadbackAttempts = 10;
    private static readonly TimeSpan DefaultReadbackInterval = TimeSpan.FromMilliseconds(50);

    private readonly Func<object?> _readEnabledValue;
    private readonly Action<ushort, uint> _keybdEvent;
    private readonly Action<TimeSpan> _delay;
    private readonly int _readbackAttempts;
    private readonly TimeSpan _readbackInterval;

    public WindowsTouchpadController()
        : this(
            ReadEnabledValue,
            SendKey,
            Thread.Sleep,
            DefaultReadbackAttempts,
            DefaultReadbackInterval)
    {
    }

    internal WindowsTouchpadController(
        Func<object?> readEnabledValue,
        Action<ushort, uint> keybdEvent,
        Action<TimeSpan> delay,
        int readbackAttempts = DefaultReadbackAttempts,
        TimeSpan? readbackInterval = null)
    {
        ArgumentNullException.ThrowIfNull(readEnabledValue);
        ArgumentNullException.ThrowIfNull(keybdEvent);
        ArgumentNullException.ThrowIfNull(delay);
        ArgumentOutOfRangeException.ThrowIfLessThan(readbackAttempts, 1);

        _readEnabledValue = readEnabledValue;
        _keybdEvent = keybdEvent;
        _delay = delay;
        _readbackAttempts = readbackAttempts;
        _readbackInterval = readbackInterval ?? DefaultReadbackInterval;
    }

    public bool? GetEnabled()
    {
        try
        {
            return _readEnabledValue() is int value ? value != 0 : null;
        }
        catch
        {
            return null;
        }
    }

    public bool ToggleVerified()
    {
        var before = GetEnabled();
        if (before is null)
        {
            throw new InvalidOperationException("Could not read the Windows precision touchpad state; no toggle input was sent.");
        }

        SendToggleKeys();

        for (var attempt = 0; attempt < _readbackAttempts; attempt++)
        {
            var after = GetEnabled();
            if (after is not null && after != before)
            {
                return after.Value;
            }

            if (attempt + 1 < _readbackAttempts)
            {
                _delay(_readbackInterval);
            }
        }

        throw new InvalidOperationException(
            "Touchpad readback was unchanged or unavailable after toggling; " +
            "keybd_event does not return status, so success can only be determined by readback. " +
            $"Previous state was {(before.Value ? "enabled" : "disabled")}.");
    }

    private void SendToggleKeys()
    {
        const ushort virtualKeyF24 = 0x87;
        const ushort virtualKeyControl = 0x11;
        const ushort virtualKeyLeftWindows = 0x5B;
        const uint extended = 0x0001;
        const uint keyUp = 0x0002;

        // Mirrors the official SysUtilsNative.dll sequence of one keybd_event
        // call per event. The inter-key interval has not been measured, so no
        // delay is inserted between calls.
        _keybdEvent(virtualKeyF24, extended);
        _keybdEvent(virtualKeyControl, extended);
        _keybdEvent(virtualKeyLeftWindows, extended);
        _keybdEvent(virtualKeyF24, extended);
        _keybdEvent(virtualKeyF24, extended | keyUp);
        _keybdEvent(virtualKeyControl, extended | keyUp);
        _keybdEvent(virtualKeyLeftWindows, extended | keyUp);
        _keybdEvent(virtualKeyF24, extended | keyUp);
    }

    private static object? ReadEnabledValue()
    {
        using var key = Registry.CurrentUser.OpenSubKey(StatusKeyPath, writable: false);
        return key is not null && key.GetValueKind(EnabledValueName) == RegistryValueKind.DWord
            ? key.GetValue(EnabledValueName, null, RegistryValueOptions.DoNotExpandEnvironmentNames)
            : null;
    }

    private static void SendKey(ushort virtualKey, uint flags) =>
        keybd_event((byte)virtualKey, 0, flags, 0);

    [DllImport("user32.dll")]
    private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, nuint dwExtraInfo);
}
