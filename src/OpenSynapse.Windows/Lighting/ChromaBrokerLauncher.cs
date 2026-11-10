using System.ComponentModel;
using System.Diagnostics;
using OpenSynapse.ChromaSdk.Protocol;

namespace OpenSynapse.Windows.Lighting;

internal static class ChromaBrokerLauncher
{
    internal static string PipeName => ChromaBrokerProtocol.PipeName;
    internal static string ExecutablePath => Path.Combine(AppContext.BaseDirectory, "OpenSynapse.ChromaBroker.exe");
    internal static bool IsAvailable => File.Exists(ExecutablePath);
    internal static string OfficialDllPath => Path.Combine(Environment.SystemDirectory, "RzChromaSDK64.dll");
    internal static bool IsOfficialDllInstalled => File.Exists(OfficialDllPath);

    /// <summary>
    /// Fallback for per-user and portable installs, or a machine install whose service
    /// could not be registered. The broker needs an elevated token to create the Global
    /// Chroma objects, so this asks once instead of silently leaving the SDK dead.
    /// </summary>
    internal static bool TryStartElevated(int parentProcessId, string? userSid, out string? error)
    {
        error = null;
        if (!IsAvailable)
        {
            error = "OpenSynapse.ChromaBroker.exe is not packaged.";
            return false;
        }
        if (!IsOfficialDllInstalled)
        {
            error = "The official RzChromaSDK64.dll is not installed.";
            return false;
        }
        if (parentProcessId <= 0 || string.IsNullOrWhiteSpace(userSid))
        {
            error = "The broker request is missing its parent process or user.";
            return false;
        }

        var startInfo = new ProcessStartInfo(ExecutablePath)
        {
            UseShellExecute = true,
            Verb = "runas",
            WindowStyle = ProcessWindowStyle.Hidden,
            WorkingDirectory = AppContext.BaseDirectory,
            Arguments = $"--pipe \"{PipeName}\" --parent {parentProcessId} --user \"{userSid}\"",
        };
        try
        {
            using var process = Process.Start(startInfo);
            if (process is null)
            {
                error = "The Chroma broker process could not be started.";
                return false;
            }
            return true;
        }
        catch (Win32Exception exception) when (exception.NativeErrorCode == 1223)
        {
            error = "Administrator approval was cancelled.";
            return false;
        }
        catch (Exception exception) when (exception is Win32Exception or InvalidOperationException)
        {
            error = exception.Message;
            return false;
        }
    }
}
