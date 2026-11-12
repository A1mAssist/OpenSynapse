using System.ComponentModel;
using System.Runtime.InteropServices;

namespace OpenSynapse.Windows.Devices;

internal static class RazerFilterControlCompletion
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(2);

    internal static void Complete(
        WaitHandle completion,
        Func<bool> getResult,
        Action cancel,
        uint controlCode)
    {
        if (!completion.WaitOne(Timeout))
        {
            // Cancel only this write; the read loops share the same handle.
            cancel();
            completion.WaitOne();
            throw new TimeoutException(
                $"Razer filter IOCTL 0x{controlCode:X8} did not complete within {Timeout.TotalSeconds:0} s.");
        }

        if (!getResult())
        {
            throw new Win32Exception(
                Marshal.GetLastWin32Error(),
                $"Razer filter IOCTL 0x{controlCode:X8} failed.");
        }
    }
}
