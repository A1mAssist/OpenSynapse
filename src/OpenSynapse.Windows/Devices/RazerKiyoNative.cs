using System.Runtime.InteropServices;

namespace OpenSynapse.Windows.Devices;

/// <summary>
/// Thin P/Invoke wrapper for Razer's camera DLL. The DLL is shipped with Razer
/// Synapse and is never redistributed with OpenSynapse; callers must locate the
/// copy the user already has. Export signatures come from the middleware's
/// <c>ConfigureFFI</c> tables in <c>rzCamKiyo</c>, <c>rzCamKiyoPro</c>,
/// <c>rzCamKiyoProUltra</c>, and <c>rzCamKimberly</c>. All available DLLs are
/// x64, so the Windows x64 calling convention applies to every export.
/// </summary>
public sealed class RazerKiyoNative : IDisposable
{
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate IntPtr GetStringDelegate();

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate IntPtr GetStringForDelegate([MarshalAs(UnmanagedType.LPStr)] string value);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate IntPtr GetStringForTwoDelegate(
        [MarshalAs(UnmanagedType.LPStr)] string first,
        [MarshalAs(UnmanagedType.LPStr)] string second);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate bool BoolDelegate();

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate bool BoolForStringDelegate([MarshalAs(UnmanagedType.LPStr)] string value);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate bool SetLongBoolDelegate(
        [MarshalAs(UnmanagedType.LPStr)] string containerId,
        long value,
        bool auto);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void FreeMallocDelegate(IntPtr pointer);

    private readonly IntPtr _handle;
    private readonly FreeMallocDelegate _freeMalloc;
    private readonly GetStringDelegate _getDllVersion;
    private readonly BoolForStringDelegate _deviceInitialize;
    private readonly BoolForStringDelegate _deviceTerminate;
    private readonly GetStringForDelegate? _getBrightness;
    private readonly GetStringForDelegate? _getContrast;
    private readonly GetStringForDelegate? _getSaturation;
    private readonly GetStringForDelegate? _getWhiteBalance;
    private readonly GetStringForDelegate? _getCameraFocus;
    private readonly GetStringForDelegate? _getCameraZoom;
    private readonly GetStringForDelegate? _getFirmwareVersion;
    private readonly GetStringForDelegate? _getSerialNumber;
    private readonly SetLongBoolDelegate? _setBrightness;
    private readonly SetLongBoolDelegate? _setContrast;
    private readonly SetLongBoolDelegate? _setSaturation;
    private readonly SetLongBoolDelegate? _setWhiteBalance;
    private readonly SetLongBoolDelegate? _setCameraFocus;
    private readonly SetLongBoolDelegate? _setCameraZoom;
    private readonly BoolForStringDelegate _startCamera;
    private readonly BoolForStringDelegate _stopCamera;
    private readonly BoolForStringDelegate _displayProperties;
    private readonly BoolForStringDelegate _closeProperties;
    private readonly BoolDelegate _openAudioProperties;
    private readonly BoolDelegate _enumerateCameraList;
    private readonly GetStringForTwoDelegate _getContainerIdFromDeviceId;
    private readonly GetStringForTwoDelegate _getChromiumDeviceId;

    private RazerKiyoNative(IntPtr handle, string dllName)
    {
        _handle = handle;
        DllName = dllName;
        _freeMalloc = LoadRequired<FreeMallocDelegate>("FreeMalloc");
        _getDllVersion = LoadRequired<GetStringDelegate>("GetDLLVersion");
        _deviceInitialize = LoadRequired<BoolForStringDelegate>("DeviceInitialize");
        _deviceTerminate = LoadRequired<BoolForStringDelegate>("DeviceTerminate");
        _getBrightness = LoadOptional<GetStringForDelegate>("GetBrightness");
        _getContrast = LoadOptional<GetStringForDelegate>("GetContrast");
        _getSaturation = LoadOptional<GetStringForDelegate>("GetSaturation");
        _getWhiteBalance = LoadOptional<GetStringForDelegate>("GetWhiteBalance");
        _getCameraFocus = LoadOptional<GetStringForDelegate>("GetCameraFocus");
        _getCameraZoom = LoadOptional<GetStringForDelegate>("GetCameraZoom");
        _getFirmwareVersion = LoadOptional<GetStringForDelegate>("GetFirmwareVersion");
        _getSerialNumber = LoadOptional<GetStringForDelegate>("GetSerialNumber");
        _setBrightness = LoadOptional<SetLongBoolDelegate>("SetBrightness");
        _setContrast = LoadOptional<SetLongBoolDelegate>("SetContrast");
        _setSaturation = LoadOptional<SetLongBoolDelegate>("SetSaturation");
        _setWhiteBalance = LoadOptional<SetLongBoolDelegate>("SetWhiteBalance");
        _setCameraFocus = LoadOptional<SetLongBoolDelegate>("SetCameraFocus");
        _setCameraZoom = LoadOptional<SetLongBoolDelegate>("SetCameraZoom");
        _startCamera = LoadRequired<BoolForStringDelegate>("StartCamera");
        _stopCamera = LoadRequired<BoolForStringDelegate>("StopCamera");
        _displayProperties = LoadRequired<BoolForStringDelegate>("DisplayCameraPropertiesDialog");
        _closeProperties = LoadRequired<BoolForStringDelegate>("CloseCameraPropertiesDialog");
        _openAudioProperties = LoadRequired<BoolDelegate>("OpenAudioProperties");
        _enumerateCameraList = LoadRequired<BoolDelegate>("EnumerateCameraList");
        _getContainerIdFromDeviceId = LoadRequired<GetStringForTwoDelegate>("GetContainerIdFromDeviceId");
        _getChromiumDeviceId = LoadRequired<GetStringForTwoDelegate>("GetChromiumDeviceId");

        Api =
            Flag(RazerKiyoApi.ReadBrightness, _getBrightness) |
            Flag(RazerKiyoApi.ReadContrast, _getContrast) |
            Flag(RazerKiyoApi.ReadSaturation, _getSaturation) |
            Flag(RazerKiyoApi.ReadWhiteBalance, _getWhiteBalance) |
            Flag(RazerKiyoApi.ReadFirmwareVersion, _getFirmwareVersion) |
            Flag(RazerKiyoApi.ReadSerialNumber, _getSerialNumber) |
            Flag(RazerKiyoApi.ReadCameraFocus, _getCameraFocus) |
            Flag(RazerKiyoApi.ReadCameraZoom, _getCameraZoom) |
            Flag(RazerKiyoApi.WriteBrightness, _setBrightness) |
            Flag(RazerKiyoApi.WriteContrast, _setContrast) |
            Flag(RazerKiyoApi.WriteSaturation, _setSaturation) |
            Flag(RazerKiyoApi.WriteWhiteBalance, _setWhiteBalance) |
            Flag(RazerKiyoApi.WriteCameraFocus, _setCameraFocus) |
            Flag(RazerKiyoApi.WriteCameraZoom, _setCameraZoom) |
            RazerKiyoApi.SettingsDialog;
    }

    public string DllName { get; }

    public RazerKiyoApi Api { get; }

    public static RazerKiyoNative? TryLoad(string dllName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dllName);
        var path = FindDll(dllName);
        if (path is null)
        {
            return null;
        }

        IntPtr handle = IntPtr.Zero;
        try
        {
            handle = NativeLibrary.Load(path);
            return new RazerKiyoNative(handle, dllName);
        }
        catch (Exception exception) when (IsLoadFailure(exception))
        {
            if (handle != IntPtr.Zero)
            {
                NativeLibrary.Free(handle);
            }

            return null;
        }
    }

    public string GetDllVersion() => ReadString(_getDllVersion());

    public bool DeviceInitialize(string containerId) => _deviceInitialize(containerId);

    public bool DeviceTerminate(string containerId) => _deviceTerminate(containerId);

    public string GetBrightness(string containerId) => ReadOptional(_getBrightness, containerId);

    public string GetContrast(string containerId) => ReadOptional(_getContrast, containerId);

    public string GetSaturation(string containerId) => ReadOptional(_getSaturation, containerId);

    public string GetWhiteBalance(string containerId) => ReadOptional(_getWhiteBalance, containerId);

    public string GetCameraFocus(string containerId) => ReadOptional(_getCameraFocus, containerId);

    public string GetCameraZoom(string containerId) => ReadOptional(_getCameraZoom, containerId);

    public string GetFirmwareVersion(string containerId) => ReadOptional(_getFirmwareVersion, containerId);

    public string GetSerialNumber(string containerId) => ReadOptional(_getSerialNumber, containerId);

    // Every DLL in this family reads r8b as the auto flag for the image setters,
    // even where the middleware's older ConfigureFFI table omits it.
    public bool SetBrightness(string containerId, long value) =>
        _setBrightness?.Invoke(containerId, value, false) ?? false;

    public bool SetContrast(string containerId, long value) =>
        _setContrast?.Invoke(containerId, value, false) ?? false;

    public bool SetSaturation(string containerId, long value) =>
        _setSaturation?.Invoke(containerId, value, false) ?? false;

    public bool SetWhiteBalance(string containerId, long value, bool auto) =>
        _setWhiteBalance?.Invoke(containerId, value, auto) ?? false;

    public bool SetCameraFocus(string containerId, long value, bool auto) =>
        _setCameraFocus?.Invoke(containerId, value, auto) ?? false;

    public bool SetCameraZoom(string containerId, long value, bool auto) =>
        _setCameraZoom?.Invoke(containerId, value, auto) ?? false;

    public bool StartCamera(string containerId) => _startCamera(containerId);

    public bool StopCamera(string containerId) => _stopCamera(containerId);

    public bool DisplayCameraPropertiesDialog(string containerId) => _displayProperties(containerId);

    public bool CloseCameraPropertiesDialog(string containerId) => _closeProperties(containerId);

    public bool OpenAudioProperties() => _openAudioProperties();

    public bool EnumerateCameraList() => _enumerateCameraList();

    public string GetContainerIdFromDeviceId(string deviceId, string origin) =>
        ReadString(_getContainerIdFromDeviceId(deviceId, origin));

    public string GetChromiumDeviceId(string containerId, string origin) =>
        ReadString(_getChromiumDeviceId(containerId, origin));

    public void Dispose()
    {
        if (_handle != IntPtr.Zero)
        {
            NativeLibrary.Free(_handle);
        }
    }

    private T LoadRequired<T>(string export) where T : Delegate
    {
        if (!NativeLibrary.TryGetExport(_handle, export, out var address))
        {
            throw new EntryPointNotFoundException(export);
        }

        return Marshal.GetDelegateForFunctionPointer<T>(address);
    }

    private T? LoadOptional<T>(string export) where T : Delegate =>
        NativeLibrary.TryGetExport(_handle, export, out var address)
            ? Marshal.GetDelegateForFunctionPointer<T>(address)
            : null;

    private static RazerKiyoApi Flag<T>(RazerKiyoApi flag, T? value) where T : Delegate =>
        value is null ? RazerKiyoApi.None : flag;

    private string ReadOptional(GetStringForDelegate? getter, string containerId) =>
        getter is null ? string.Empty : ReadString(getter(containerId));

    private string ReadString(IntPtr pointer)
    {
        if (pointer == IntPtr.Zero)
        {
            return string.Empty;
        }

        try
        {
            return Marshal.PtrToStringAnsi(pointer) ?? string.Empty;
        }
        finally
        {
            _freeMalloc(pointer);
        }
    }

    /// <summary>
    /// The middleware loads this from
    /// <c>%LOCALAPPDATA%\Razer\RazerAppEngine\User Data\Apps\Synapse</c>; fall back
    /// to the usual Razer install roots for older layouts.
    /// </summary>
    public static string? FindDll(string dllName)
    {
        if (string.IsNullOrWhiteSpace(dllName))
        {
            return null;
        }

        var candidates = new List<string>(12);
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (!string.IsNullOrWhiteSpace(local))
        {
            var synapseRoot = Path.Combine(
                local,
                "Razer",
                "RazerAppEngine",
                "User Data",
                "Apps",
                "Synapse");
            candidates.Add(Path.Combine(synapseRoot, dllName));
            if (Directory.Exists(synapseRoot))
            {
                try
                {
                    candidates.AddRange(Directory.EnumerateFiles(
                        synapseRoot,
                        dllName,
                        SearchOption.AllDirectories));
                }
                catch (Exception exception) when (
                    exception is IOException or UnauthorizedAccessException or ArgumentException)
                {
                }
            }
        }

        foreach (var root in new[]
                 {
                     Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                     Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                     Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                 })
        {
            if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
            {
                continue;
            }

            var razerRoot = Path.Combine(root, "Razer");
            if (!Directory.Exists(razerRoot))
            {
                continue;
            }

            try
            {
                candidates.AddRange(Directory.EnumerateFiles(razerRoot, dllName, SearchOption.AllDirectories));
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
            }
        }

        return candidates.FirstOrDefault(File.Exists);
    }

    private static bool IsLoadFailure(Exception exception) =>
        exception is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException or
            IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or
            MarshalDirectiveException;
}
