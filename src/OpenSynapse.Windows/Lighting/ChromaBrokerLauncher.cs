using System.ComponentModel;
using System.Diagnostics;
using OpenSynapse.ChromaSdk.Protocol;
using OpenSynapse.Windows.Input;

namespace OpenSynapse.Windows.Lighting;

internal static class ChromaBrokerLauncher
{
    internal static string PipeName => ChromaBrokerProtocol.PipeName;
    internal static string StopEventName(int parentProcessId) =>
        ChromaBrokerProtocol.StopEventName(parentProcessId);
    internal static string ExecutablePath => Path.Combine(AppContext.BaseDirectory, "OpenSynapse.ChromaBroker.exe");
    internal static bool IsAvailable => File.Exists(ExecutablePath);
    internal static string OfficialDllPath => Path.Combine(Environment.SystemDirectory, "RzChromaSDK64.dll");
    internal static bool IsOfficialDllInstalled => File.Exists(OfficialDllPath);

    internal static Process? Start()
    {
        if (!IsAvailable) return null;
        var elevated = ProcessIntegrityLevel.IsElevated(ProcessIntegrityLevel.Current());
        var startInfo = new ProcessStartInfo
        {
            FileName = ExecutablePath,
            Arguments = $"--pipe {PipeName} --parent {Environment.ProcessId}",
            WorkingDirectory = AppContext.BaseDirectory,
            UseShellExecute = true,
            WindowStyle = ProcessWindowStyle.Hidden,
        };
        if (!elevated) startInfo.Verb = "runas";
        try
        {
            return Process.Start(startInfo);
        }
        catch (Win32Exception)
        {
            return null;
        }
    }

    internal static void Stop(Process? process)
    {
        if (process is null)
        {
            return;
        }

        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (Exception exception) when (
            exception is InvalidOperationException or Win32Exception or NotSupportedException)
        {
            // The elevated broker may outlive a lower-integrity app; its parent check exits it.
        }
        finally
        {
            process.Dispose();
        }
    }
}
