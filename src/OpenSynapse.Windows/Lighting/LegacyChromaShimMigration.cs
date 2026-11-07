using Microsoft.Win32;

namespace OpenSynapse.Windows.Lighting;

/// <summary>
/// Restores the official DLL left behind by the retired self-developed shim.
/// New installs never ship the shim; this only cleans up older beta installs.
/// </summary>
internal static class LegacyChromaShimMigration
{
    private const string FileName = "RzChromaSDK64.dll";
    private const string BackupName = FileName + ".opensynapse-backup";
    private const string SdkRegistryKey = @"SOFTWARE\WOW6432Node\Razer Chroma SDK";

    internal static bool TryRestore()
    {
        var directory = SdkBinDirectory;
        if (directory is null)
        {
            return false;
        }

        var target = Path.Combine(directory, FileName);
        var backup = Path.Combine(directory, BackupName);
        if (!File.Exists(backup))
        {
            return false;
        }

        var temporary = Path.Combine(directory, $"{FileName}.{Guid.NewGuid():N}.tmp");
        try
        {
            File.Copy(backup, temporary, overwrite: true);
            if (File.Exists(target))
            {
                File.Replace(temporary, target, destinationBackupFileName: null, ignoreMetadataErrors: true);
            }
            else
            {
                File.Move(temporary, target);
            }

            File.Delete(backup);
            return true;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return false;
        }
        finally
        {
            TryDelete(temporary);
        }
    }

    internal static void TryRemovePackagedShim()
    {
        var directory = Path.Combine(AppContext.BaseDirectory, "ChromaSdkShim");
        try
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or NotSupportedException)
        {
        }
    }

    private static string? SdkBinDirectory
    {
        get
        {
            try
            {
                using var key = Registry.LocalMachine.OpenSubKey(SdkRegistryKey);
                var install = key?.GetValue("InstallPath64") as string;
                if (string.IsNullOrWhiteSpace(install))
                {
                    install = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                        "Razer Chroma SDK");
                }

                var bin = Path.Combine(install, "bin");
                return Directory.Exists(bin) ? bin : null;
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException or System.Security.SecurityException)
            {
                return null;
            }
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }
}
