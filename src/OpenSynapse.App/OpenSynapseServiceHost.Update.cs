using System.Security.Principal;
using System.Security.Cryptography;
using System.Text.Json;
using System.Globalization;
using OpenSynapse.Core.Devices;
using OpenSynapse.Windows.Lifecycle;

namespace OpenSynapse.App;

internal static partial class OpenSynapseServiceHost
{
    private static readonly HttpClient UpdateHttpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(30),
    };

    private const string UpdateManifestUrl =
        "https://github.com/A1mAssist/OpenSynapse/releases/download/v{0}/releases.win.json";

    private static async Task<OpenSynapseServiceResponse> ApplyUpdateAsync(
        OpenSynapseServiceRequest request,
        SecurityIdentifier? authorizedUser,
        CancellationToken cancellationToken)
    {
        if (authorizedUser is null)
        {
            return UpdateUnsupported("The OpenSynapse service is missing its authorized user SID.");
        }

        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, ".."));
        if (!File.Exists(Path.Combine(root, "Update.exe")))
        {
            root = Path.GetFullPath(AppContext.BaseDirectory);
        }

        if (!OpenSynapseServiceInstaller.IsMachineInstallPath(root))
        {
            return UpdateFailed("The OpenSynapse install root is not protected.");
        }

        var packagePath = Path.GetFullPath(request.PackagePath!);
        var packageFileName = request.PackageFileName!;
        if (!StringComparer.Ordinal.Equals(Path.GetFileName(packagePath), packageFileName) ||
            !packageFileName.EndsWith(".nupkg", StringComparison.OrdinalIgnoreCase) ||
            !File.Exists(packagePath))
        {
            return UpdateFailed("The downloaded update package is unavailable.");
        }

        var packages = Path.Combine(root, "packages");
        var stagedPath = Path.Combine(packages, packageFileName);
        var verificationPath = Path.Combine(
            packages,
            $"{packageFileName}.verify-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(packages);
            File.Copy(packagePath, verificationPath, overwrite: true);
            if (!await VerifyPackageAsync(
                    request.PackageVersion!,
                    packageFileName,
                    verificationPath,
                    cancellationToken).ConfigureAwait(false))
            {
                return UpdateFailed("The update package could not be verified.");
            }

            File.Move(verificationPath, stagedPath, overwrite: true);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or
                System.Security.SecurityException or HttpRequestException or JsonException or
                InvalidOperationException or FormatException or NotSupportedException)
        {
            return UpdateFailed($"The update package could not be staged: {exception.Message}");
        }
        finally
        {
            try
            {
                File.Delete(verificationPath);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        var started = OpenSynapseUpdateTaskInstaller.TryStart(
            root,
            packageFileName,
            request.WaitProcessId!.Value,
            authorizedUser,
            out var taskError);
        return started
            ? new OpenSynapseServiceResponse(
                OpenSynapseServiceProtocol.Version,
                Handled: true,
                DedicatedGpuSwitchOutcome.Success,
                ExitCode: 0,
                Status: null,
                Error: null)
            : UpdateFailed(taskError ?? "The update task could not be started.");
    }

    private static async Task<bool> VerifyPackageAsync(
        string version,
        string packageFileName,
        string packagePath,
        CancellationToken cancellationToken)
    {
        var url = string.Format(
            CultureInfo.InvariantCulture,
            UpdateManifestUrl,
            Uri.EscapeDataString(version));
        using var document = JsonDocument.Parse(
            await UpdateHttpClient.GetStringAsync(url, cancellationToken).ConfigureAwait(false));
        if (!TryGetProperty(document.RootElement, "Assets", out var assets) ||
            assets.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        foreach (var asset in assets.EnumerateArray())
        {
            if (!TryGetProperty(asset, "FileName", out var name) ||
                !StringComparer.Ordinal.Equals(name.GetString(), packageFileName) ||
                !TryGetProperty(asset, "SHA256", out var hash) ||
                !TryGetProperty(asset, "Size", out var size))
            {
                continue;
            }

            var expectedHash = hash.GetString();
            var expectedSize = size.GetInt64();
            var actualSize = new FileInfo(packagePath).Length;
            if (string.IsNullOrWhiteSpace(expectedHash) || actualSize != expectedSize)
            {
                return false;
            }

            await using var stream = File.OpenRead(packagePath);
            var actualHash = Convert.ToHexString(
                await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false));
            return StringComparer.OrdinalIgnoreCase.Equals(actualHash, expectedHash);
        }

        return false;
    }

    private static bool TryGetProperty(JsonElement element, string name, out JsonElement value) =>
        element.TryGetProperty(name, out value) ||
        element.TryGetProperty(name.ToLowerInvariant(), out value);

    private static OpenSynapseServiceResponse UpdateFailed(string error) =>
        new(
            OpenSynapseServiceProtocol.Version,
            Handled: true,
            DedicatedGpuSwitchOutcome.Failed,
            ExitCode: 1,
            Status: null,
            Error: error);

    private static OpenSynapseServiceResponse UpdateUnsupported(string error) =>
        new(
            OpenSynapseServiceProtocol.Version,
            Handled: false,
            DedicatedGpuSwitchOutcome.Failed,
            ExitCode: 0,
            Status: null,
            Error: error);
}
