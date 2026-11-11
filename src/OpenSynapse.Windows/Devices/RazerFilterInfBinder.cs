using System.ComponentModel;
using System.Diagnostics;
using Microsoft.Win32;

namespace OpenSynapse.Windows.Devices;

internal enum RazerFilterBindStatus
{
    Applied,
    NoStagedInf,
    Failed,
}

internal sealed record RazerFilterBindResult(RazerFilterBindStatus Status, string? Error)
{
    internal bool Applied => Status == RazerFilterBindStatus.Applied;
}

/// <summary>
/// The vendor package stages RzDevU_&lt;pid&gt;_*.inf into the DriverStore when it
/// runs silently, but a staged filter INF is not attached to any device and a
/// reboot cannot attach it. Installing the staged INF against the present nodes
/// is what writes UpperFilters and creates the RAZER\DeviceContol child.
/// </summary>
internal static class RazerFilterInfBinder
{
    private static string WindowsInfDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.Windows),
        "INF");

    internal static IReadOnlyList<string> FindStagedInfs(ushort productId) =>
        FindStagedInfs(productId, WindowsInfDirectory);

    internal static IReadOnlyList<string> FindStagedInfs(ushort productId, string infDirectory)
    {
        if (!Directory.Exists(infDirectory))
        {
            return [];
        }

        var candidates = GetCandidateProductIds(productId);
        var matches = new List<string>();
        foreach (var path in Directory.EnumerateFiles(infDirectory, "oem*.inf"))
        {
            try
            {
                var contents = File.ReadAllText(path);
                // The base device INF also names the Pid_XXXX hardware id; only
                // RzDev filter INFs are safe to attach with pnputil.
                if (!contents.Contains("RzDev", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (candidates.Any(candidate =>
                        contents.Contains($"Pid_{candidate:X4}", StringComparison.OrdinalIgnoreCase) ||
                        contents.Contains($"; RzDevU_{candidate:X4}_", StringComparison.OrdinalIgnoreCase)))
                {
                    matches.Add(path);
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                continue;
            }
        }

        return matches;
    }

    internal static async Task<RazerFilterBindResult> ApplyAsync(
        ushort productId,
        CancellationToken cancellationToken,
        string? infDirectory = null)
    {
        var infs = FindStagedInfs(productId, infDirectory ?? WindowsInfDirectory);
        if (infs.Count == 0)
        {
            return new(RazerFilterBindStatus.NoStagedInf, null);
        }

        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = Path.Combine(Environment.SystemDirectory, "cmd.exe"),
                Arguments = CommandLine(infs),
                UseShellExecute = true,
                Verb = "runas",
                WindowStyle = ProcessWindowStyle.Hidden,
            };

            using var process = Process.Start(startInfo);
            if (process is null)
            {
                return new(RazerFilterBindStatus.Failed, "The driver binder did not start.");
            }

            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            return process.ExitCode is 0 or 3010 or 1641
                ? new(RazerFilterBindStatus.Applied, null)
                : new(RazerFilterBindStatus.Failed, $"pnputil returned {process.ExitCode}.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Win32Exception exception) when (exception.NativeErrorCode == 1223)
        {
            return new(RazerFilterBindStatus.Failed, "The driver binder was cancelled.");
        }
        catch (Exception exception) when (exception is Win32Exception or IOException or UnauthorizedAccessException)
        {
            return new(RazerFilterBindStatus.Failed, exception.Message);
        }
    }

    /// <summary>
    /// Runs inside the machine-wide service, which is already elevated, so the
    /// same bind needs no UAC prompt.
    /// </summary>
    internal static async Task<RazerFilterBindResult> ApplyDirectAsync(
        ushort productId,
        CancellationToken cancellationToken,
        string? infDirectory = null)
    {
        var infs = FindStagedInfs(productId, infDirectory ?? WindowsInfDirectory);
        if (infs.Count == 0)
        {
            return new(RazerFilterBindStatus.NoStagedInf, null);
        }

        try
        {
            var exitCodes = new List<string>(infs.Count);
            var succeeded = true;
            foreach (var inf in infs)
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = Path.Combine(Environment.SystemDirectory, "pnputil.exe"),
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WindowStyle = ProcessWindowStyle.Hidden,
                };
                startInfo.ArgumentList.Add("/add-driver");
                startInfo.ArgumentList.Add(inf);
                startInfo.ArgumentList.Add("/install");

                using var process = Process.Start(startInfo);
                if (process is null)
                {
                    return new(RazerFilterBindStatus.Failed, "The driver binder did not start.");
                }

                await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
                exitCodes.Add($"{Path.GetFileName(inf)} returned {process.ExitCode}");
                succeeded &= RazerDriverInstaller.IsSuccessfulExitCode(process.ExitCode);
            }

            return succeeded
                ? new(RazerFilterBindStatus.Applied, null)
                : new(RazerFilterBindStatus.Failed, string.Join("; ", exitCodes));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is Win32Exception or IOException or UnauthorizedAccessException)
        {
            return new(RazerFilterBindStatus.Failed, exception.Message);
        }
    }

    private static IReadOnlyList<ushort> GetCandidateProductIds(ushort productId) =>
        RazerDriverPackageCatalog.TryResolve(productId, out var package)
            ? RazerDriverPackageCatalog.GetCandidateProductIds(productId, package)
            : [productId];

    private const string HidEnumPath = @"SYSTEM\CurrentControlSet\Enum\HID";

    /// <summary>
    /// True when a present device instance of the product really carries the
    /// per-device RzDev filter in UpperFilters. A staged INF in the DriverStore
    /// is not enough: the filter only runs once it is attached to the device stack.
    /// </summary>
    internal static bool IsAttached(
        IReadOnlyList<ushort> productIds,
        RegistryKey? enumRoot = null)
    {
        ArgumentNullException.ThrowIfNull(productIds);
        var ownsRoot = enumRoot is null;
        var root = enumRoot ?? Registry.LocalMachine.OpenSubKey(HidEnumPath);
        if (root is null)
        {
            return false;
        }

        try
        {
            foreach (var productId in productIds)
            {
                var filter = $"RzDev_{productId:X4}";
                foreach (var (deviceName, instanceName) in InstanceNames(root, productId))
                {
                    using var instance = root.OpenSubKey($@"{deviceName}\{instanceName}");
                    if (instance is null)
                    {
                        continue;
                    }

                    if (ContainsFilter(instance.GetValue("UpperFilters"), filter))
                    {
                        return true;
                    }

                    using var parameters = instance.OpenSubKey("Device Parameters");
                    if (ContainsFilter(parameters?.GetValue("UpperFilters"), filter))
                    {
                        return true;
                    }
                }
            }

            return false;
        }
        finally
        {
            if (ownsRoot)
            {
                root.Dispose();
            }
        }
    }

    internal static IReadOnlyList<string> FindInstances(
        IReadOnlyList<ushort> productIds,
        RegistryKey? enumRoot = null)
    {
        ArgumentNullException.ThrowIfNull(productIds);
        var ownsRoot = enumRoot is null;
        var root = enumRoot ?? Registry.LocalMachine.OpenSubKey(HidEnumPath);
        if (root is null)
        {
            return [];
        }

        try
        {
            var instances = new List<string>();
            foreach (var productId in productIds)
            {
                foreach (var (deviceName, instanceName) in InstanceNames(root, productId))
                {
                    instances.Add($@"HID\{deviceName}\{instanceName}");
                }
            }

            return instances;
        }
        finally
        {
            if (ownsRoot)
            {
                root.Dispose();
            }
        }
    }

    private static IEnumerable<(string DeviceName, string InstanceName)> InstanceNames(
        RegistryKey root,
        ushort productId)
    {
        var marker = $"VID_1532&PID_{productId:X4}&MI_";
        foreach (var deviceName in root.GetSubKeyNames())
        {
            if (!deviceName.Contains(marker, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string[] instances;
            using (var device = root.OpenSubKey(deviceName))
            {
                if (device is null)
                {
                    continue;
                }

                instances = device.GetSubKeyNames();
            }

            foreach (var instanceName in instances)
            {
                yield return (deviceName, instanceName);
            }
        }
    }

    private static bool ContainsFilter(object? value, string filter) =>
        value switch
        {
            string[] names => names.Any(name =>
                name.Contains(filter, StringComparison.OrdinalIgnoreCase)),
            string name => name.Contains(filter, StringComparison.OrdinalIgnoreCase),
            _ => false,
        };

    private static string CommandLine(IReadOnlyList<string> infs)
    {
        const char quote = '"';
        // Every argument is a drive path from the Windows INF directory, so no
        // caller-controlled text reaches the command line.
        var command = string.Join(
            " && ",
            infs.Select(inf => string.Concat(
                "pnputil /add-driver ", quote, inf, quote, " /install")));
        return string.Concat("/s /c ", quote, command, quote);
    }
}
