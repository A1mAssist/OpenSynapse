using System.Globalization;
using System.Resources;
using System.Runtime.InteropServices;
using OpenSynapse.Core.Storage;

namespace OpenSynapse.Windows.Lifecycle;

/// <summary>
/// Offers to remove the user's local data during uninstall. The Velopack hook
/// runs before WinUI starts, so this uses a native message box and the Windows
/// display language instead of the app resource manager.
/// </summary>
internal static class UninstallUserDataPrompt
{
    private const uint MessageBoxYesNo = 0x00000004;
    private const uint MessageBoxIconWarning = 0x00000030;
    private const uint MessageBoxDefaultButton2 = 0x00000100;
    private const uint MessageBoxSetForeground = 0x00010000;
    private const uint MessageBoxTopMost = 0x00040000;
    private const int MessageBoxYes = 6;
    private static readonly ResourceManager Strings = new(
        "OpenSynapse.Windows.Lifecycle.UninstallStrings",
        typeof(UninstallUserDataPrompt).Assembly);

    // The uninstall hook is killed after 60 seconds. A timeout keeps an
    // unattended uninstall from blocking forever and defaults to keeping data.
    private const uint PromptTimeoutMilliseconds = 30_000;

    public static void Offer()
    {
        var root = AppDataPaths.Root;
        if (!Directory.Exists(root) || !Environment.UserInteractive)
        {
            return;
        }

        if (ConfirmDelete(root))
        {
            TryDelete(root);
        }
    }

    /// <summary>Deletes the data root only when it is an absolute path.</summary>
    internal static bool TryDelete(string root)
    {
        if (string.IsNullOrWhiteSpace(root) || !Path.IsPathFullyQualified(root))
        {
            return false;
        }

        try
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }

            return !Directory.Exists(root);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or System.Security.SecurityException or
                ArgumentException or NotSupportedException)
        {
            return false;
        }
    }

    private static bool ConfirmDelete(string root)
    {
        var culture = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "zh"
            ? CultureInfo.GetCultureInfo("zh-CN")
            : CultureInfo.GetCultureInfo("en-US");
        var (title, text) = ResolveText(root, culture);
        var flags = MessageBoxYesNo | MessageBoxIconWarning | MessageBoxDefaultButton2 |
            MessageBoxSetForeground | MessageBoxTopMost;
        return ShowMessageBox(text, title, flags) == MessageBoxYes;
    }

    internal static (string Title, string Message) ResolveText(string root, CultureInfo culture)
    {
        var title = Strings.GetString("Title", culture) ?? "Uninstall OpenSynapse";
        var message = string.Format(
            culture,
            Strings.GetString("Message", culture) ??
                "Delete locally saved settings?\n\nThis removes profiles, macros, Lighting Studio data, device caches and logs from:\n{0}",
            root);
        return (title, message);
    }

    private static int ShowMessageBox(string text, string title, uint flags)
    {
        try
        {
            return MessageBoxTimeout(IntPtr.Zero, text, title, flags, 0, PromptTimeoutMilliseconds);
        }
        catch (EntryPointNotFoundException)
        {
            return MessageBox(IntPtr.Zero, text, title, flags);
        }
    }

    [DllImport("user32.dll", EntryPoint = "MessageBoxTimeoutW", CharSet = CharSet.Unicode)]
    private static extern int MessageBoxTimeout(
        IntPtr hWnd, string text, string caption, uint type, uint languageId, uint timeout);

    [DllImport("user32.dll", EntryPoint = "MessageBoxW", CharSet = CharSet.Unicode)]
    private static extern int MessageBox(IntPtr hWnd, string text, string caption, uint type);
}
