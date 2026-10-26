using System.Security.Cryptography;

namespace OpenSynapse.Windows.Audio;

internal enum ThxHeadsetAvailability
{
    UnsupportedDevice,
    ServiceMissing,
    NativeMissing,
    Available,
}

internal sealed record ThxHeadsetStatus(ThxHeadsetAvailability Availability, string? ErrorCode);

/// <summary>
/// THX for the BlackShark V2 Pro 2020 (0528) and 2023 (0555) needs three layers:
/// the THX APO bound to the headset audio endpoint, the VSSrv service, and
/// ThxV3Native.dll. This class checks the last two; the APO comes with each model's
/// installer.
/// </summary>
internal static class ThxHeadsetAdmission
{
    internal const string ServiceName = "VSSrv";

    private static readonly IReadOnlyDictionary<ushort, string> ServiceInstallers =
        new Dictionary<ushort, string>
        {
            [0x0528] =
                "https://app-assets.razer.com/files/synapse/devices/1320/1.0.4.2/THX-0528-BlackSharkV2Pro2020-v3.2.3.0.exe",
            [0x0555] =
                "https://app-assets.razer.com/files/synapse/devices/1365/1.0.0.16/THX-0555-BlackSharkV2Pro2023-v3.2.3.0.exe",
        };
    internal const string NativeDllName = "ThxV3Native_v1.1.42.1.dll";
    internal const string NativeDllUri =
        "https://app-assets.razer.com/files/synapse/devices/common/THX/ThxV3Native_v1.1.42.1.dll";
    internal const long NativeDllLength = 3_351_240;
    internal const string NativeDllSha256 =
        "5DE7CBBCDB0DDB70E5622D6570FB2DEBB5186AE1182E693D4F212E5DF1EC835A";

    private static readonly HttpClient ComponentClient = new()
    {
        Timeout = TimeSpan.FromSeconds(60),
    };

    internal static string NativeDllPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Razer",
        "RazerAppEngine",
        "User Data",
        "Apps",
        "Common",
        "thxv3common",
        NativeDllName);

    internal static bool IsSupported(ushort productId) => ServiceInstallers.ContainsKey(productId);

    internal static string GetServiceInstallerUri(ushort productId) =>
        ServiceInstallers.TryGetValue(productId, out var uri)
            ? uri
            : throw new ArgumentOutOfRangeException(nameof(productId), "No THX installer is pinned for this headset.");

    internal static ThxHeadsetStatus Check(ushort productId)
    {
        if (!IsSupported(productId))
        {
            return Evaluate(productId, false, false);
        }

        return Evaluate(
            productId,
            ThxAudioAdmission.HasRunningService(ServiceName),
            HasVerifiedNativeClient());
    }

    internal static ThxHeadsetStatus Evaluate(ushort productId, bool serviceRunning, bool nativePresent)
    {
        if (!IsSupported(productId))
        {
            return new(ThxHeadsetAvailability.UnsupportedDevice, "ThxHeadsetUnsupported");
        }
        if (!serviceRunning)
        {
            return new(ThxHeadsetAvailability.ServiceMissing, "ThxServiceMissing");
        }
        if (!nativePresent)
        {
            return new(ThxHeadsetAvailability.NativeMissing, "ThxNativeMissing");
        }
        return new(ThxHeadsetAvailability.Available, null);
    }

    internal static bool HasVerifiedNativeClient()
    {
        try
        {
            using var stream = new FileStream(NativeDllPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            return CryptographicOperations.FixedTimeEquals(
                Convert.FromHexString(NativeDllSha256),
                SHA256.HashData(stream));
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or CryptographicException or ArgumentException)
        {
            return false;
        }
    }

    internal static async Task<ThxHeadsetStatus> InstallNativeComponentAsync(
        ushort productId,
        CancellationToken cancellationToken = default)
    {
        var destination = NativeDllPath;
        var directory = Path.GetDirectoryName(destination)
            ?? throw new InvalidOperationException("The THX component path has no directory.");
        var temporary = Path.Combine(
            Path.GetTempPath(),
            $"{NativeDllName}.{Guid.NewGuid():N}.download");
        try
        {
            using var response = await ComponentClient.GetAsync(
                NativeDllUri,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength is { } declaredLength &&
                declaredLength != NativeDllLength)
            {
                return new(ThxHeadsetAvailability.NativeMissing, "ThxDownloadVerificationFailed");
            }

            await using (var source = await response.Content
                .ReadAsStreamAsync(cancellationToken).ConfigureAwait(false))
            await using (var target = new FileStream(
                temporary,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 81920,
                FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                await source.CopyToAsync(target, cancellationToken).ConfigureAwait(false);
            }

            if (new FileInfo(temporary).Length != NativeDllLength)
            {
                return new(ThxHeadsetAvailability.NativeMissing, "ThxDownloadVerificationFailed");
            }

            using (var stream = new FileStream(temporary, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                if (!string.Equals(
                        Convert.ToHexString(SHA256.HashData(stream)),
                        NativeDllSha256,
                        StringComparison.OrdinalIgnoreCase))
                {
                    return new(ThxHeadsetAvailability.NativeMissing, "ThxDownloadVerificationFailed");
                }
            }

            Directory.CreateDirectory(directory);
            File.Move(temporary, destination, overwrite: true);
            return Check(productId);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new(ThxHeadsetAvailability.NativeMissing, "ThxDownloadFailed");
        }
        catch (Exception exception) when (
            exception is HttpRequestException or IOException or UnauthorizedAccessException)
        {
            return new(ThxHeadsetAvailability.NativeMissing, "ThxDownloadFailed");
        }
        finally
        {
            try
            {
                File.Delete(temporary);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
            }
        }
    }
}
