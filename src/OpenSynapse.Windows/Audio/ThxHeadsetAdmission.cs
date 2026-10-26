using System.Security.Cryptography;
using OpenSynapse.Core.Audio;

namespace OpenSynapse.Windows.Audio;

internal enum ThxHeadsetAvailability
{
    UnsupportedDevice,
    ServiceMissing,
    NativeMissing,
    Available,
}

internal enum ThxNativeVersion
{
    V3,
    V4,
}

internal sealed record ThxHeadsetStatus(ThxHeadsetAvailability Availability, string? ErrorCode);

internal sealed record ThxHeadsetProfile(
    ushort ProductId,
    string Name,
    ThxNativeVersion NativeVersion,
    string? InstallerUri,
    string? V4ProductId,
    string? V4EndpointId,
    string NativeDllName,
    string NativeDllUri,
    long NativeDllLength,
    string NativeDllSha256,
    bool HasSidetone,
    string? GameProfilesUri = null,
    string? PatcherUri = null)
{
    internal string NativeVersionName => NativeVersion == ThxNativeVersion.V3 ? "v3" : "v4";
}

/// <summary>
/// THX for headsets is an APO bound to the USB audio endpoint, the VSSrv service
/// (V3) or the V4 audio stack, and a native control DLL. The APO and the service
/// come from the per-model Razer THX installer; the DLL is downloaded here.
/// </summary>
internal static class ThxHeadsetAdmission
{
    internal const string ServiceName = "VSSrv";

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

    internal static ThxHeadsetProfile? GetProfile(ushort productId) =>
        Profiles.TryGetValue(productId, out var profile) ? profile : null;

    internal static string GetServiceInstallerUri(ushort productId) =>
        GetProfile(productId)?.InstallerUri
        ?? throw new ArgumentOutOfRangeException(nameof(productId), "No THX installer is pinned for this headset.");

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
    {
        var profile = GetProfile(productId);
        if (profile is null)
        {
            return new(ThxHeadsetAvailability.UnsupportedDevice, "ThxHeadsetUnsupported");
        }

        // V4 does not expose a stable service name in the payload we have. Its
        // native client is the reliable gate; a missing APO shows up when the
        // helper initializes and the card keeps the installer entry available.
        var serviceRunning = profile.NativeVersion == ThxNativeVersion.V3 &&
            ThxAudioAdmission.HasRunningService(ServiceName);
        return Evaluate(profile, serviceRunning, HasVerifiedNativeClient(profile));
    }

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

    internal static async Task<ThxHeadsetStatus> InstallNativeComponentAsync(
        ushort productId,
        CancellationToken cancellationToken = default)
    {
        var profile = GetProfile(productId);
        if (profile is null)
        {
            return new(ThxHeadsetAvailability.UnsupportedDevice, "ThxHeadsetUnsupported");
        }

        var destination = NativeDllPath(profile);
        var directory = Path.GetDirectoryName(destination)
            ?? throw new InvalidOperationException("The THX component path has no directory.");
        var temporary = Path.Combine(
            Path.GetTempPath(),
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

            if (new FileInfo(temporary).Length != profile.NativeDllLength)
            {
                return new(ThxHeadsetAvailability.NativeMissing, "ThxDownloadVerificationFailed");
            }

            using (var stream = new FileStream(temporary, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                if (!string.Equals(
                        Convert.ToHexString(SHA256.HashData(stream)),
                        profile.NativeDllSha256,
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

    private static IReadOnlyDictionary<ushort, ThxHeadsetProfile> BuildProfiles()
    {
        var profiles = new Dictionary<ushort, ThxHeadsetProfile>();
        AddV3(profiles, 0x051A, "Razer Nari Ultimate",
            "https://app-assets.razer.com/files/synapse/devices/1306/1.0.1.7/THX-051A-NariUltimate-v3.2.3.0.exe");
        AddV3(profiles, 0x051C, "Razer Nari",
            "https://app-assets.razer.com/files/synapse/devices/1308/1.0.1.6/THX-051C-Nari-v3.2.3.0.exe");
        AddV3(profiles, 0x051E, "Razer Nari Essential",
            "https://app-assets.razer.com/files/synapse/devices/1310/1.0.1.7/THX-051E-NariEssential-v3.2.3.0.exe");
        AddV3(profiles, 0x0520, "Razer USB Audio Controller",
            "https://app-assets.razer.com/files/synapse/devices/1312/1.0.0.6/THX-0520-KrakenTE-v3.2.3.0.exe");
        AddV3(profiles, 0x0521, "Razer Kraken Kitty Edition",
            "https://app-assets.razer.com/files/synapse/devices/1313/1.0.1.3/THX-0521-KrakenKitty-v3.2.3.0.exe");
        AddV3(profiles, 0x0527, "Razer Kraken Ultimate",
            "https://app-assets.razer.com/files/synapse/devices/1319/1.0.1.3/THX-0527-KrakenUltimate-v3.2.3.0.exe");
        AddV3(profiles, 0x0528, "Razer BlackShark V2 Pro 2020",
            "https://app-assets.razer.com/files/synapse/devices/1320/1.0.4.2/THX-0528-BlackSharkV2Pro2020-v3.2.3.0.exe");
        AddV3(profiles, 0x0529, "Razer USB Sound Card",
            "https://app-assets.razer.com/files/synapse/devices/1321/1.0.1.10/THX-0529-BlackSharkV2RazerUSBSoundCard-v3.2.3.0+1.exe");
        AddV3(profiles, 0x052D, "Razer Kraken V3 Pro",
            "https://app-assets.razer.com/files/synapse/devices/1324/1.0.0.5/THX-052C-KrakenV3Pro-v3.2.3.0.exe");
        AddV3(profiles, 0x0533, "Razer Kraken V3 HyperSense",
            "https://app-assets.razer.com/files/synapse/devices/1331/1.0.0.10/THX-0533-KrakenV3HyperSense-v3.2.3.0.exe");
        AddV3(profiles, 0x053C, "Razer Barracuda",
            "https://app-assets.razer.com/files/synapse/devices/1339/1.0.0.1/THX-053C-RazerBarracuda-v3.0.30.0-r1.exe");
        AddV3(profiles, 0x0549, "Razer Kraken V3",
            "https://app-assets.razer.com/files/synapse/devices/1353/1.0.0.8/THX-0549-KrakenV3-v3.2.3.0.exe");
        AddV3(profiles, 0x0554, "Razer Kraken Kitty V2 Pro",
            "https://app-assets.razer.com/files/synapse/devices/1364/1.0.1.4/THX-0554-KrakenKittyV2Pro-v3.2.3.0.exe");
        AddV3(profiles, 0x0555, "Razer BlackShark V2 Pro 2023",
            "https://app-assets.razer.com/files/synapse/devices/1365/1.0.0.16/THX-0555-BlackSharkV2Pro2023-v3.2.3.0.exe");
        AddV3(profiles, 0x0565, "Razer BlackShark V2 HyperSpeed",
            "https://app-assets.razer.com/files/synapse/devices/1390/1.0.0.11/THX-0565-BlackSharkV2HyperSpeed-v3.2.3.0.exe");
        AddV3(profiles, 0x0567, "Razer Kraken V4 Pro",
            "https://app-assets.razer.com/files/synapse/devices/1383/1.0.0.9/THX-0567-KrakenV4Pro-v3.2.5.0.exe");
        AddV3(profiles, 0x0568, "Razer Kraken V4 Pro",
            "https://app-assets.razer.com/files/synapse/devices/1383/1.0.0.9/THX-0568-KrakenV4Pro-v3.2.5.0.exe");
        AddV3(profiles, 0x056B, "Razer Kraken V4",
            "https://app-assets.razer.com/files/synapse/devices/1387/1.0.0.2/THX-056B-KrakenV4-v3.2.5.0.exe");
        AddV3(profiles, 0x056C, "Razer Kraken V4",
            "https://app-assets.razer.com/files/synapse/devices/1387/1.0.0.2/THX-056C-KrakenV4-v3.2.5.0.exe");
        AddV3(profiles, 0x056E, "Razer BlackShark V2 HyperSpeed",
            "https://app-assets.razer.com/files/synapse/devices/1390/1.0.0.11/THX-056E-BlackSharkV2HyperSpeed-v3.2.3.0.exe");
        AddV3(profiles, 0x0583, "Razer Barracuda Pro",
            "https://app-assets.razer.com/files/synapse/devices/1411/THXAPO-USB_BarracudaProRefresh-0583-mabel-t1r_v3.2.10.0-mssigned-a1234_20250121-w4_i1137.exe");
        AddV3(profiles, 0x0584, "Razer Barracuda 2.4",
            "https://app-assets.razer.com/files/synapse/devices/1412/THXAPO-USB_BarracudaRefresh-0584-mabel-t2r_v3.2.8.0-mssigned-a1227_20250121-w4_i1138.exe");

        AddV4(profiles, 0x0576, "Razer BlackShark V3 Pro", V4Dll113Name, V4Dll113Uri,
            V4Dll113Sha256, "razer_blacksharkv3pro_pc", "headphones_game",
            "https://app-assets.razer.com/files/synapse/devices/1398/THXAPO_razer_blacksharkv3pro_pc_v4.1.28.1057-75fb4b2-i1880n_MSSIGN-e6a07c7f.91eacce8.exe",
            "https://app-assets.razer.com/files/synapse/devices/1398/properties-updater/thx_razer_blacksharkv3pro_pc_properties_updater-v4.1.28-f19f37d9-Razer-4.1.28-prod.exe",
            "https://app-assets.razer.com/files/synapse/devices/1398/714-patcher/thx_razer_blacksharkv3pro_pc_v4.2.38-a844df5a-714_patcher_config_updater-v14-20260420.exe");
        AddV4(profiles, 0x0579, "Razer BlackShark V3", V4Dll113Name, V4Dll113Uri,
            V4Dll113Sha256, "razer_blacksharkv3_pc", "headphones_game",
            "https://app-assets.razer.com/files/synapse/devices/1401/THXAPO_razer_blacksharkv3_pc_v4.1.28.1054-75fb4b2-i1878n_MSSIGN-e6a07c7f.284bc2f0.exe",
            "https://app-assets.razer.com/files/synapse/devices/1401/properties-updater/thx_razer_blacksharkv3_pc_properties_updater-v4.1.28-f19f37d9-Razer-4.1.28-prod.exe",
            "https://app-assets.razer.com/files/synapse/devices/1401/714-patcher/thx_razer_blacksharkv3_pc_v4.2.38-a844df5a-714_patcher_config_updater-v14-20260420.exe");
        AddV4(profiles, 0x0587, "Razer Kraken Kitty V3 Pro", V4Dll112Name, V4Dll112Uri,
            V4Dll112Sha256, "razer_krakenkittyv3pro", "headphones_game",
            "https://app-assets.razer.com/files/synapse/devices/1415/THXAPO_razer_krakenkittyv3pro_v4.1.28.1060-75fb4b2-i1882n_MSSIGN-e6a07c7f.d1e76a8f.exe",
            "https://app-assets.razer.com/files/synapse/devices/1415/properties-updater/thx_razer_krakenkittyv3pro_properties_updater-v4.1.28-f19f37d9-Razer-4.1.28-prod.exe");
        AddV4(profiles, 0x0593, "Razer Barracuda V2 Pro", V4Dll112Name, V4Dll112Uri,
            V4Dll112Sha256, "razer_barracudav2pro", "headphones",
            "https://app-assets.razer.com/files/synapse/devices/1427/APO/THXAPO_razer_barracudav2pro_v4.2.41.2572-f517b01d-i3531ns_MSSIGN-4aa58b0e.dbf3dd66.exe",
            "https://app-assets.razer.com/files/synapse/devices/1427/properties-updater/thx_razer_barracudav2pro_properties_updater_v4.3.13-18360025_main.exe");
        return profiles;
    }

    private static void AddV3(
        IDictionary<ushort, ThxHeadsetProfile> profiles,
        ushort productId,
        string name,
        string installerUri) =>
        profiles[productId] = new ThxHeadsetProfile(
            productId,
            name,
            ThxNativeVersion.V3,
            installerUri,
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
        string installerUri,
        string gameProfilesUri,
        string? patcherUri = null) =>
        profiles[productId] = new ThxHeadsetProfile(
            productId,
            name,
            ThxNativeVersion.V4,
            installerUri,
            v4ProductId,
            v4EndpointId,
            dllName,
            dllUri,
            V4DllLength,
            dllSha256,
            HasSidetone: false,
            gameProfilesUri,
            patcherUri);
}
