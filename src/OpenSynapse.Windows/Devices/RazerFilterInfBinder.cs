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

internal readonly record struct RazerFilterAttachmentState(
    bool AnyAttached,
    int KeyboardInstances,
    int AttachedKeyboardInstances)
{
    internal bool KeyboardAttached => KeyboardInstances == AttachedKeyboardInstances;

    internal bool IsAttached => AnyAttached && KeyboardAttached;
}

/// <summary>
/// The vendor package stages RzDevU_&lt;pid&gt;_*.inf into the DriverStore when it
/// runs silently, but a staged filter INF is not attached to any device and a
/// reboot cannot attach it. Installing the staged INF against the present nodes
/// is what writes UpperFilters and creates the RAZER\DeviceContol child.
/// </summary>
internal static partial class RazerFilterInfBinder
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

        // The vendor package can exit 0 after only unpacking its INFs under
        // Program Files, so DriverStore has no oem*.inf to find. pnputil accepts
        // the vendor copies directly, which is the only path left on that host.
        if (matches.Count == 0 &&
            string.Equals(infDirectory, WindowsInfDirectory, StringComparison.OrdinalIgnoreCase))
        {
            matches.AddRange(FindVendorInfs(productId));
        }

        return matches;
    }

    internal static IReadOnlyList<string> FindVendorInfs(
        ushort productId,
        IEnumerable<string>? baseDirectories = null)
    {
        var matches = new List<string>();
        var roots = baseDirectories?.ToArray() ?? new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
        };
        foreach (var candidate in GetCandidateProductIds(productId))
        {
            var folder = $"PID{candidate:X4}Drv";
            foreach (var baseDirectory in roots)
            {
                if (string.IsNullOrEmpty(baseDirectory))
                {
                    continue;
                }

                var root = Path.Combine(baseDirectory, "Razer", folder, "Drivers");
                if (!Directory.Exists(root))
                {
                    continue;
                }

                try
                {
                    matches.AddRange(Directory.EnumerateFiles(
                        root,
                        $"RzDevU_{candidate:X4}_*.inf",
                        SearchOption.AllDirectories));
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    continue;
                }
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
                // pnputil refuses /install while the inbox driver still owns the
                // collection, so register the package before binding it.
                var addCode = await RunPnpUtilAsync(inf, install: false, cancellationToken)
                    .ConfigureAwait(false);
                if (!RazerDriverInstaller.IsSuccessfulExitCode(addCode))
                {
                    exitCodes.Add($"{DescribeInf(inf)} returned {addCode}");
                    succeeded = false;
                    continue;
                }

                var exitCode = await RunPnpUtilAsync(inf, install: true, cancellationToken).ConfigureAwait(false);
                var description = DescribeInf(inf);
                for (var attempt = 0; exitCode == 259 && attempt < 3; attempt++)
                {
                    // Installing a filter re-enumerates the HID children. A device
                    // that is still coming back reports ERROR_PENDING; wait for it
                    // instead of failing the whole bind after one short retry.
                    await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken).ConfigureAwait(false);
                    exitCode = await RunPnpUtilAsync(inf, install: true, cancellationToken).ConfigureAwait(false);
                }
                exitCodes.Add($"{description} returned {exitCode}");

                succeeded &= RazerDriverInstaller.IsSuccessfulExitCode(exitCode);
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
            exception is Win32Exception or IOException or InvalidOperationException or
                UnauthorizedAccessException)
        {
            return new(RazerFilterBindStatus.Failed, exception.Message);
        }
    }

    private static async Task<int> RunPnpUtilAsync(
        string inf,
        bool install,
        CancellationToken cancellationToken)
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
        if (install)
        {
            startInfo.ArgumentList.Add("/install");
        }

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("The driver binder did not start.");
        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        return process.ExitCode;
    }

    private static IReadOnlyList<ushort> GetCandidateProductIds(ushort productId) =>
        RazerDriverPackageCatalog.TryResolve(productId, out var package)
            ? RazerDriverPackageCatalog.GetCandidateProductIds(productId, package)
            : [productId];

    private const string HidEnumPath = @"SYSTEM\CurrentControlSet\Enum\HID";

    /// <summary>
    /// True when every keyboard collection of the present product carries a RzDev
    /// filter from the candidate list. The candidate list includes the wired
    /// sibling, so its filter name is accepted; stale registry instances of that
    /// sibling are not counted.
    /// </summary>
    internal static bool IsAttached(
        IReadOnlyList<ushort> productIds,
        RegistryKey? enumRoot = null,
        IReadOnlySet<ushort>? presentProductIds = null,
        IReadOnlySet<(ushort ProductId, int InterfaceNumber)>? coveredInterfaces = null) =>
        InspectAttachment(productIds, enumRoot, presentProductIds, coveredInterfaces).IsAttached;

    internal static RazerFilterAttachmentState InspectAttachment(
        IReadOnlyList<ushort> productIds,
        RegistryKey? enumRoot = null,
        IReadOnlySet<ushort>? presentProductIds = null,
        IReadOnlySet<(ushort ProductId, int InterfaceNumber)>? coveredInterfaces = null)
    {
        ArgumentNullException.ThrowIfNull(productIds);
        var ownsRoot = enumRoot is null;
        var root = enumRoot ?? Registry.LocalMachine.OpenSubKey(HidEnumPath);
        if (root is null)
        {
            return default;
        }

        var anyAttached = false;
        var keyboardInstances = 0;
        var attachedKeyboardInstances = 0;
        var filters = productIds.Select(static id => $"RzDev_{id:X4}").ToArray();
        try
        {
            foreach (var productId in productIds)
            {
                if (presentProductIds is not null && !presentProductIds.Contains(productId))
                {
                    continue;
                }

                foreach (var (deviceName, instanceName) in InstanceNames(root, productId))
                {
                    if (coveredInterfaces is not null &&
                        (!TryGetInterfaceNumber(deviceName, out var interfaceNumber) ||
                         !coveredInterfaces.Contains((productId, interfaceNumber))))
                    {
                        continue;
                    }

                    using var instance = root.OpenSubKey($@"{deviceName}\{instanceName}");
                    if (instance is null)
                    {
                        continue;
                    }

                    var attached = filters.Any(filter => HasFilter(instance, filter));
                    anyAttached |= attached;
                    if (!IsKeyboardInstance(instance))
                    {
                        continue;
                    }

                    keyboardInstances++;
                    if (attached)
                    {
                        attachedKeyboardInstances++;
                    }
                }
            }
        }
        finally
        {
            if (ownsRoot)
            {
                root.Dispose();
            }
        }

        return new RazerFilterAttachmentState(
            anyAttached,
            keyboardInstances,
            attachedKeyboardInstances);
    }

    internal static IReadOnlyList<string> FindInstances(
        IReadOnlyList<ushort> productIds,
        RegistryKey? enumRoot = null,
        IReadOnlySet<ushort>? presentProductIds = null)
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
                if (presentProductIds is not null && !presentProductIds.Contains(productId))
                {
                    continue;
                }

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

    /// <summary>
    /// Keyboard collections that are present but still lack the RzDev filter. The
    /// count alone does not say which collection needs the repair.
    /// </summary>
    internal static IReadOnlyList<string> FindUnfilteredKeyboardInstances(
        IReadOnlyList<ushort> productIds,
        RegistryKey? enumRoot = null,
        IReadOnlySet<ushort>? presentProductIds = null,
        IReadOnlySet<(ushort ProductId, int InterfaceNumber)>? coveredInterfaces = null)
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
            var filters = productIds.Select(static id => $"RzDev_{id:X4}").ToArray();
            foreach (var productId in productIds)
            {
                if (presentProductIds is not null && !presentProductIds.Contains(productId))
                {
                    continue;
                }

                foreach (var (deviceName, instanceName) in InstanceNames(root, productId))
                {
                    if (coveredInterfaces is not null &&
                        (!TryGetInterfaceNumber(deviceName, out var interfaceNumber) ||
                         !coveredInterfaces.Contains((productId, interfaceNumber))))
                    {
                        continue;
                    }

                    using var instance = root.OpenSubKey($@"{deviceName}\{instanceName}");
                    if (instance is null || !IsKeyboardInstance(instance) ||
                        filters.Any(filter => HasFilter(instance, filter)))
                    {
                        continue;
                    }

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

    private const string KeyboardClassGuid =
        "{4D36E96B-E325-11CE-BFC1-08002BE10318}";

    private static bool HasFilter(
        RegistryKey instance,
        string filter,
        string valueName = "UpperFilters")
    {
        if (ContainsFilter(instance.GetValue(valueName), filter))
        {
            return true;
        }

        using var parameters = instance.OpenSubKey("Device Parameters");
        return ContainsFilter(parameters?.GetValue(valueName), filter);
    }

    private static bool IsKeyboardInstance(RegistryKey instance)
    {
        if (instance.GetValue("ClassGUID") is string classGuid &&
            string.Equals(classGuid, KeyboardClassGuid, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (instance.GetValue("HardwareID") is string[] hardwareIds)
        {
            foreach (var hardwareId in hardwareIds)
            {
                if (hardwareId.Contains("HID_DEVICE_SYSTEM_KEYBOARD", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool ContainsFilter(object? value, string filter) =>
        value switch
        {
            string[] names => names.Any(name =>
                name.Contains(filter, StringComparison.OrdinalIgnoreCase)),
            string name => name.Contains(filter, StringComparison.OrdinalIgnoreCase),
            _ => false,
        };

    private static string DescribeInf(string path)
    {
        var fileName = Path.GetFileName(path);
        try
        {
            foreach (var line in File.ReadLines(path).Take(20))
            {
                var marker = line.IndexOf("RzDevU_", StringComparison.OrdinalIgnoreCase);
                if (marker < 0)
                {
                    continue;
                }

                var original = line[marker..].Trim();
                return string.Equals(original, fileName, StringComparison.OrdinalIgnoreCase)
                    ? fileName
                    : $"{fileName} ({original})";
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }

        return fileName;
    }

    private static string CommandLine(IReadOnlyList<string> infs)
    {
        const char quote = '"';
        // Every argument is a drive path from the Windows INF directory, so no
        // caller-controlled text reaches the command line.
        // Register first: /install is refused while the inbox driver still owns
        // the collection, which would otherwise abort the whole && chain.
        var register = infs.Select(inf => string.Concat(
            "pnputil /add-driver ", quote, inf, quote));
        var bind = infs.Select(inf => string.Concat(
            "pnputil /add-driver ", quote, inf, quote, " /install"));
        var command = string.Join(" && ", register.Concat(bind));
        return string.Concat("/s /c ", quote, command, quote);
    }
}
