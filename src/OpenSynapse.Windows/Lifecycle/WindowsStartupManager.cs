using Microsoft.Win32;

namespace OpenSynapse.Windows.Lifecycle;

public sealed class WindowsStartupManager
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "OpenSynapse";

    public bool IsEnabled(string executablePath)
    {
        var expected = FormatCommand(executablePath);
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
        return key?.GetValue(ValueName) is string actual &&
               StringComparer.OrdinalIgnoreCase.Equals(actual, expected);
    }

    public void SetEnabled(bool enabled, string executablePath)
    {
        var command = FormatCommand(executablePath);
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

    private static string FormatCommand(string executablePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executablePath);
        return $"\"{Path.GetFullPath(executablePath.Trim())}\"";
    }
}
