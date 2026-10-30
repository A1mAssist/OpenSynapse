using OpenSynapse.Windows.Lifecycle;
using System.Runtime.InteropServices;

namespace OpenSynapse.App;

public partial class App
{
    private const uint MessageBoxInformation = 0x00000040;
    private const uint MessageBoxTopmost = 0x00040000;

    private static void ShowLegacyInstanceMessage() =>
        MessageBoxW(
            IntPtr.Zero,
            AppStrings.Text("LegacyInstanceRunningMessage"),
            "OpenSynapse",
            MessageBoxInformation | MessageBoxTopmost);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int MessageBoxW(IntPtr window, string text, string caption, uint type);

    private async Task ObserveActivationRequestsAsync(CancellationToken cancellationToken)
    {
        var guard = _singleInstanceGuard;
        if (guard is null)
        {
            return;
        }

        while (!cancellationToken.IsCancellationRequested)
        {
            var signal = await Task.Run(
                () => guard.WaitForSignal(cancellationToken),
                CancellationToken.None);
            switch (signal)
            {
                case SingleInstanceSignal.Activate:
                    _window?.RequestActivation();
                    break;
                case SingleInstanceSignal.Shutdown:
                    _window?.RequestExit();
                    return;
                default:
                    return;
            }
        }
    }
}
