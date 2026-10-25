using Microsoft.Win32;

namespace OpenSynapse.Windows.Lifecycle;

public sealed class WindowsStartupManager
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "OpenSynapse";

    public bool IsEnabled(string executablePath)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
        return key?.GetValue(ValueName) is string actual &&
               (StringComparer.OrdinalIgnoreCase.Equals(actual, FormatCommand(executablePath, silent: false)) ||
                StringComparer.OrdinalIgnoreCase.Equals(actual, FormatCommand(executablePath, silent: true)));
    }

    public bool IsSilentEnabled(string executablePath)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
        return key?.GetValue(ValueName) is string actual &&
               StringComparer.OrdinalIgnoreCase.Equals(actual, FormatCommand(executablePath, silent: true));
    }

    public void SetEnabled(bool enabled, string executablePath, bool silent = false)
    {
        var command = FormatCommand(executablePath, silent);
        using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true)
            ?? throw new InvalidOperationException("Unable to open the current-user startup registry key.");
        if (enabled)
        {
            key.SetValue(ValueName, command, RegistryValueKind.String);
        }
        else
        {
            key.DeleteValue(ValueName, throwOnMissingValue: false);
        }
    }

    /// <summary>
    /// Points an existing OpenSynapse startup entry at the current executable
    /// when the registered per-user executable no longer exists.
    /// </summary>
    public static bool TryRepairStaleRegistration(string executablePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executablePath);
        var fullPath = Path.GetFullPath(executablePath.Trim());
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
        if (key?.GetValue(ValueName) is not string registration ||
            !TryParseRegistration(registration, out var registeredPath, out var silent) ||
            PathsEqual(registeredPath, fullPath) ||
            !IsOpenSynapseExecutable(registeredPath))
        {
            return false;
        }

        var legacyRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "OpenSynapse");
        if (!IsUnderDirectory(registeredPath, legacyRoot) && File.Exists(registeredPath))
        {
            return false;
        }

        key.SetValue(ValueName, FormatCommand(fullPath, silent), RegistryValueKind.String);
        return true;
    }

    internal static bool TryParseRegistration(
        string registration,
        out string executablePath,
        out bool silent)
    {
        executablePath = string.Empty;
        silent = false;
        var command = registration.Trim();
        if (command.Length == 0)
        {
            return false;
        }

        string remainder;
        if (command[0] == '"')
        {
            var endQuote = command.IndexOf('"', 1);
            if (endQuote <= 1)
            {
                return false;
            }

            executablePath = command[1..endQuote];
            remainder = command[(endQuote + 1)..].Trim();
        }
        else
        {
            var separator = command.IndexOf(' ');
            executablePath = separator < 0 ? command : command[..separator];
            remainder = separator < 0 ? string.Empty : command[(separator + 1)..].Trim();
        }

        if (remainder.Length == 0)
        {
            return true;
        }

        if (!StringComparer.OrdinalIgnoreCase.Equals(remainder, "--silent"))
        {
            executablePath = string.Empty;
            return false;
        }

        silent = true;
        return true;
    }

    private static bool IsOpenSynapseExecutable(string path)
    {
        var name = Path.GetFileName(path);
        return StringComparer.OrdinalIgnoreCase.Equals(name, "OpenSynapse.App.exe") ||
            StringComparer.OrdinalIgnoreCase.Equals(name, "OpenSynapse.exe");
    }

    private static bool PathsEqual(string left, string right)
    {
        try
        {
            return StringComparer.OrdinalIgnoreCase.Equals(
                Path.GetFullPath(left),
                Path.GetFullPath(right));
        }
        catch (Exception exception) when (
            exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }

    private static bool IsUnderDirectory(string path, string directory)
    {
        try
        {
            var fullPath = Path.GetFullPath(path);
            var prefix = Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory)) +
                Path.DirectorySeparatorChar;
            return fullPath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception exception) when (
            exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }

    internal static string FormatCommand(string executablePath, bool silent)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executablePath);
        var command = $"\"{Path.GetFullPath(executablePath.Trim())}\"";
        return silent ? $"{command} --silent" : command;
    }
}
