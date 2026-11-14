using System.Globalization;
using System.Resources;
using System.Runtime.InteropServices;
using OpenSynapse.Core.Storage;

namespace OpenSynapse.Windows.Lifecycle;

/// <summary>
/// Offers to remove the user's local data during uninstall. The Velopack hook
/// runs before WinUI starts, so this uses a native task dialog and the Windows
/// display language instead of the app resource manager.
/// </summary>
internal static class UninstallUserDataPrompt
{
    private const uint TaskDialogAllowCancellation = 0x0008;
    private const int ContinueButtonId = 100;
    private const uint WmClose = 0x0010;
    private const int TaskDialogCreated = 0;
    // The uninstall hook is killed after 60 seconds. A timeout keeps an
    // unattended uninstall from blocking forever and defaults to keeping data.
    private const uint PromptTimeoutMilliseconds = 30_000;
    private static readonly ResourceManager Strings = new(
        "OpenSynapse.Windows.Lifecycle.UninstallStrings",
        typeof(UninstallUserDataPrompt).Assembly);

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
        return ShowTaskDialog(ResolveText(root, culture));
    }

    internal static (string Title, string Message, string Verification, string Continue) ResolveText(
        string root,
        CultureInfo culture)
    {
        var title = Strings.GetString("Title", culture) ?? "Uninstall OpenSynapse";
        var message = string.Format(
            culture,
            Strings.GetString("Message", culture) ??
                "OpenSynapse will be uninstalled. Profiles, macros and Lighting Studio data are stored in:\n{0}\n\nSelect the option below only if you also want to delete this folder.",
            root);
        var verification = Strings.GetString("Verification", culture) ??
            "Also delete local settings and caches (cannot be undone)";
        var continueText = Strings.GetString("Continue", culture) ?? "Continue uninstall";
        return (title, message, verification, continueText);
    }

    internal static bool ShouldDelete(int selectedButton, bool verificationChecked) =>
        selectedButton == ContinueButtonId && verificationChecked;

    private static bool ShowTaskDialog(
        (string Title, string Message, string Verification, string Continue) text)
    {
        var title = Marshal.StringToHGlobalUni(text.Title);
        var message = Marshal.StringToHGlobalUni(text.Message);
        var verification = Marshal.StringToHGlobalUni(text.Verification);
        var continueText = Marshal.StringToHGlobalUni(text.Continue);
        var button = Marshal.AllocHGlobal(Marshal.SizeOf<TaskDialogButton>());
        Marshal.StructureToPtr(
            new TaskDialogButton { ButtonId = ContinueButtonId, Text = continueText },
            button,
            false);

        var state = new DialogState();
        TaskDialogCallback callback = (hwnd, notification, _, _, _) =>
        {
            if (notification == TaskDialogCreated)
            {
                state.Handle = hwnd;
                state.Timer = new System.Threading.Timer(
                    _ =>
                    {
                        var handle = state.Handle;
                        if (handle != IntPtr.Zero)
                        {
                            PostMessage(handle, WmClose, IntPtr.Zero, IntPtr.Zero);
                        }
                    },
                    null,
                    PromptTimeoutMilliseconds,
                    Timeout.Infinite);
            }
            return 0;
        };

        var config = new TaskDialogConfig
        {
            Size = (uint)Marshal.SizeOf<TaskDialogConfig>(),
            Flags = TaskDialogAllowCancellation,
            WindowTitle = title,
            Content = message,
            ButtonCount = 1,
            Buttons = button,
            DefaultButton = ContinueButtonId,
            VerificationText = verification,
            Callback = Marshal.GetFunctionPointerForDelegate(callback),
        };

        try
        {
            var result = TaskDialogIndirect(
                ref config,
                out var selectedButton,
                out _,
                out var deleteData);
            return result >= 0 && ShouldDelete(selectedButton, deleteData);
        }
        catch (Exception exception) when (
            exception is DllNotFoundException or EntryPointNotFoundException or MarshalDirectiveException)
        {
            // Failing to show the prompt must keep the user's data.
            return false;
        }
        finally
        {
            state.Timer?.Dispose();
            Marshal.FreeHGlobal(button);
            Marshal.FreeHGlobal(continueText);
            Marshal.FreeHGlobal(verification);
            Marshal.FreeHGlobal(message);
            Marshal.FreeHGlobal(title);
            GC.KeepAlive(callback);
        }
    }

    private sealed class DialogState
    {
        public IntPtr Handle;
        public System.Threading.Timer? Timer;
    }

    private delegate int TaskDialogCallback(
        IntPtr hwnd,
        uint notification,
        IntPtr wParam,
        IntPtr lParam,
        IntPtr refData);

    [StructLayout(LayoutKind.Sequential)]
    private struct TaskDialogConfig
    {
        public uint Size;
        public IntPtr Parent;
        public IntPtr Instance;
        public uint Flags;
        public uint CommonButtons;
        public IntPtr WindowTitle;
        public IntPtr MainIcon;
        public IntPtr MainInstruction;
        public IntPtr Content;
        public uint ButtonCount;
        public IntPtr Buttons;
        public int DefaultButton;
        public uint RadioButtonCount;
        public IntPtr RadioButtons;
        public int DefaultRadioButton;
        public IntPtr VerificationText;
        public IntPtr ExpandedInformation;
        public IntPtr ExpandedControlText;
        public IntPtr CollapsedControlText;
        public IntPtr FooterIcon;
        public IntPtr Footer;
        public IntPtr Callback;
        public IntPtr CallbackData;
        public uint Width;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct TaskDialogButton
    {
        public int ButtonId;
        public IntPtr Text;
    }

    [DllImport("comctl32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int TaskDialogIndirect(
        ref TaskDialogConfig config,
        out int button,
        out int radioButton,
        [MarshalAs(UnmanagedType.Bool)] out bool verificationChecked);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostMessage(
        IntPtr windowHandle,
        uint message,
        IntPtr wParam,
        IntPtr lParam);
}
