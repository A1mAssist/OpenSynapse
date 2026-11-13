using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using Microsoft.Win32;

namespace OpenSynapse.Windows.Devices;

/// <summary>
/// Blade Fn support rides on a lower filter that the vendor driver package never
/// installs, so the pieces that complete it live here rather than in the shared
/// HID filter binder.
/// </summary>
internal static partial class RazerFilterInfBinder
{
    private const string UsbEnumPath = @"SYSTEM\CurrentControlSet\Enum\USB";

    /// <summary>
    /// The DKM INF installs RzDev_&lt;pid&gt; as a LowerFilter on the USB composite
    /// function that owns the Fn row. When only the HID filter is attached the
    /// RZCONTROL endpoint still opens, so a missing DKM looks healthy while every
    /// Fn key reads zero.
    /// </summary>
    internal static bool IsDkmAttached(
        ushort productId,
        RegistryKey? usbEnumRoot = null)
    {
        var ownsRoot = usbEnumRoot is null;
        var root = usbEnumRoot ?? Registry.LocalMachine.OpenSubKey(UsbEnumPath);
        if (root is null)
        {
            return false;
        }

        var marker = $"VID_1532&PID_{productId:X4}&MI_";
        var filter = $"RzDev_{productId:X4}";
        try
        {
            foreach (var deviceName in root.GetSubKeyNames())
            {
                // Any composite function can own the Fn row and only the DKM INF
                // writes LowerFilters, so the interface number is not assumed.
                if (!deviceName.StartsWith(marker, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                using var device = root.OpenSubKey(deviceName);
                if (device is null)
                {
                    continue;
                }

                foreach (var instanceName in device.GetSubKeyNames())
                {
                    using var instance = device.OpenSubKey(instanceName);
                    if (instance is not null && HasFilter(instance, filter, "LowerFilters"))
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

    /// <summary>
    /// Installs the DKM lower filter that the vendor package leaves out. The
    /// package's own installer stages only the HID filters, so a machine can
    /// show a healthy RZCONTROL endpoint while the Fn row never reaches the
    /// driver. The virtual consumer node does not exist until the DKM driver
    /// creates it, so that INF is staged, never installed.
    /// </summary>
    internal static async Task<RazerFilterBindResult> ApplyBladeDkmAsync(
        ushort productId,
        string extractRoot,
        CancellationToken cancellationToken)
    {
        var lower = Path.Combine(
            extractRoot, "Drivers", "x64", "Lower", $"RzDevU_{productId:X4}_Dkm.inf");
        if (!File.Exists(lower))
        {
            return new(RazerFilterBindStatus.NoStagedInf, null);
        }

        try
        {
            var exitCode = await RunPnpUtilAsync(lower, install: true, cancellationToken)
                .ConfigureAwait(false);
            if (!RazerDriverInstaller.IsSuccessfulExitCode(exitCode))
            {
                return new(
                    RazerFilterBindStatus.Failed,
                    $"RzDevU_{productId:X4}_Dkm.inf returned {exitCode}.");
            }

            var virtualConsumer = Path.Combine(
                extractRoot, "Drivers", "x64", "VCon", $"RzDevU_{productId:X4}_VCon.inf");
            if (File.Exists(virtualConsumer))
            {
                var stageCode = await RunPnpUtilAsync(
                        virtualConsumer,
                        install: false,
                        cancellationToken)
                    .ConfigureAwait(false);
                if (!RazerDriverInstaller.IsSuccessfulExitCode(stageCode))
                {
                    return new(
                        RazerFilterBindStatus.Failed,
                        $"RzDevU_{productId:X4}_VCon.inf returned {stageCode}.");
                }
            }

            return IsDkmAttached(productId)
                ? new(RazerFilterBindStatus.Applied, null)
                : new(
                    RazerFilterBindStatus.Failed,
                    $"LowerFilters RzDev_{productId:X4} was not written to any MI_ node.");
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

    /// <summary>
    /// Runs the same DKM install through one elevated prompt. The app-side caller
    /// is not elevated once the vendor installer exits, so the service path uses
    /// <see cref="ApplyBladeDkmAsync"/> directly instead.
    /// </summary>
    internal static async Task<RazerFilterBindResult> ApplyBladeDkmElevatedAsync(
        ushort productId,
        string extractRoot,
        CancellationToken cancellationToken)
    {
        var lower = Path.Combine(
            extractRoot, "Drivers", "x64", "Lower", $"RzDevU_{productId:X4}_Dkm.inf");
        if (!File.Exists(lower))
        {
            return new(RazerFilterBindStatus.NoStagedInf, null);
        }

        var virtualConsumer = Path.Combine(
            extractRoot, "Drivers", "x64", "VCon", $"RzDevU_{productId:X4}_VCon.inf");
        var command = new StringBuilder()
            .Append("pnputil /add-driver \"").Append(lower).Append("\" /install");
        if (File.Exists(virtualConsumer))
        {
            command.Append(" && pnputil /add-driver \"").Append(virtualConsumer).Append('"');
        }

        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = Path.Combine(Environment.SystemDirectory, "cmd.exe"),
                Arguments = $"/s /c \"{command}\"",
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
            if (process.ExitCode is not (0 or 3010 or 1641))
            {
                return new(RazerFilterBindStatus.Failed, $"pnputil returned {process.ExitCode}.");
            }

            return IsDkmAttached(productId)
                ? new(RazerFilterBindStatus.Applied, null)
                : new(
                    RazerFilterBindStatus.Failed,
                    $"LowerFilters RzDev_{productId:X4} was not written to any MI_ node.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Win32Exception exception) when (exception.NativeErrorCode == 1223)
        {
            return new(RazerFilterBindStatus.Failed, "The driver binder was cancelled.");
        }
        catch (Exception exception) when (exception is Win32Exception or IOException)
        {
            return new(RazerFilterBindStatus.Failed, exception.Message);
        }
    }
}