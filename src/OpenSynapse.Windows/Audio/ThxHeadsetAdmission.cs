using System.Security.Cryptography;
using OpenSynapse.Core.Audio;
using OpenSynapse.Core.Storage;

namespace OpenSynapse.Windows.Audio;

internal enum ThxHeadsetAvailability
{
    UnsupportedDevice,
    ServiceMissing,
    NativeMissing,
    Available,
    InstalledButInactive,
}

internal enum ThxNativeVersion
{
    V3,
    V4,
}

internal sealed record ThxHeadsetStatus(
    ThxHeadsetAvailability Availability,
    string? ErrorCode,
    bool RebootRequired = false);

internal sealed record ThxHeadsetProfile(
    ushort ProductId,
    string Name,
    ThxNativeVersion NativeVersion,
    int InstallerPage,
    string? V4ProductId,
    string? V4EndpointId,
    string NativeDllName,
    string NativeDllUri,
    long NativeDllLength,
    string NativeDllSha256,
    bool HasSidetone)
{
    internal string NativeVersionName => NativeVersion == ThxNativeVersion.V3 ? "v3" : "v4";
}

/// <summary>
/// THX for headsets is an APO bound to the USB audio endpoint, the VSSrv service
/// (V3) or the V4 audio stack, and a native control DLL. The APO and the service
/// come from the per-model Razer THX installer; the DLL is downloaded here.
/// </summary>
internal static partial class ThxHeadsetAdmission
{
    internal const string ServiceName = "VSSrv";

    private static readonly ThxRebootMarkerStore RebootMarker = new();

    private const string V3DllName = "ThxV3Native_v1.1.42.1.dll";
    private const string V3DllUri =
        "https://app-assets.razer.com/files/synapse/devices/common/THX/ThxV3Native_v1.1.42.1.dll";
    private const long V3DllLength = 3_351_240;
    private const string V3DllSha256 =
        "5DE7CBBCDB0DDB70E5622D6570FB2DEBB5186AE1182E693D4F212E5DF1EC835A";

    private const string V4Dll112Name = "ThxV4Native_v1.1.2.0.dll";
    private const string V4Dll112Uri =
        "https://app-assets.razer.com/files/synapse/devices/common/THX/ThxV4Native_v1.1.2.0.dll";
    private const long V4DllLength = 13_710_024;
    private const string V4Dll112Sha256 =
        "8EACCD31629FD23BC43B9518FFF2994272BDD7DA359D8FE6DEB547C4950730F4";

    private const string V4Dll113Name = "ThxV4Native_v1.1.3.0.dll";
    private const string V4Dll113Uri =
        "https://app-assets.razer.com/files/synapse/devices/common/THX/ThxV4Native_v1.1.3.0.dll";
    private const string V4Dll113Sha256 =
        "0D88696C023364588AC2C21624F1EA1074DB74DEFAB1F67A41F60D5866B2D4D5";

    private static readonly IReadOnlyDictionary<ushort, ThxHeadsetProfile> Profiles =
        BuildProfiles();

    private static readonly HttpClient ComponentClient = new()
    {
        Timeout = TimeSpan.FromSeconds(60),
    };

    internal static bool IsSupported(ushort productId) => Profiles.ContainsKey(productId);

    internal static IEnumerable<ushort> SupportedProductIds => Profiles.Keys;

    internal static ThxHeadsetProfile? GetProfile(ushort productId) =>
        Profiles.TryGetValue(productId, out var profile) ? profile : null;

    internal static string NativeDllPath(ThxHeadsetProfile profile) => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Razer",
        "RazerAppEngine",
        "User Data",
        "Apps",
        "Common",
        profile.NativeVersion == ThxNativeVersion.V3 ? "thxv3common" : "thxv4common",
        profile.NativeDllName);

    internal static ThxHeadsetStatus Check(ushort productId)
        => Check(productId, RebootMarker);

    /// <summary>
    /// VSSrv is demand-start. A registered service that is not running yet is not a
    /// missing install, so wake it before deciding; if it stays down, keep the
    /// installer away and report the components as installed but inactive.
    /// </summary>
    internal static async Task<ThxHeadsetStatus> CheckAsync(
        ushort productId,
        CancellationToken cancellationToken = default)
    {
        var status = Check(productId);
        if (status.ErrorCode != "ThxServiceMissing" ||
            !ThxAudioAdmission.HasService(ServiceName))
        {
            return status;
        }

        using var start = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        start.CancelAfter(TimeSpan.FromSeconds(2));
        try
        {
            await ThxAudioAdmission
                .EnsureServiceRunningAsync(ServiceName, start.Token)
                .ConfigureAwait(false);
        }
        catch (Exception exception) when (
            exception is OperationCanceledException or InvalidOperationException or
                System.ComponentModel.Win32Exception or UnauthorizedAccessException)
        {
        }

        status = Check(productId);
        return status.ErrorCode == "ThxServiceMissing"
            ? new ThxHeadsetStatus(ThxHeadsetAvailability.InstalledButInactive, "ThxServiceStopped")
            : status;
    }

    internal static ThxHeadsetStatus Check(ushort productId, ThxRebootMarkerStore rebootMarker)
    {
        var profile = GetProfile(productId);
        if (profile is null)
        {
            return new(ThxHeadsetAvailability.UnsupportedDevice, "ThxHeadsetUnsupported");
        }

        if (rebootMarker.IsPending(productId))
        {
            return new(
                ThxHeadsetAvailability.ServiceMissing,
                "ThxRebootRequired",
                RebootRequired: true);
        }

        // V4 does not expose a stable service name in the payload we have. Its
        // native client is the reliable gate; a missing APO shows up when the
        // helper initializes and the card keeps the installer entry available.
        var serviceRunning = profile.NativeVersion == ThxNativeVersion.V3 &&
            ThxAudioAdmission.HasRunningService(ServiceName);
        return Evaluate(profile, serviceRunning, HasVerifiedNativeClient(profile));
    }

    /// <summary>
    /// The native stack only proves the install once it returns a state. Until
    /// then a pending reboot marker must survive the service/native file checks.
    /// </summary>
    internal static void ClearRebootMarker() => RebootMarker.Clear();

    internal static ThxHeadsetStatus Evaluate(ushort productId, bool serviceRunning, bool nativePresent)
    {
        var profile = GetProfile(productId);
        return profile is null
            ? new(ThxHeadsetAvailability.UnsupportedDevice, "ThxHeadsetUnsupported")
            : Evaluate(profile, serviceRunning, nativePresent);
    }

    internal static ThxHeadsetStatus Evaluate(
        ThxHeadsetProfile profile,
        bool serviceRunning,
        bool nativePresent)
    {
        if (profile.NativeVersion == ThxNativeVersion.V3 && !serviceRunning)
        {
            return new(ThxHeadsetAvailability.ServiceMissing, "ThxServiceMissing");
        }
        if (!nativePresent)
        {
            return new(ThxHeadsetAvailability.NativeMissing, "ThxNativeMissing");
        }
        return new(ThxHeadsetAvailability.Available, null);
    }

    internal static bool HasVerifiedNativeClient(ThxHeadsetProfile profile)
    {
        try
        {
            using var stream = new FileStream(
                NativeDllPath(profile), FileMode.Open, FileAccess.Read, FileShare.Read);
            return stream.Length == profile.NativeDllLength &&
                CryptographicOperations.FixedTimeEquals(
                    Convert.FromHexString(profile.NativeDllSha256),
                    SHA256.HashData(stream));
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or CryptographicException or ArgumentException)
        {
            return false;
        }
    }

    internal static async Task<ThxHeadsetStatus> InstallComponentsAsync(
        ushort productId,
        CancellationToken cancellationToken = default)
    {
        var profile = GetProfile(productId);
        if (profile is null)
        {
            return new(ThxHeadsetAvailability.UnsupportedDevice, "ThxHeadsetUnsupported");
        }

        if (profile.NativeVersion == ThxNativeVersion.V3)
        {
            if (await ResolveInstallerAsync(profile, cancellationToken)
                .ConfigureAwait(false) is not { } installer)
            {
                return new(ThxHeadsetAvailability.ServiceMissing, "ThxInstallerUnavailable");
            }
            var preset = await ResolvePresetInstallerAsync(profile, cancellationToken)
                .ConfigureAwait(false);
            var installPreset = preset is not null &&
                ShouldInstallPreset(ReadInstalledPresetVersion(), preset.Version);
            // A live VSSrv does not prove this model's APO is bound: another THX
            // headset can own the service. Always run the pinned per-model package;
            // a leftover service is repaired instead of treated as complete.
            var install = await ThxComponentInstaller.InstallAsync(
                installer,
                cancellationToken,
                repair: ThxAudioAdmission.HasService(ServiceName),
                startService: ServiceName,
                extra: installPreset ? preset : null).ConfigureAwait(false);
            if (install.Error is not null)
            {
                return new(ThxHeadsetAvailability.ServiceMissing, install.Error);
            }

            // The native DLL is a plain file. Stage it even when the service still
            // needs a reboot, so one click never leaves a second install step.
            var nativeError = await InstallNativeFileAsync(profile, cancellationToken)
                .ConfigureAwait(false);
            var running = await ThxAudioAdmission
                .EnsureServiceRunningAsync(ServiceName, cancellationToken)
                .ConfigureAwait(false);
            return TrackInstallResult(productId, CombineInstallResult(
                install.RebootRequired ||
                    (installPreset && !install.AlreadyInstalled),
                nativeError,
                running,
                ThxAudioAdmission.HasService(ServiceName)));
        }

        return TrackInstallResult(
            productId,
            await InstallNativeComponentAsync(productId, cancellationToken).ConfigureAwait(false));
    }

    private static ThxHeadsetStatus TrackInstallResult(ushort productId, ThxHeadsetStatus status)
    {
        if (status.RebootRequired)
        {
            RebootMarker.MarkPending(productId);
        }
        else if (status.Availability == ThxHeadsetAvailability.Available)
        {
            RebootMarker.Clear();
        }

        return status;
    }

    internal static ThxHeadsetStatus CombineInstallResult(
        bool rebootRequired,
        string? nativeError,
        bool serviceRunning,
        bool serviceRegistered)
    {
        if (nativeError is not null)
        {
            return new(ThxHeadsetAvailability.NativeMissing, nativeError);
        }
        if (rebootRequired)
        {
            return new(
                ThxHeadsetAvailability.ServiceMissing,
                "ThxRebootRequired",
                RebootRequired: true);
        }
        if (serviceRunning)
        {
            return new(ThxHeadsetAvailability.Available, null);
        }
        return serviceRegistered
            ? new(
                ThxHeadsetAvailability.ServiceMissing,
                "ThxRebootRequired",
                RebootRequired: true)
            : new(ThxHeadsetAvailability.ServiceMissing, "ThxServiceMissing");
    }

    internal static async Task<ThxHeadsetStatus> InstallNativeComponentAsync(
        ushort productId,
        CancellationToken cancellationToken = default)
    {
        var profile = GetProfile(productId);
        if (profile is null)
        {
            return new(ThxHeadsetAvailability.UnsupportedDevice, "ThxHeadsetUnsupported");
        }

        var error = await InstallNativeFileAsync(profile, cancellationToken).ConfigureAwait(false);
        return error is null
            ? Check(productId)
            : new(ThxHeadsetAvailability.NativeMissing, error);
    }

    private static async Task<string?> InstallNativeFileAsync(
        ThxHeadsetProfile profile,
        CancellationToken cancellationToken)
    {
        if (HasVerifiedNativeClient(profile))
        {
            return null;
        }

        var destination = NativeDllPath(profile);
        var directory = Path.GetDirectoryName(destination)
            ?? throw new InvalidOperationException("The THX component path has no directory.");
        var tempDirectory = AppDataPaths.Combine("Temp");
        Directory.CreateDirectory(tempDirectory);
        var temporary = Path.Combine(
            tempDirectory,
            $"{profile.NativeDllName}.{Guid.NewGuid():N}.download");
        try
        {
            using var response = await ComponentClient.GetAsync(
                profile.NativeDllUri,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength is { } declaredLength &&
                declaredLength != profile.NativeDllLength)
            {
                return "ThxDownloadVerificationFailed";
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

            if (new FileInfo(temporary).Length != profile.NativeDllLength)
            {
                return "ThxDownloadVerificationFailed";
            }

            using (var stream = new FileStream(temporary, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                if (!string.Equals(
                        Convert.ToHexString(SHA256.HashData(stream)),
                        profile.NativeDllSha256,
                        StringComparison.OrdinalIgnoreCase))
                {
                    return "ThxDownloadVerificationFailed";
                }
            }

            Directory.CreateDirectory(directory);
            File.Move(temporary, destination, overwrite: true);
            return null;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return "ThxDownloadFailed";
        }
        catch (Exception exception) when (
            exception is HttpRequestException or IOException or UnauthorizedAccessException)
        {
            return "ThxDownloadFailed";
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

    private static IReadOnlyDictionary<ushort, ThxHeadsetProfile> BuildProfiles()
    {
        var profiles = new Dictionary<ushort, ThxHeadsetProfile>();
        AddV3(profiles, 0x051A, "Razer Nari Ultimate", 1306);
        AddV3(profiles, 0x051C, "Razer Nari", 1308);
        AddV3(profiles, 0x051E, "Razer Nari Essential", 1310);
        AddV3(profiles, 0x0520, "Razer USB Audio Controller", 1312);
        AddV3(profiles, 0x0521, "Razer Kraken Kitty Edition", 1313);
        AddV3(profiles, 0x0527, "Razer Kraken Ultimate", 1319);
        AddV3(profiles, 0x0528, "Razer BlackShark V2 Pro 2020", 1320);
        AddV3(profiles, 0x0529, "Razer USB Sound Card", 1321);
        AddV3(profiles, 0x052D, "Razer Kraken V3 Pro", 1325);
        AddV3(profiles, 0x0533, "Razer Kraken V3 HyperSense", 1331);
        AddV3(profiles, 0x053C, "Razer Barracuda", 1339);
        AddV3(profiles, 0x0549, "Razer Kraken V3", 1353);
        AddV3(profiles, 0x0554, "Razer Kraken Kitty V2 Pro", 1364);
        AddV3(profiles, 0x0555, "Razer BlackShark V2 Pro 2023", 1365);
        AddV3(profiles, 0x0565, "Razer BlackShark V2 HyperSpeed", 1390);
        AddV3(profiles, 0x0567, "Razer Kraken V4 Pro", 1383);
        AddV3(profiles, 0x0568, "Razer Kraken V4 Pro", 1383);
        AddV3(profiles, 0x056B, "Razer Kraken V4", 1387);
        AddV3(profiles, 0x056C, "Razer Kraken V4", 1387);
        AddV3(profiles, 0x056E, "Razer BlackShark V2 HyperSpeed", 1390);
        AddV3(profiles, 0x0583, "Razer Barracuda Pro", 1411);
        AddV3(profiles, 0x0584, "Razer Barracuda 2.4", 1412);

        AddV4(profiles, 0x0576, "Razer BlackShark V3 Pro", V4Dll113Name, V4Dll113Uri,
            V4Dll113Sha256, "razer_blacksharkv3pro_pc", "headphones_game", 1398);
        AddV4(profiles, 0x0579, "Razer BlackShark V3", V4Dll113Name, V4Dll113Uri,
            V4Dll113Sha256, "razer_blacksharkv3_pc", "headphones_game", 1401);
        AddV4(profiles, 0x057A, "Razer BlackShark V3", V4Dll113Name, V4Dll113Uri,
            V4Dll113Sha256, "razer_blacksharkv3_pc", "headphones_game", 1401);
        AddV4(profiles, 0x0587, "Razer Kraken Kitty V3 Pro", V4Dll112Name, V4Dll112Uri,
            V4Dll112Sha256, "razer_krakenkittyv3pro", "headphones_game", 1415);
        AddV4(profiles, 0x0593, "Razer Barracuda V2 Pro", V4Dll112Name, V4Dll112Uri,
            V4Dll112Sha256, "razer_barracudav2pro", "headphones", 1427);
        AddConsoleProfiles(profiles);
        return profiles;
    }

    private static void AddV3(
        IDictionary<ushort, ThxHeadsetProfile> profiles,
        ushort productId,
        string name,
        int installerPage) =>
        profiles[productId] = new ThxHeadsetProfile(
            productId,
            name,
            ThxNativeVersion.V3,
            installerPage,
            null,
            null,
            V3DllName,
            V3DllUri,
            V3DllLength,
            V3DllSha256,
            HasSidetone: true);

    private static void AddV4(
        IDictionary<ushort, ThxHeadsetProfile> profiles,
        ushort productId,
        string name,
        string dllName,
        string dllUri,
        string dllSha256,
        string v4ProductId,
        string v4EndpointId,
        int installerPage,
        long nativeDllLength = V4DllLength) =>
        profiles[productId] = new ThxHeadsetProfile(
            productId,
            name,
            ThxNativeVersion.V4,
            installerPage,
            v4ProductId,
            v4EndpointId,
            dllName,
            dllUri,
            nativeDllLength,
            dllSha256,
            HasSidetone: false);
}
