using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Security;
using System.Security.Principal;
using System.Text;

namespace OpenSynapse.Windows.Lifecycle;

/// <summary>
/// Applies a downloaded Velopack package from a Task Scheduler job running as
/// LocalSystem, then starts the new app at normal integrity in the signed-in
/// user's session. The update task deliberately does not ask Update.exe to
/// restart the app into the updater's elevated token.
/// </summary>
internal static class OpenSynapseUpdateTaskInstaller
{
    internal const string UpdateTaskName = "OpenSynapseUpdate";
    internal const string RestartTaskName = "OpenSynapseUpdateRestart";

    internal static bool TryStart(
        string installRoot,
        string packageFileName,
        int waitProcessId,
        SecurityIdentifier? userSid,
        out string? error)
    {
        error = null;
        if (userSid is null || waitProcessId <= 0 ||
            string.IsNullOrWhiteSpace(packageFileName) ||
            !StringComparer.Ordinal.Equals(packageFileName, Path.GetFileName(packageFileName)) ||
            !packageFileName.EndsWith(".nupkg", StringComparison.OrdinalIgnoreCase))
        {
            error = "The update task request is invalid.";
            return false;
        }

        var root = Path.GetFullPath(installRoot);
        if (!OpenSynapseServiceInstaller.IsMachineInstallPath(root))
        {
            error = "The OpenSynapse install root is not protected.";
            return false;
        }

        var updateExe = Path.Combine(root, "Update.exe");
        var packages = Path.Combine(root, "packages");
        var package = Path.Combine(packages, packageFileName);
        var appLauncher = Path.Combine(root, "OpenSynapse.exe");
        if (!File.Exists(appLauncher))
        {
            appLauncher = Path.Combine(root, "current", "OpenSynapse.App.exe");
        }

        if (!File.Exists(updateExe) || !File.Exists(package) || !File.Exists(appLauncher))
        {
            error = "The update task files are missing.";
            return false;
        }

        if (!CreateTask(
                UpdateTaskName,
                BuildUpdateTaskXml(
                    updateExe,
                    BuildApplyArguments(package, root, packages, waitProcessId),
                    root),
                out error))
        {
            error = $"The update task could not be created: {error}";
            return false;
        }

        if (!CreateTask(
                RestartTaskName,
                BuildRestartTaskXml(appLauncher, root, userSid),
                out error))
        {
            error = $"The restart task could not be created: {error}";
            return false;
        }

        var run = RunSchTasks("/run", "/tn", UpdateTaskName);
        if (run.ExitCode != 0)
        {
            error = $"The update task could not be started: {run.Output}";
            return false;
        }

        return true;
    }

    internal static string BuildApplyArguments(
        string packagePath,
        string rootDirectory,
        string packagesDirectory,
        int waitProcessId) =>
        string.Join(' ',
            "apply",
            "--package", Quote(packagePath),
            "--waitPid", waitProcessId.ToString(CultureInfo.InvariantCulture),
            "--rootDir", Quote(rootDirectory),
            "--packageDir", Quote(packagesDirectory),
            "--norestart",
            "--silent");

    internal static string BuildUpdateTaskXml(
        string updateExePath,
        string arguments,
        string workingDirectory)
    {
        var schTasks = Path.Combine(Environment.SystemDirectory, "schtasks.exe");
        var commandPrompt = Path.Combine(Environment.SystemDirectory, "cmd.exe");
        var commandArguments = BuildConditionalRestartArguments(updateExePath, arguments, schTasks);
        var systemSid = new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null).Value;
        return $"""
            <?xml version="1.0" encoding="UTF-16"?>
            <Task version="1.2" xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">
              <RegistrationInfo>
                <Description>OpenSynapse update. Applies the downloaded package as LocalSystem, then starts the app at normal integrity.</Description>
              </RegistrationInfo>
              <Triggers />
              <Principals>
                <Principal id="Author">
                  <UserId>{Escape(systemSid)}</UserId>
                  <RunLevel>HighestAvailable</RunLevel>
                </Principal>
              </Principals>
              <Settings>
                <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>
                <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>
                <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>
                <AllowHardTerminate>true</AllowHardTerminate>
                <StartWhenAvailable>false</StartWhenAvailable>
                <RunOnlyIfIdle>false</RunOnlyIfIdle>
                <AllowStartOnDemand>true</AllowStartOnDemand>
                <Enabled>true</Enabled>
                <Hidden>true</Hidden>
                <ExecutionTimeLimit>PT0S</ExecutionTimeLimit>
                <Priority>5</Priority>
              </Settings>
              <Actions Context="Author">
                <Exec>
                  <Command>{Escape(commandPrompt)}</Command>
                  <Arguments>{Escape(commandArguments)}</Arguments>
                  <WorkingDirectory>{Escape(workingDirectory)}</WorkingDirectory>
                </Exec>
              </Actions>
            </Task>
            """;
    }

    internal static string BuildConditionalRestartArguments(
        string updateExePath,
        string applyArguments,
        string schTasksPath) =>
        $"/d /s /c \"\"{updateExePath}\" {applyArguments} && \"{schTasksPath}\" /run /tn {RestartTaskName}\"";

    internal static string BuildRestartTaskXml(
        string appPath,
        string workingDirectory,
        SecurityIdentifier userSid) =>
        $"""
            <?xml version="1.0" encoding="UTF-16"?>
            <Task version="1.2" xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">
              <RegistrationInfo>
                <Description>OpenSynapse restart after an update. Runs in the signed-in user's session without elevation.</Description>
              </RegistrationInfo>
              <Triggers />
              <Principals>
                <Principal id="Author">
                  <UserId>{Escape(userSid.Value)}</UserId>
                  <LogonType>InteractiveToken</LogonType>
                  <RunLevel>LeastPrivilege</RunLevel>
                </Principal>
              </Principals>
              <Settings>
                <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>
                <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>
                <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>
                <AllowHardTerminate>true</AllowHardTerminate>
                <StartWhenAvailable>false</StartWhenAvailable>
                <RunOnlyIfIdle>false</RunOnlyIfIdle>
                <AllowStartOnDemand>true</AllowStartOnDemand>
                <Enabled>true</Enabled>
                <Hidden>true</Hidden>
                <ExecutionTimeLimit>PT0S</ExecutionTimeLimit>
                <Priority>5</Priority>
              </Settings>
              <Actions Context="Author">
                <Exec>
                  <Command>{Escape(appPath)}</Command>
                  <WorkingDirectory>{Escape(workingDirectory)}</WorkingDirectory>
                </Exec>
              </Actions>
            </Task>
            """;

    private static bool CreateTask(string name, string xml, out string? error)
    {
        var xmlPath = Path.Combine(Path.GetTempPath(), $"OpenSynapseUpdate-{Guid.NewGuid():N}.xml");
        try
        {
            File.WriteAllText(xmlPath, xml, new UnicodeEncoding(bigEndian: false, byteOrderMark: true));
            var result = RunSchTasks("/create", "/tn", name, "/xml", xmlPath, "/f");
            error = result.ExitCode == 0 ? null : result.Output;
            return result.ExitCode == 0;
        }
        finally
        {
            try
            {
                File.Delete(xmlPath);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    private static SchTasksResult RunSchTasks(params string[] arguments)
    {
        var startInfo = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "schtasks.exe"))
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        try
        {
            using var process = Process.Start(startInfo)
                ?? throw new InvalidOperationException("schtasks.exe could not be started.");
            var output = process.StandardOutput.ReadToEnd();
            var error = process.StandardError.ReadToEnd();
            process.WaitForExit();
            return new SchTasksResult(process.ExitCode, $"{output}{error}".Trim());
        }
        catch (Exception exception) when (
            exception is Win32Exception or InvalidOperationException)
        {
            return new SchTasksResult(-1, exception.Message);
        }
    }

    private static string Escape(string value) => SecurityElement.Escape(value) ?? string.Empty;

    private static string Quote(string value) => '"' + value + '"';

    private readonly record struct SchTasksResult(int ExitCode, string Output);
}
