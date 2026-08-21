using Microsoft.Win32;

namespace OpenSynapse.Windows.Lifecycle;

public static class WindowsGpuPreference
{
    private const string KeyPath = @"Software\Microsoft\DirectX\UserGpuPreferences";
    private const string MinimumPower = "GpuPreference=1;";

    public static bool EnsureMinimumPower(string executablePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executablePath);
        var fullPath = Path.GetFullPath(executablePath.Trim());
        using var key = Registry.CurrentUser.CreateSubKey(KeyPath, writable: true)
            ?? throw new InvalidOperationException("无法打开当前用户的 Windows 图形首选项。");
        var changed = false;
        foreach (var registeredPath in key.GetValueNames())
        {
            if (IsStaleOpenSynapsePath(registeredPath, fullPath))
            {
                key.DeleteValue(registeredPath, throwOnMissingValue: false);
                changed = true;
            }
        }
        if (key.GetValue(fullPath) is string)
        {
            return changed;
        }

        key.SetValue(fullPath, MinimumPower, RegistryValueKind.String);
        return true;
    }

    internal static bool IsStaleOpenSynapsePath(string registeredPath, string currentPath) =>
        StringComparer.OrdinalIgnoreCase.Equals(
            Path.GetFileName(registeredPath),
            "OpenSynapse.App.exe") &&
        !StringComparer.OrdinalIgnoreCase.Equals(registeredPath, currentPath);
}
