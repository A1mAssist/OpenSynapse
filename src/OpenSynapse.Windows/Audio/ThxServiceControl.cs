using Microsoft.Win32;

namespace OpenSynapse.Windows.Audio;

internal enum ThxServiceHealth
{
    Missing,
    ImageMissing,
    Stopped,
    Running,
}

/// <summary>
/// Synapse uninstall removes VSSrv.exe but leaves the service registration
/// behind. Registration alone is not an install: check the ImagePath file.
/// </summary>
internal static class ThxServiceControl
{
    internal static ThxServiceHealth ReadServiceHealth(string serviceName)
    {
        string? imagePath;
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(
                $@"SYSTEM\CurrentControlSet\Services\{serviceName}");
            if (key is null)
            {
                return ThxServiceHealth.Missing;
            }
            imagePath = key.GetValue("ImagePath") as string;
        }
        catch (Exception exception) when (
            exception is UnauthorizedAccessException or System.Security.SecurityException or IOException)
        {
            return ThxAudioAdmission.HasRunningService(serviceName)
                ? ThxServiceHealth.Running
                : ThxServiceHealth.Stopped;
        }

        var executable = ResolveServiceImagePath(imagePath);
        if (executable is null || !File.Exists(executable))
        {
            return ThxServiceHealth.ImageMissing;
        }
        return ThxAudioAdmission.HasRunningService(serviceName)
            ? ThxServiceHealth.Running
            : ThxServiceHealth.Stopped;
    }

    internal static string? ResolveServiceImagePath(string? imagePath)
    {
        if (string.IsNullOrWhiteSpace(imagePath))
        {
            return null;
        }

        var expanded = Environment.ExpandEnvironmentVariables(imagePath.Trim());
        var end = expanded.StartsWith('"') ? expanded.IndexOf('"', 1) : -1;
        var path = end > 0 ? expanded[1..end] : expanded.Split(' ', 2)[0];
        const string systemRoot = @"\SystemRoot\";
        if (path.StartsWith(systemRoot, StringComparison.OrdinalIgnoreCase))
        {
            path = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.Windows),
                path[systemRoot.Length..]);
        }
        else if (!Path.IsPathRooted(path))
        {
            path = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.Windows),
                path);
        }
        return path;
    }
}
