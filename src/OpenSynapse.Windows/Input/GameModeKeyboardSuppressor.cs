using System.ComponentModel;
using System.Runtime.InteropServices;

namespace OpenSynapse.Windows.Input;

/// <summary>
/// Suppresses the physical Windows keys while Blade game mode is on. The filter
/// driver can do this through its mapping table, but game mode must still work when
/// only the 91-byte feature channel is available.
/// </summary>
public sealed class GameModeKeyboardSuppressor : IDisposable
{
    private const int WhKeyboardLl = 13;
    private const int WmQuit = 0x0012;
    private const uint LlkhfInjected = 0x10;
    private const int VkLWin = 0x5B;
    private const int VkRWin = 0x5C;
    private const int HookStartTimeoutMilliseconds = 5_000;
    private const int HookStopTimeoutMilliseconds = 500;

    private readonly LowLevelKeyboardProc _keyboardProc;
    private readonly ManualResetEventSlim _ready = new(false);
    private readonly Thread _thread;
    private static readonly object Gate = new();
    private static GameModeKeyboardSuppressor? _current;
    private nint _hook;
    private Exception? _startupError;
    private uint _threadId;
    private int _disposed;

    public GameModeKeyboardSuppressor()
    {
        _keyboardProc = KeyboardHook;
        _thread = new Thread(ThreadMain)
        {
            IsBackground = true,
            Name = "OpenSynapse game mode keyboard hook",
        };
        _thread.Start();
        if (!_ready.Wait(HookStartTimeoutMilliseconds))
        {
            Dispose();
            throw new TimeoutException("The game mode keyboard hook did not start.");
        }
        if (_startupError is not null)
        {
            var error = _startupError;
            Dispose();
            throw new InvalidOperationException("The game mode keyboard hook could not start.", error);
        }
    }

    public static void SetEnabled(bool enabled)
    {
        lock (Gate)
        {
            if (!enabled)
            {
                _current?.Dispose();
                _current = null;
                return;
            }

            _current ??= new GameModeKeyboardSuppressor();
        }
    }

    internal static bool ShouldSuppress(int virtualKey, uint flags) =>
        (flags & LlkhfInjected) == 0 && virtualKey is VkLWin or VkRWin;

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        if (_threadId != 0)
        {
            PostThreadMessage(_threadId, WmQuit, 0, 0);
        }
        _thread.Join(HookStopTimeoutMilliseconds);
    }

    private void ThreadMain()
    {
        try
        {
            _threadId = GetCurrentThreadId();
            _hook = SetWindowsHookEx(WhKeyboardLl, _keyboardProc, GetModuleHandle(null), 0);
            if (_hook == 0)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(),
                    "Could not install the game mode keyboard hook.");
            }
            _ready.Set();
            while (GetMessage(out var message, 0, 0, 0) > 0)
            {
                TranslateMessage(ref message);
                DispatchMessage(ref message);
            }
        }
        catch (Exception exception)
        {
            _startupError = exception;
            _ready.Set();
        }
        finally
        {
            if (_hook != 0)
            {
                UnhookWindowsHookEx(_hook);
            }
        }
    }

    private nint KeyboardHook(int code, nint wParam, nint lParam)
    {
        if (code >= 0 && Volatile.Read(ref _disposed) == 0)
        {
            try
            {
                var data = Marshal.PtrToStructure<KbdLlHookStruct>(lParam);
                if (ShouldSuppress(checked((int)data.VirtualKey), data.Flags))
                {
                    return 1;
                }
            }
            catch
            {
                // A malformed hook payload must not break every keyboard event.
            }
        }

        return CallNextHookEx(0, code, wParam, lParam);
    }

    private delegate nint LowLevelKeyboardProc(int code, nint wParam, nint lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct KbdLlHookStruct
    {
        public uint VirtualKey;
        public uint ScanCode;
        public uint Flags;
        public uint Time;
        public nuint ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Msg
    {
        public nint Hwnd;
        public uint Message;
        public nint WParam;
        public nint LParam;
        public uint Time;
        public int PtX;
        public int PtY;
        public uint LPrivate;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern nint SetWindowsHookEx(int hookId, Delegate callback, nint module, uint threadId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(nint hook);

    [DllImport("user32.dll")]
    private static extern nint CallNextHookEx(nint hook, int code, nint wParam, nint lParam);

    [DllImport("user32.dll")]
    private static extern int GetMessage(out Msg message, nint window, uint messageFilterMin, uint messageFilterMax);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool TranslateMessage(ref Msg message);

    [DllImport("user32.dll")]
    private static extern nint DispatchMessage(ref Msg message);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostThreadMessage(uint threadId, uint message, nint wParam, nint lParam);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern nint GetModuleHandle(string? moduleName);
}
