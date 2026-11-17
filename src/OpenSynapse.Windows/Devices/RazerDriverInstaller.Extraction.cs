using System.ComponentModel;
using System.Diagnostics;

namespace OpenSynapse.Windows.Devices;

internal static partial class RazerDriverInstaller
{
    /// <summary>
    /// The vendor package is an NSIS self-extractor. Its silent installer can
    /// exit successfully without leaving the filter INFs in the DriverStore or
    /// Program Files, so the verified package is the only dependable source.
    /// </summary>
    internal static Task<string?> ExtractLowerFiltersAsync(
        string packagePath,
        string outputDirectory,
        CancellationToken cancellationToken) =>
        ExtractPackageAsync(
            packagePath,
            outputDirectory,
            [@"Drivers\x64\Lower\*", @"Drivers\x64\VCon\*"],
            cancellationToken);

    private static Task<string?> ExtractDriverPackageAsync(
        string packagePath,
        string outputDirectory,
        CancellationToken cancellationToken) =>
        ExtractPackageAsync(packagePath, outputDirectory, null, cancellationToken);

    private static async Task<string?> ExtractPackageAsync(
        string packagePath,
        string outputDirectory,
        IReadOnlyList<string>? filePatterns,
        CancellationToken cancellationToken)
    {
        // 7za is the reduced standalone build and has no NSIS handler. The full
        // 7z.exe + 7z.dll pair is shipped next to it for Razer's self-extractors.
        var extractor = Path.Combine(AppContext.BaseDirectory, "7z.exe");
        if (!File.Exists(extractor))
        {
            return null;
        }

        Directory.CreateDirectory(outputDirectory);
        var startInfo = new ProcessStartInfo
        {
            FileName = extractor,
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden,
        };
        startInfo.ArgumentList.Add("x");
        startInfo.ArgumentList.Add(packagePath);
        startInfo.ArgumentList.Add($"-o{outputDirectory}");
        if (filePatterns is not null)
        {
            foreach (var pattern in filePatterns)
            {
                startInfo.ArgumentList.Add(pattern);
            }
        }
        startInfo.ArgumentList.Add("-y");

        try
        {
            using var process = Process.Start(startInfo);
            if (process is null)
            {
                return null;
            }

            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            return process.ExitCode is 0 or 1 ? outputDirectory : null;
        }
        catch (Exception exception) when (
            exception is Win32Exception or IOException or InvalidOperationException)
        {
            return null;
        }
    }
}
