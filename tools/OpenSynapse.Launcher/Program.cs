using System.Diagnostics;
using System.Runtime.InteropServices;

namespace OpenSynapse.Launcher;

internal static class Program
{
    private const uint ErrorIcon = 0x10;

    [STAThread]
    private static int Main(string[] args)
    {
        string resourcesDirectory = Path.Combine(AppContext.BaseDirectory, "resources");
        string applicationPath = Path.Combine(resourcesDirectory, "OpenSynapse.App.exe");

        if (!File.Exists(applicationPath))
        {
            ShowError("OpenSynapse files are incomplete. Please extract the entire archive before starting the app.");
            return 2;
        }

        try
        {
            ProcessStartInfo startInfo = new(applicationPath)
            {
                WorkingDirectory = resourcesDirectory,
                UseShellExecute = false,
            };

            foreach (string argument in args)
            {
                startInfo.ArgumentList.Add(argument);
            }

            Process.Start(startInfo);
            return 0;
        }
        catch (Exception exception)
        {
            ShowError($"OpenSynapse could not start.\n\n{exception.Message}");
            return 1;
        }
    }

    private static void ShowError(string message) =>
        MessageBoxW(0, message, "OpenSynapse", ErrorIcon);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int MessageBoxW(nint window, string text, string caption, uint type);
}
