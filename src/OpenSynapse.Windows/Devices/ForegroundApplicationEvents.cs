using System.Runtime.InteropServices;
using OpenSynapse.Core.Profiles;

namespace OpenSynapse.Windows.Devices;

public sealed class ForegroundApplicationEvents : IDisposable
{
    private readonly IActiveApplicationProvider _provider;
    private readonly Action _changed;
    private readonly NativeMethods.WinEventDelegate _callback;
    private IntPtr _hook;

    public ForegroundApplicationEvents(IActiveApplicationProvider provider, Action changed)
    {
        _provider = provider;
        _changed = changed;
        _callback = (_, _, _, _, _, _, _) => _changed();
    }

    public string? ExecutablePath => _provider.ExecutablePath;

    public void Start()
    {
        if (_hook != IntPtr.Zero) return;
        _hook = NativeMethods.SetWinEventHook(3, 3, IntPtr.Zero, _callback, 0, 0, 0);
    }

    public void Dispose()
    {
        if (_hook == IntPtr.Zero) return;
        NativeMethods.UnhookWinEvent(_hook);
        _hook = IntPtr.Zero;
    }

    private static class NativeMethods
    {
        internal delegate void WinEventDelegate(
            IntPtr hook, uint eventType, IntPtr window, int objectId, int childId,
            uint eventThread, uint eventTime);

        [DllImport("user32.dll")]
        internal static extern IntPtr SetWinEventHook(
            uint eventMin, uint eventMax, IntPtr module, WinEventDelegate callback,
            uint processId, uint threadId, uint flags);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool UnhookWinEvent(IntPtr hook);
    }
}
