using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using OpenSynapse.Windows.Devices;
using OpenSynapse.Windows.Input;
using OpenSynapse.Windows.Lifecycle;
using OpenSynapse.Windows.Lighting;
using System.Security.Principal;
using Velopack;
using Velopack.Locators;
using Velopack.Windows;

namespace OpenSynapse.App;

public static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        var installServiceIndex = Array.FindIndex(
            args,
            argument => argument.Equals("--install-service", StringComparison.OrdinalIgnoreCase));
        if (installServiceIndex >= 0)
        {
            var userSid = args.Length > installServiceIndex + 1 &&
                !args[installServiceIndex + 1].StartsWith("--", StringComparison.Ordinal)
                    ? args[installServiceIndex + 1]
                    : null;
            var ok = OpenSynapseServiceInstaller.EnsureInstalled(Environment.ProcessPath, userSid);
            Console.WriteLine(ok
                ? "OpenSynapseService registered."
                : !OpenSynapseServiceInstaller.IsMachineInstallPath(Environment.ProcessPath)
                    ? "OpenSynapseService registration failed: install under Program Files first."
                    : "OpenSynapseService registration failed: run the command as administrator.");
            Environment.Exit(ok ? 0 : 1);
        }

        if (args.Contains("--probe-dkm", StringComparer.OrdinalIgnoreCase))
        {
            Environment.Exit(ProbeDkmTable());
        }

        if (OpenSynapseServiceHost.TryRun(args))
        {
            return;
        }

        if (ElevatedMacroAgent.TryRun(args))
        {
            return;
        }

        if (WindowsDedicatedGpuMode.TryRunElevatedCommand(args) is int helperExitCode)
        {
            Environment.Exit(helperExitCode);
            return;
        }

        VelopackApp.Build()
            .SetAutoApplyOnStartup(false)
            .OnAfterInstallFastCallback(_ =>
            {
                MigrateLegacyInstall();
                RepairStartupRegistration();
                EnsureOpenSynapseService();
                EnsureMacroAgentTask();
                MigrateChromaShim();
                PreserveExistingShortcuts();
            })
            .OnAfterUpdateFastCallback(_ =>
            {
                MigrateChromaShim();
                // The machine-wide updater runs this hook as LocalSystem. Re-running the
                // per-user setup would rewrite the service and tasks with SYSTEM's SID.
                if (!IsRunningAsSystem())
                {
                    EnsureOpenSynapseService();
                    EnsureMacroAgentTask();
                    RepairStartupRegistration();
                    PreserveExistingShortcuts();
                }
            })
            .OnBeforeUpdateFastCallback(_ => StopOpenSynapseService())
            .OnBeforeUninstallFastCallback(_ =>
            {
                RemoveOpenSynapseService();
                RemoveMacroAgentTask();
                RemoveStartupRegistration();
                RemoveShortcuts();
                MigrateChromaShim();
                OfferUserDataCleanup();
            })
            .Run();

        if (ProcessIntegrityLevel.IsElevated(ProcessIntegrityLevel.Current()) &&
            OpenSynapseServiceInstaller.IsMachineInstallPath(Environment.ProcessPath))
        {
            EnsureOpenSynapseService();
            MigrateChromaShim();
        }

        MigrateLegacyUserData();

        // Velopack can restart the app with its parent directory as the working directory.
        // WinUI/.NET native probing must stay anchored to the deployed app directory.
        Environment.CurrentDirectory = AppContext.BaseDirectory;

        WinRT.ComWrappersSupport.InitializeComWrappers();
        Application.Start(_ =>
        {
            SynchronizationContext.SetSynchronizationContext(
                new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread()));
            new App();
        });
    }

    private static int ProbeDkmTable()
    {
        var productId = RazerFilterEndpointDiscovery.Product710ProductId;
        var path = RazerFilterEndpointDiscovery.DiscoverAsync(productId)
            .GetAwaiter()
            .GetResult();
        if (path is null)
        {
            Console.Error.WriteLine($"RZCONTROL endpoint not found for PID 0x{productId:X4}.");
            return 2;
        }

        if (!RazerDkmTableReader.TryRead(path, out var table, out var error))
        {
            Console.Error.WriteLine(error);
            return 3;
        }

        Console.WriteLine($"pid=0x{productId:X4} bytes={table.Length}");
        Console.WriteLine(Convert.ToHexString(table));
        return 0;
    }

    private static void MigrateLegacyInstall()
    {
        try
        {
            _ = LegacyPerUserInstallMigrator.CopyLegacyUserData(Environment.ProcessPath);
            _ = LegacyPerUserInstallMigrator.CleanupTransitionalRootIfMigrated();
        }
        catch (Exception)
        {
            // The new install remains usable; the old install is left intact
            // for a later manual cleanup if migration cannot complete.
        }
    }

    private static void MigrateLegacyUserData()
    {
        try
        {
            _ = LegacyPerUserInstallMigrator.TryMigrate(Environment.ProcessPath);
            _ = LegacyPerUserInstallMigrator.CopyLegacyUserData(Environment.ProcessPath);
            _ = LegacyPerUserInstallMigrator.CleanupTransitionalRootIfMigrated();
        }
        catch (Exception)
        {
        }

        try
        {
            MigrateLegacyShortcuts();
        }
        catch (Exception)
        {
        }
    }

    private static void RepairStartupRegistration()
    {
        try
        {
            if (OpenSynapseServiceInstaller.IsMachineInstallPath(Environment.ProcessPath) &&
                Environment.ProcessPath is string executablePath)
            {
                _ = WindowsStartupManager.TryRepairStaleRegistration(executablePath);
            }
        }
        catch (Exception)
        {
        }
    }

    private static void EnsureOpenSynapseService()
    {
        try
        {
            _ = OpenSynapseServiceInstaller.EnsureInstalled(Environment.ProcessPath);
        }
        catch (Exception)
        {
            // A missing service falls back to the existing UAC helper.
        }
    }

    private static void StopOpenSynapseService()
    {
        try
        {
            if (OpenSynapseServiceInstaller.IsMachineInstallPath(Environment.ProcessPath))
            {
                _ = OpenSynapseServiceInstaller.Stop();
            }
        }
        catch (Exception)
        {
        }
    }

    /// <summary>
    /// Creates the macro agent's scheduled task.
    ///
    /// The install hook runs elevated, and a task at the highest integrity
    /// level can only be created by an administrator. The task then starts
    /// the agent in the user's session, so no further prompt is needed.
    /// </summary>
    private static void EnsureMacroAgentTask()
    {
        try
        {
            if (Environment.ProcessPath is { } executablePath &&
                ElevatedMacroTaskInstaller.EnsureInstalled(executablePath))
            {
                ElevatedMacroTaskInstaller.TryStart();
            }
        }
        catch (Exception)
        {
            // Macros keep working through the app's own input path.
        }
    }

    private static void RemoveMacroAgentTask()
    {
        try
        {
            _ = ElevatedMacroTaskInstaller.Remove();
        }
        catch (Exception)
        {
        }
    }

    private static void RemoveStartupRegistration()
    {
        try
        {
            WindowsStartupManager.Remove();
        }
        catch (Exception)
        {
        }
    }

    private static void MigrateChromaShim()
    {
        try
        {
            LegacyChromaShimMigration.TryRestore();
            LegacyChromaShimMigration.TryRemovePackagedShim();
        }
        catch (Exception)
        {
            // Install and uninstall must finish even when the SDK DLL is locked by a game.
        }
    }

    private static void OfferUserDataCleanup()
    {
        try
        {
            UninstallUserDataPrompt.Offer();
        }
        catch (Exception)
        {
        }
    }

    private static bool IsRunningAsSystem()
    {
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            return identity.User?.IsWellKnown(WellKnownSidType.LocalSystemSid) == true;
        }
        catch (Exception exception) when (
            exception is InvalidOperationException or UnauthorizedAccessException or
                System.Security.SecurityException)
        {
            return false;
        }
    }

    private static void RemoveOpenSynapseService()
    {
        try
        {
            if (OpenSynapseServiceInstaller.IsMachineInstallPath(Environment.ProcessPath))
            {
                _ = OpenSynapseServiceInstaller.Remove();
            }
        }
        catch (Exception)
        {
        }
    }

    private static void RemoveShortcuts()
    {
        // The machine-wide MSI shortcut points at the OpenSynapse.exe launcher, which
        // Velopack's package-relative lookup does not own; remove those files by name.
        foreach (var shortcut in ShortcutFiles())
        {
            TryDeleteShortcut(shortcut);
        }
    }

    private static IEnumerable<string> ShortcutFiles()
    {
        var roots = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.StartMenu), "Programs"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu), "Programs"),
        };
        foreach (var root in roots.Where(root => !string.IsNullOrWhiteSpace(root)))
        {
            foreach (var name in new[] { "OpenSynapse.lnk", "OpenSynapse.App.lnk" })
            {
                var path = Path.Combine(root, name);
                if (File.Exists(path))
                {
                    yield return path;
                }
            }
        }
    }

    private static void TryDeleteShortcut(string shortcutPath)
    {
        try
        {
            using var link = new ShellLink(shortcutPath);
            if (link.Target is not string target ||
                !Path.GetFileName(target).StartsWith("OpenSynapse", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }
        }
        catch (Exception)
        {
            return;
        }

        try
        {
            File.Delete(shortcutPath);
        }
        catch (Exception)
        {
            // A locked or protected shortcut must not block the uninstall.
        }
    }

#pragma warning disable CS0618
    private static void PreserveExistingShortcuts()
    {
        try
        {
            var locations = ShortcutLocation.Desktop | ShortcutLocation.StartMenuRoot;
            var shortcuts = new Shortcuts(VelopackLocator.Current);
            if (shortcuts.FindShortcuts("OpenSynapse.App.exe", locations).Count > 0)
            {
                // updateOnly preserves a user's deleted desktop or Start-menu shortcut.
                shortcuts.CreateShortcut("OpenSynapse.App.exe", locations, updateOnly: true,
                    programArguments: null, icon: null);
            }
        }
        catch (Exception)
        {
            // Shortcut maintenance must never block an otherwise valid update.
        }

        try
        {
            // The machine-wide install has no Velopack-managed shortcut of its
            // own, so the legacy per-user ones need retargeting separately.
            MigrateLegacyShortcuts();
        }
        catch (Exception)
        {
        }
    }
#pragma warning restore CS0618

    /// <summary>
    /// Retargets the shortcuts the legacy per-user install left behind to the
    /// machine-wide executable. Velopack's Shortcuts only knows the current
    /// install root, so the legacy .lnk files are read and rewritten directly.
    /// </summary>
    private static void MigrateLegacyShortcuts()
    {
        var machineExecutable = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(machineExecutable) ||
            !OpenSynapseServiceInstaller.IsMachineInstallPath(machineExecutable))
        {
            return;
        }

        // The app runs from current\, while the launcher stub the MSI
        // shortcut points at sits in the install root above it.
        var installRoot = Path.GetDirectoryName(
            Path.GetDirectoryName(machineExecutable) ?? string.Empty) ?? string.Empty;
        var launcher = Path.Combine(installRoot, "OpenSynapse.exe");
        var machineTarget = File.Exists(launcher) ? launcher : machineExecutable;

        foreach (var shortcut in LegacyShortcutFiles())
        {
            TryRetargetShortcut(shortcut, machineTarget);
        }
    }

    private static IEnumerable<string> LegacyShortcutFiles()
    {
        var desktop = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
        var startMenu = Environment.GetFolderPath(Environment.SpecialFolder.StartMenu);
        var roots = new[] { desktop, Path.Combine(startMenu, "Programs") };
        foreach (var root in roots.Where(root => !string.IsNullOrWhiteSpace(root)))
        {
            // Velopack names the shortcut after the pack title, while a
            // shortcut created from the executable alone uses its file name.
            foreach (var name in new[] { "OpenSynapse.lnk", "OpenSynapse.App.lnk" })
            {
                var path = Path.Combine(root, name);
                if (File.Exists(path))
                {
                    yield return path;
                }
            }
        }
    }

    private static void TryRetargetShortcut(string shortcutPath, string machineTarget)
    {
        try
        {
            using var link = new ShellLink(shortcutPath);
            var target = link.Target;
            if (string.IsNullOrWhiteSpace(target) ||
                !IsLegacyTarget(target))
            {
                return;
            }

            link.Target = machineTarget;
            link.WorkingDirectory = Path.GetDirectoryName(machineTarget) ?? string.Empty;
            link.Save();
        }
        catch (Exception)
        {
            // A shortcut that cannot be retargeted must not block the launch.
        }
    }

    /// <summary>
    /// True when the shortcut still points into the legacy per-user install.
    /// A shortcut that already points at the machine-wide install is left
    /// alone so repeated launches do not rewrite it.
    /// </summary>
    private static bool IsLegacyTarget(string target) =>
        LegacyPerUserInstallMigrator.IsUnderLegacyRoot(target);
}
