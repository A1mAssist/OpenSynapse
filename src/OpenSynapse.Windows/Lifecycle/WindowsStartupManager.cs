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

    internal static string FormatCommand(string executablePath, bool silent)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executablePath);
        var command = $"\"{Path.GetFullPath(executablePath.Trim())}\"";
        return silent ? $"{command} --silent" : command;
    }
}
