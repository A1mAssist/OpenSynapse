using System.Runtime.InteropServices;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace OpenSynapse.App;

internal static class WindowAppearance
{
    private const int DwmUseImmersiveDarkMode = 20;

    internal static bool Apply(Window window)
    {
        var micaApplied = MicaController.IsSupported();
        if (micaApplied)
        {
            window.SystemBackdrop = new MicaBackdrop();
        }

        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000))
        {
            var enabled = 1;
            var windowHandle = WinRT.Interop.WindowNative.GetWindowHandle(window);
            _ = DwmSetWindowAttribute(
                windowHandle,
                DwmUseImmersiveDarkMode,
                ref enabled,
                sizeof(int));
        }

        return micaApplied;
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(
        nint windowHandle,
        int attribute,
        ref int value,
        int size);
}
