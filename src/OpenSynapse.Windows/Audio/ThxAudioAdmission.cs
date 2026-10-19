using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Microsoft.Win32;
using OpenSynapse.Core.Audio;

namespace OpenSynapse.Windows.Audio;

internal sealed record ThxAudioProfile(
    string ModelNumber,
    string ProductName,
    string ProductId,
    string UsbProductId,
    string ServiceName,
    int SpeakerBandCount,
    IReadOnlyDictionary<string, double[]> SpeakerFactoryCurves,
    IReadOnlyDictionary<string, double[]> HeadphoneFactoryCurves);

internal sealed record ThxAudioAdmissionResult(
    ThxAudioStatus Status,
    ThxAudioProfile? Profile = null);

internal static class ThxAudioAdmission
{
    internal const string NativeDllName = "ThxV4Native_v1.1.1.0.dll";
    internal const string NativeDllUri =
        "https://app-assets.razer.com/files/synapse/devices/common/THX/ThxV4Native_v1.1.1.0.dll";
    internal const long NativeDllLength = 13_684_872;
    internal const string NativeDllSha256 =
        "8E8F6923624762935D70B72F58BEC00808DB84DF740340047919B53331130A0A";
    private const string BiosKey = @"HARDWARE\DESCRIPTION\System\BIOS";
    private const string SystemInformationKey =
        @"SYSTEM\CurrentControlSet\Control\SystemInformation";
    private static readonly int[] Frequencies = [31, 63, 125, 250, 500, 1000, 2000, 4000, 8000, 16000];

    private static readonly IReadOnlyDictionary<string, double[]> HeadphoneCurves =
        new Dictionary<string, double[]>(StringComparer.Ordinal)
        {
            ["game"] = [-5, -5, -4, -3, -1, 2, 4, 4, 3, 2],
            ["movie"] = [4, 4, 3, -4, -5, -1, 3, 5, 3, 2],
            ["music"] = [1, 1, 1, 0, 0, 2, 3, 3, 2, 1],
            ["voice"] = [-12, -6, 4, 4, 4, 3, 0, 2, 4, -12],
            ["custom"] = [0, 0, 0, 0, 0, 0, 0, 0, 0, 0],
        };

    private static readonly ThxAudioProfile[] Profiles =
    [
        new(
            "RZ09-0581",
            "Blade 16 - RZ09-0581",
            "rznb12",
            "02E0",
            "thx_rznb12_service",
            7,
            new Dictionary<string, double[]>(StringComparer.Ordinal)
            {
                ["game"] = [1, 2, 4, 5, 1, -1, -4],
                ["movie"] = [3, -3, -4, 4, 5, 4, 2],
                ["music"] = [2, 0, 0, 3, 4, 3, 1],
                ["voice"] = [-4, 0, 3, 6, 5, 1, -6],
                ["custom"] = [0, 0, 0, 0, 0, 0, 0],
            },
            HeadphoneCurves),
        new(
            "RZ09-0528",
            "Blade 16 - RZ09-0528",
            "rznb11",
            "02C6",
            "thx_rznb11_service",
            7,
            new Dictionary<string, double[]>(StringComparer.Ordinal)
            {
                ["game"] = [-1, 1, 3, 4, 0, -3, -6],
                ["movie"] = [2, -4, -5, 3, 4, 3, -1],
                ["music"] = [1, -1, -1, 2, 3, 2, 0],
                ["voice"] = [-5, -1, 2, 5, 4, 0, -7],
                ["custom"] = [0, 0, 0, 0, 0, 0, 0],
            },
            HeadphoneCurves),
        new(
            "RZ09-0530",
            "Blade 14 - RZ09-0530",
            "rznb11",
            "02C5",
            "thx_rznb11_service",
            7,
            new Dictionary<string, double[]>(StringComparer.Ordinal)
            {
                ["game"] = [-1, 2, 3, 4, 0, -2, -4],
                ["movie"] = [2, -4, -4, 3, 4, 3, 0],
                ["music"] = [2, -3, -1, 2, 3, 2, 1],
                ["voice"] = [-4, -1, 2, 5, 4, -1, -6],
                ["custom"] = [0, 0, 0, 0, 0, 0, 0],
            },
            HeadphoneCurves),
    ];

    internal static IReadOnlyList<int> EqualizerFrequencies => Frequencies;

    internal static bool IsSupportedHostModel()
    {
        var model = ReadHostModel();
        return Profiles.Any(profile =>
            string.Equals(profile.ProductName, model, StringComparison.Ordinal));
    }

    internal static string NativeDllPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Razer",
        "RazerAppEngine",
        "User Data",
        "Apps",
        "Common",
        "thxv4common",
        NativeDllName);

    internal static ThxAudioAdmissionResult Check()
    {
        try
        {
            return CheckCore();
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            return new(new ThxAudioStatus(
                ThxAudioAvailability.ComponentsUnavailable,
                $"ThxAdmissionFailed:{exception.Message}",
                null));
        }
    }

    private static ThxAudioAdmissionResult CheckCore()
    {
        var model = ReadHostModel();
        var profile = Profiles.FirstOrDefault(candidate =>
            string.Equals(candidate.ProductName, model, StringComparison.Ordinal));
        if (profile is null)
        {
            return new(new ThxAudioStatus(
                ThxAudioAvailability.UnsupportedModel,
                model is null ? "ThxModelUnknown" : "ThxModelUnsupported",
                null));
        }

        if (!HasUsbIdentity(profile))
        {
            return new(new ThxAudioStatus(
                ThxAudioAvailability.ComponentsUnavailable,
                "ThxUsbMissing",
                null));
        }

        if (!HasRunningService(profile.ServiceName))
        {
            return new(new ThxAudioStatus(
                ThxAudioAvailability.ComponentsUnavailable,
                "ThxServiceMissing",
                null));
        }

        if (!HasVerifiedNativeClient())
        {
            return new(new ThxAudioStatus(
                ThxAudioAvailability.ComponentsUnavailable,
                "ThxNativeMissing",
                null));
        }

        if (IsCompetingControllerRunning())
        {
            return new(new ThxAudioStatus(
                ThxAudioAvailability.ControllerActive,
                "ThxControllerActive",
                null));
        }

        return new(new ThxAudioStatus(ThxAudioAvailability.Available, null, null), profile);
    }

    internal static IReadOnlyList<double> FactoryCurve(ThxAudioProfile profile, ThxAudioEndpoint endpoint, string presetId)
    {
        var source = endpoint == ThxAudioEndpoint.Speakers
            ? profile.SpeakerFactoryCurves
            : profile.HeadphoneFactoryCurves;
        return source.TryGetValue(presetId, out var gains) ? gains : [];
    }

    internal static IReadOnlyList<ThxAudioBand> ToBands(
        ThxAudioProfile profile,
        ThxAudioEndpoint endpoint,
        IReadOnlyList<double> nativeGains)
    {
        var first = endpoint == ThxAudioEndpoint.Speakers && profile.SpeakerBandCount == 7 ? 3 : 0;
        var count = endpoint == ThxAudioEndpoint.Speakers ? profile.SpeakerBandCount : 10;
        return Enumerable.Range(0, count)
            .Select(index => new ThxAudioBand(
                Frequencies[index + first],
                index + first < nativeGains.Count ? nativeGains[index + first] : 0))
            .ToArray();
    }

    internal static IReadOnlyList<double> ToNativeGains(
        ThxAudioProfile profile,
        ThxAudioEndpoint endpoint,
        IReadOnlyList<double> visibleGains) =>
        endpoint == ThxAudioEndpoint.Speakers && profile.SpeakerBandCount == 7
            ? [0, 0, 0, .. visibleGains]
            : [.. visibleGains];

    private static string? ReadHostModel()
    {
        using var localMachine = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
        using var bios = localMachine.OpenSubKey(BiosKey);
        using var system = localMachine.OpenSubKey(SystemInformationKey);
        var biosManufacturer = bios?.GetValue("SystemManufacturer") as string;
        var biosProduct = bios?.GetValue("SystemProductName") as string;
        var systemManufacturer = system?.GetValue("SystemManufacturer") as string;
        var systemProduct = system?.GetValue("SystemProductName") as string;
        return biosManufacturer == "Razer" &&
               systemManufacturer == "Razer" &&
               string.Equals(biosProduct, systemProduct, StringComparison.Ordinal)
            ? biosProduct
            : null;
    }

    private static bool HasUsbIdentity(ThxAudioProfile profile)
    {
        using var localMachine = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
        using var key = localMachine.OpenSubKey(
            $@"SYSTEM\CurrentControlSet\Enum\USB\VID_1532&PID_{profile.UsbProductId}");
        return key is not null;
    }

    private static bool HasVerifiedNativeClient()
    {
        var path = NativeDllPath;
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
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

    private static bool IsCompetingControllerRunning()
    {
        foreach (var name in new[] { "RazerAppEngine", "Razer Synapse Service", "RazerSynapseService", "THXSpatialAudioClient" })
        {
            using var process = Process.GetProcessesByName(name).FirstOrDefault();
            if (process is not null)
            {
                return true;
            }
        }
        return false;
    }

    private static bool HasRunningService(string serviceName)
    {
        var manager = NativeMethods.OpenSCManager(null, null, NativeMethods.ScManagerConnect);
        if (manager == IntPtr.Zero)
        {
            return false;
        }
        try
        {
            var service = NativeMethods.OpenService(manager, serviceName, NativeMethods.ServiceQueryStatus);
            if (service == IntPtr.Zero)
            {
                return false;
            }
            try
            {
                var status = new ServiceStatusProcess();
                return NativeMethods.QueryServiceStatusEx(
                    service,
                    0,
                    ref status,
                    Marshal.SizeOf<ServiceStatusProcess>(),
                    out _) && status.CurrentState == NativeMethods.ServiceRunning;
            }
            finally
            {
                NativeMethods.CloseServiceHandle(service);
            }
        }
        finally
        {
            NativeMethods.CloseServiceHandle(manager);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ServiceStatusProcess
    {
        public uint ServiceType;
        public uint CurrentState;
        public uint ControlsAccepted;
        public uint Win32ExitCode;
        public uint ServiceSpecificExitCode;
        public uint CheckPoint;
        public uint WaitHint;
        public uint ProcessId;
        public uint ServiceFlags;
    }

    private static class NativeMethods
    {
        internal const uint ScManagerConnect = 0x0001;
        internal const uint ServiceQueryStatus = 0x0004;
        internal const uint ServiceRunning = 0x00000004;

        [DllImport("advapi32.dll", EntryPoint = "OpenSCManagerW", SetLastError = true, CharSet = CharSet.Unicode)]
        internal static extern IntPtr OpenSCManager(string? machineName, string? databaseName, uint access);

        [DllImport("advapi32.dll", EntryPoint = "OpenServiceW", SetLastError = true, CharSet = CharSet.Unicode)]
        internal static extern IntPtr OpenService(IntPtr manager, string serviceName, uint access);

        [DllImport("advapi32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool QueryServiceStatusEx(
            IntPtr service,
            int infoLevel,
            ref ServiceStatusProcess status,
            int bufferSize,
            out int bytesNeeded);

        [DllImport("advapi32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool CloseServiceHandle(IntPtr handle);
    }
}
