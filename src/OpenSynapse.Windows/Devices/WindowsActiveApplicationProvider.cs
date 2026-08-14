using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using OpenSynapse.Core.Profiles;

namespace OpenSynapse.Windows.Devices;

public sealed class WindowsActiveApplicationProvider : IActiveApplicationProvider
{
    public string? ExecutablePath
    {
        get
        {
            var window = NativeMethods.GetForegroundWindow();
            if (window == IntPtr.Zero)
            {
                return null;
            }

            NativeMethods.GetWindowThreadProcessId(window, out var processId);
            if (processId == 0)
            {
                return null;
            }

            try
            {
                using var process = Process.GetProcessById(checked((int)processId));
                return process.MainModule?.FileName;
            }
            catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or
                                               Win32Exception or NotSupportedException)
            {
                return null;
            }
        }
    }

    private static class NativeMethods
    {
        [DllImport("user32.dll", ExactSpelling = true)]
        internal static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll", ExactSpelling = true)]
        internal static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
    }
}
