using OpenSynapse.Core.Storage;
using OpenSynapse.App.Updates;

namespace OpenSynapse.App;

internal static class AppUpdateSettings
{
    private static readonly TimeSpan AutomaticCheckInterval = TimeSpan.FromHours(6);
    private static readonly string SettingsPath = AppDataPaths.Combine("automatic-updates.txt");
    private static readonly string LastCheckPath = AppDataPaths.Combine("last-update-check.txt");
    private static readonly string SkippedVersionPath = AppDataPaths.Combine("skipped-update-version.txt");
    private static readonly string UpdateSourcePath = AppDataPaths.Combine("update-source.txt");

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

    /// <summary>
    /// Which release host the updater uses. Automatic is the default and is
    /// deliberately stored as a name, so older builds and hand-edited files
    /// fall back to the safe choice instead of parsing an unknown value.
    /// </summary>
    public static UpdateSourcePreference UpdateSource
    {
        get
        {
            try
            {
                if (!File.Exists(UpdateSourcePath))
                {
                    return UpdateSourcePreference.Automatic;
                }

                return File.ReadAllText(UpdateSourcePath).Trim().ToLowerInvariant() switch
                {
                    "gitee" => UpdateSourcePreference.Gitee,
                    "github" => UpdateSourcePreference.GitHub,
                    _ => UpdateSourcePreference.Automatic,
                };
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException or
                System.Security.SecurityException)
            {
                return UpdateSourcePreference.Automatic;
            }
        }
        set
        {
            Directory.CreateDirectory(Path.GetDirectoryName(UpdateSourcePath)!);
            File.WriteAllText(UpdateSourcePath, value switch
            {
                UpdateSourcePreference.Gitee => "gitee",
                UpdateSourcePreference.GitHub => "github",
                _ => "auto",
            });
        }
    }

    public static string? SkippedUpdateVersion
    {
        get
        {
            try
            {
                return File.Exists(SkippedVersionPath)
                    ? File.ReadAllText(SkippedVersionPath).Trim()
                    : null;
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException or
                System.Security.SecurityException)
            {
                return null;
            }
        }
    }

    public static void SkipUpdateVersion(string version)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SkippedVersionPath)!);
            File.WriteAllText(SkippedVersionPath, version);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or
            System.Security.SecurityException)
        {
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
