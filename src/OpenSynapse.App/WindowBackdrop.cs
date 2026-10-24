using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace OpenSynapse.App;

internal static class WindowBackdrop
{
    internal static void ApplyMicaIfSupported(Window window)
    {
        if (MicaController.IsSupported())
        {
            window.SystemBackdrop = new MicaBackdrop();
        }
    }
}
