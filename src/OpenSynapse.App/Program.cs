using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using OpenSynapse.Windows.Lifecycle;
using Velopack;
using Velopack.Locators;
using Velopack.Windows;

namespace OpenSynapse.App;

public static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        if (GpuModeServiceHost.TryRun(args))
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
                EnsureGpuService();
                PreserveExistingShortcuts();
            })
            .OnAfterUpdateFastCallback(_ =>
            {
                EnsureGpuService();
                RepairStartupRegistration();
                PreserveExistingShortcuts();
            })
            .OnBeforeUpdateFastCallback(_ => StopGpuService())
            .OnBeforeUninstallFastCallback(_ => RemoveGpuService())
            .Run();

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

    private static void MigrateLegacyInstall()
    {
        try
        {
            _ = LegacyPerUserInstallMigrator.TryMigrate(Environment.ProcessPath);
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
            if (DedicatedGpuModeServiceInstaller.IsMachineInstallPath(Environment.ProcessPath) &&
                Environment.ProcessPath is string executablePath)
            {
                _ = WindowsStartupManager.TryRepairStaleRegistration(executablePath);
            }
        }
        catch (Exception)
        {
        }
    }

    private static void EnsureGpuService()
    {
        try
        {
            _ = DedicatedGpuModeServiceInstaller.EnsureInstalled(Environment.ProcessPath);
        }
        catch (Exception)
        {
            // A missing service falls back to the existing UAC helper.
        }
    }

    private static void StopGpuService()
    {
        try
        {
            if (DedicatedGpuModeServiceInstaller.IsMachineInstallPath(Environment.ProcessPath))
            {
                _ = DedicatedGpuModeServiceInstaller.Stop();
            }
        }
        catch (Exception)
        {
        }
    }

    private static void RemoveGpuService()
    {
        try
        {
            if (DedicatedGpuModeServiceInstaller.IsMachineInstallPath(Environment.ProcessPath))
            {
                _ = DedicatedGpuModeServiceInstaller.Remove();
            }
        }
        catch (Exception)
        {
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
            !DedicatedGpuModeServiceInstaller.IsMachineInstallPath(machineExecutable))
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
