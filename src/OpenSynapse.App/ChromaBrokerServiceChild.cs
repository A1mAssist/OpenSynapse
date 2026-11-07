using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Security.Principal;
using OpenSynapse.ChromaSdk.Protocol;

namespace OpenSynapse.App;

/// <summary>
/// Owns the Chroma broker process while the machine-wide service is running.
/// The broker inherits LocalSystem, so it can create the Global shared-memory
/// objects without a per-launch UAC prompt.
/// </summary>
internal static class ChromaBrokerServiceChild
{
    private static readonly object Gate = new();
    private static Process? _process;

    internal static bool IsRunning
    {
        get
        {
            lock (Gate)
            {
                return IsRunningLocked();
            }
        }
    }

    /// <summary>Returns null when the broker is running or was started.</summary>
    internal static string? Start(int parentProcessId, SecurityIdentifier? authorizedUser)
    {
        lock (Gate)
        {
            if (IsRunningLocked())
            {
                return null;
            }

            if (authorizedUser is null)
            {
                return "The service is missing its authorized user SID.";
            }

            if (!IsProcessAlive(parentProcessId))
            {
                return "The parent app process is not running.";
            }

            var executable = Path.Combine(
                AppContext.BaseDirectory,
                "OpenSynapse.ChromaBroker.exe");
            if (!File.Exists(executable))
            {
                return "OpenSynapse.ChromaBroker.exe is not packaged.";
            }

            var officialDll = Path.Combine(Environment.SystemDirectory, "RzChromaSDK64.dll");
            if (!File.Exists(officialDll))
            {
                return "The official RzChromaSDK64.dll is not installed.";
            }

            var startInfo = new ProcessStartInfo(executable)
            {
                WorkingDirectory = AppContext.BaseDirectory,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            startInfo.ArgumentList.Add("--pipe");
            startInfo.ArgumentList.Add(ChromaBrokerProtocol.PipeName);
            startInfo.ArgumentList.Add("--parent");
            startInfo.ArgumentList.Add(parentProcessId.ToString(CultureInfo.InvariantCulture));
            startInfo.ArgumentList.Add("--user");
            startInfo.ArgumentList.Add(authorizedUser.Value);

            try
            {
                _process = Process.Start(startInfo);
                return _process is null
                    ? "The Chroma broker process could not be started."
                    : null;
            }
            catch (Exception exception) when (
                exception is Win32Exception or InvalidOperationException or IOException)
            {
                return exception.Message;
            }
        }
    }

    internal static void Stop()
    {
        lock (Gate)
        {
            var process = _process;
            _process = null;
            if (process is null)
            {
                return;
            }

            try
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                    process.WaitForExit(2_000);
                }
            }
            catch (Exception exception) when (
                exception is InvalidOperationException or Win32Exception or NotSupportedException)
            {
            }
            finally
            {
                process.Dispose();
            }
        }
    }

    private static bool IsRunningLocked()
    {
        if (_process is null)
        {
            return false;
        }

        try
        {
            if (!_process.HasExited)
            {
                return true;
            }
        }
        catch (Exception exception) when (
            exception is InvalidOperationException or Win32Exception)
        {
        }

        _process.Dispose();
        _process = null;
        return false;
    }

    private static bool IsProcessAlive(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            return !process.HasExited;
        }
        catch (Exception exception) when (
            exception is ArgumentException or InvalidOperationException or Win32Exception)
        {
            return false;
        }
    }
}
