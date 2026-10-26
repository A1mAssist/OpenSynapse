using OpenSynapse.Core.Storage;

namespace OpenSynapse.App;

internal static class AppUpdateSettings
{
    private static readonly TimeSpan AutomaticCheckInterval = TimeSpan.FromHours(6);
    private static readonly string SettingsPath = AppDataPaths.Combine("automatic-updates.txt");
    private static readonly string LastCheckPath = AppDataPaths.Combine("last-update-check.txt");

    public static bool AutomaticCheckDue
    {
        get
        {
            try
            {
                return !File.Exists(LastCheckPath) ||
                    DateTime.UtcNow - File.GetLastWriteTimeUtc(LastCheckPath) >= AutomaticCheckInterval;
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException or
                System.Security.SecurityException)
            {
                return true;
            }
        }
    }

    public static bool AutomaticUpdatesEnabled
    {
        get
        {
            try
            {
                return !File.Exists(SettingsPath) || File.ReadAllText(SettingsPath).Trim() != "0";
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException or
                System.Security.SecurityException)
            {
                return true;
            }
        }
        set
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
            File.WriteAllText(SettingsPath, value ? "1" : "0");
        }
    }

    public static void MarkCheckCompleted()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(LastCheckPath)!);
            File.WriteAllText(LastCheckPath, string.Empty);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or
            System.Security.SecurityException)
        {
        }
    }
}
