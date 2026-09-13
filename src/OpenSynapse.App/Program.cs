using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Velopack;

namespace OpenSynapse.App;

public static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        VelopackApp.Build()
            .SetAutoApplyOnStartup(false)
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
}
