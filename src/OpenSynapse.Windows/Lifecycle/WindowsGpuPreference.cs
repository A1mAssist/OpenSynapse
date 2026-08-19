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
        if (key.GetValue(fullPath) is string)
        {
            return false;
        }

        key.SetValue(fullPath, MinimumPower, RegistryValueKind.String);
        return true;
    }
}
