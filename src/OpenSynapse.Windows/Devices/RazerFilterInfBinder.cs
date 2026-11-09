using System.ComponentModel;
using System.Diagnostics;
namespace OpenSynapse.Windows.Devices;

/// <summary>
/// The vendor package stages RzDevU_&lt;pid&gt;_*.inf into the DriverStore when it
/// runs silently, but a staged filter INF is not attached to any device and a
/// reboot cannot attach it. Installing the staged INF against the present nodes
/// is what writes UpperFilters and creates the RAZER\DeviceContol child.
/// </summary>
internal static class RazerFilterInfBinder
{
    internal static IReadOnlyList<string> FindStagedInfs(ushort productId) =>
        FindStagedInfs(productId, Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.Windows), "INF"));

    internal static IReadOnlyList<string> FindStagedInfs(ushort productId, string infDirectory)
    {
        if (!Directory.Exists(infDirectory))
        {
            return [];
        }

        var baseProductId = ResolveBaseProductId(productId);
        // The vendor INF keeps its original file name in the comment block,
        // which survives the rename to oem<nn>.inf.
        var marker = $"; RzDevU_{baseProductId:X4}_";
        var matches = new List<string>();
        foreach (var path in Directory.EnumerateFiles(infDirectory, "oem*.inf"))
        {
            try
            {
                using var reader = new StreamReader(path);
                for (var lineIndex = 0; lineIndex < 8; lineIndex++)
                {
                    var line = reader.ReadLine();
                    if (line is null)
                    {
                        break;
                    }

                    if (line.StartsWith(marker, StringComparison.OrdinalIgnoreCase))
                    {
                        matches.Add(path);
                        break;
                    }
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                continue;
            }
        }

        return matches;
    }

    private static ushort ResolveBaseProductId(ushort productId)
    {
        if (RazerDriverPackageCatalog.TryResolve(productId, out var package) &&
            ushort.TryParse(
                package.ProductId,
                System.Globalization.NumberStyles.HexNumber,
                System.Globalization.CultureInfo.InvariantCulture,
                out var baseProductId))
        {
            return baseProductId;
        }

        return productId;
    }

    internal static async Task<RazerFilterBindResult> ApplyAsync(
        ushort productId,
        CancellationToken cancellationToken)
    {
        var infs = FindStagedInfs(productId);
        if (infs.Count == 0)
        {
            return new(false, null);
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
                return new(false, "The driver binder did not start.");
            }

            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            return process.ExitCode is 0 or 3010 or 1641
                ? new(true, null)
                : new(false, $"pnputil returned {process.ExitCode}.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Win32Exception exception) when (exception.NativeErrorCode == 1223)
        {
            return new(false, "The driver binder was cancelled.");
        }
        catch (Exception exception) when (exception is Win32Exception or IOException or UnauthorizedAccessException)
        {
            return new(false, exception.Message);
        }
    }

    /// <summary>
    /// Runs inside the machine-wide service, which is already elevated, so the
    /// same bind needs no UAC prompt.
    /// </summary>
    internal static async Task<RazerFilterBindResult> ApplyDirectAsync(
        ushort productId,
        CancellationToken cancellationToken)
    {
        var infs = FindStagedInfs(productId);
        if (infs.Count == 0)
        {
            return new(false, null);
        }

        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = Path.Combine(Environment.SystemDirectory, "cmd.exe"),
                Arguments = CommandLine(infs),
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden,
            };

            using var process = Process.Start(startInfo);
            if (process is null)
            {
                return new(false, "The driver binder did not start.");
            }

            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            return process.ExitCode is 0 or 3010 or 1641
                ? new(true, null)
                : new(false, string.Concat("pnputil returned ", process.ExitCode, "."));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is Win32Exception or IOException or UnauthorizedAccessException)
        {
            return new(false, exception.Message);
        }
    }

    private static string CommandLine(IReadOnlyList<string> infs)
    {
        const char quote = '"';
        // Every argument is a drive path from the Windows INF directory, so no
        // caller-controlled text reaches the command line.
        var command = string.Join(
            " & ",
            infs.Select(inf => string.Concat(
                "pnputil /add-driver ", quote, inf, quote, " /install")));
        return string.Concat("/s /c ", quote, command, quote);
    }
}

internal sealed record RazerFilterBindResult(bool Applied, string? Error);
