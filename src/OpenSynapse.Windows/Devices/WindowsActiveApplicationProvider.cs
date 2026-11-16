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

            var handle = NativeMethods.OpenProcess(
                NativeMethods.ProcessQueryLimitedInformation,
                false,
                processId);
            if (handle == IntPtr.Zero)
            {
                return null;
            }

            try
            {
                var buffer = new char[1024];
                var length = (uint)buffer.Length;
                return NativeMethods.QueryFullProcessImageName(handle, 0, buffer, ref length)
                    ? new string(buffer, 0, (int)length)
                    : null;
            }
            finally
            {
                NativeMethods.CloseHandle(handle);
            }
        }
    }

    private static class NativeMethods
    {
        internal const uint ProcessQueryLimitedInformation = 0x1000;

        [DllImport("user32.dll", ExactSpelling = true)]
        internal static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll", ExactSpelling = true)]
        internal static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);

        [DllImport("kernel32.dll", SetLastError = true)]
        internal static extern IntPtr OpenProcess(uint access, bool inheritHandle, uint processId);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool QueryFullProcessImageName(
            IntPtr process, uint flags, [Out] char[] buffer, ref uint length);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool CloseHandle(IntPtr handle);
    }
}
