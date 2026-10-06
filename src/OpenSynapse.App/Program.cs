using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Velopack;
using Velopack.Locators;
using Velopack.Windows;

namespace OpenSynapse.App;

public static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        VelopackApp.Build()
            .SetAutoApplyOnStartup(false)
            .OnAfterUpdateFastCallback(_ => PreserveExistingShortcuts())
            .Run();

        // Velopack can restart the app with its parent directory as the working directory.
        // WinUI/.NET native probing must stay anchored to the deployed app directory.
        Environment.CurrentDirectory = AppContext.BaseDirectory;

        WinRT.ComWrappersSupport.InitializeComWrappers();
        Application.Start(_ =>
        {
            SynchronizationContext.SetSynchronizationContext(
                new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread()));
            new App();
        });
    }

#pragma warning disable CS0618
    private static void PreserveExistingShortcuts()
    {
        try
        {
            var locations = ShortcutLocation.Desktop | ShortcutLocation.StartMenuRoot;
            var shortcuts = new Shortcuts(VelopackLocator.Current);
            if (shortcuts.FindShortcuts("OpenSynapse.App.exe", locations).Count == 0)
            {
                return;
            }

            // updateOnly preserves a user's deleted desktop or Start-menu shortcut.
            shortcuts.CreateShortcut("OpenSynapse.App.exe", locations, updateOnly: true,
                programArguments: null, icon: null);
        }
        catch (Exception)
        {
            // Shortcut maintenance must never block an otherwise valid update.
        }
    }
#pragma warning restore CS0618
}
