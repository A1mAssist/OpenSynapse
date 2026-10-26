using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace OpenSynapse.Windows.Audio;

/// <summary>
/// The V3 THX client is a flat C API keyed by the audio endpoint container ID.
/// It is only loaded inside OpenSynapse.ThxHelper; the WinUI process never maps it.
/// </summary>
internal sealed class ThxV3NativeClient : IDisposable
{
    private const uint LoadLibrarySearchSystem32 = 0x00000800;
    private const int MaximumJsonBytes = 64 * 1024;

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    private delegate bool ContainerBoolCall(nint containerId);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    private delegate bool ContainerSetBoolCall(nint containerId, [MarshalAs(UnmanagedType.I1)] bool value);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate nint ContainerPointerCall(nint containerId);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    private delegate bool ContainerSetStringCall(nint containerId, nint value);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int ContainerIntCall(nint containerId);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    private delegate bool ContainerSetIntCall(nint containerId, int value);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate nint EmptyPointerCall();

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void FreeMallocCall(nint value);

    private readonly nint _library;
    private readonly ContainerBoolCall _deviceInitialize;
    private readonly ContainerBoolCall _getSpatial;
    private readonly ContainerSetBoolCall _setSpatial;
    private readonly ContainerPointerCall _getPresetList;
    private readonly ContainerPointerCall _getPreset;
    private readonly ContainerSetStringCall _setPreset;
    private readonly ContainerPointerCall _getEqGains;
    private readonly ContainerSetStringCall _setEqGains;
    private readonly ContainerBoolCall _resetEqGains;
    private readonly ContainerBoolCall _getNormalization;
    private readonly ContainerSetBoolCall _setNormalization;
    private readonly ContainerIntCall _getNormalizationLevel;
    private readonly ContainerSetIntCall _setNormalizationLevel;
    private readonly ContainerBoolCall _getVocalClarity;
    private readonly ContainerSetBoolCall _setVocalClarity;
    private readonly ContainerIntCall _getVocalClarityLevel;
    private readonly ContainerSetIntCall _setVocalClarityLevel;
    private readonly ContainerBoolCall _getSidetone;
    private readonly ContainerSetBoolCall _setSidetone;
    private readonly ContainerIntCall _getSidetoneLevel;
    private readonly ContainerSetIntCall _setSidetoneLevel;
    private readonly EmptyPointerCall _getVersion;
    private readonly FreeMallocCall _freeMalloc;
    private bool _initialized;

    internal ThxV3NativeClient(string path, long expectedLength, string expectedSha256)
    {
        if (!VerifyFile(path, expectedLength, expectedSha256))
        {
            throw new InvalidOperationException("The THX V3 native component is missing or changed.");
        }

        _library = LoadLibraryExW(path, nint.Zero, LoadLibrarySearchSystem32);
        if (_library == nint.Zero)
        {
            throw new InvalidOperationException(
                $"The THX V3 native component could not be loaded (0x{Marshal.GetLastPInvokeError():X8}).");
        }

        _deviceInitialize = GetExport<ContainerBoolCall>("DeviceInitialize");
        _getSpatial = GetExport<ContainerBoolCall>("GetRenderSpatialProcessing");
        _setSpatial = GetExport<ContainerSetBoolCall>("SetRenderSpatialProcessing");
        _getPresetList = GetExport<ContainerPointerCall>("GetRenderPresetList");
        _getPreset = GetExport<ContainerPointerCall>("GetRenderPreset");
        _setPreset = GetExport<ContainerSetStringCall>("SetRenderPreset");
        _getEqGains = GetExport<ContainerPointerCall>("GetRenderEQGains");
        _setEqGains = GetExport<ContainerSetStringCall>("SetRenderEQGains");
        _resetEqGains = GetExport<ContainerBoolCall>("ResetRenderEQGains");
        _getNormalization = GetExport<ContainerBoolCall>("GetRenderNormalization");
        _setNormalization = GetExport<ContainerSetBoolCall>("SetRenderNormalization");
        _getNormalizationLevel = GetExport<ContainerIntCall>("GetRenderNormalizationLevel");
        _setNormalizationLevel = GetExport<ContainerSetIntCall>("SetRenderNormalizationLevel");
        _getVocalClarity = GetExport<ContainerBoolCall>("GetRenderVocalClarity");
        _setVocalClarity = GetExport<ContainerSetBoolCall>("SetRenderVocalClarity");
        _getVocalClarityLevel = GetExport<ContainerIntCall>("GetRenderVocalClarityLevel");
        _setVocalClarityLevel = GetExport<ContainerSetIntCall>("SetRenderVocalClarityLevel");
        _getSidetone = GetExport<ContainerBoolCall>("GetCaptureSidetone");
        _setSidetone = GetExport<ContainerSetBoolCall>("SetCaptureSidetone");
        _getSidetoneLevel = GetExport<ContainerIntCall>("GetCaptureSidetoneLevel");
        _setSidetoneLevel = GetExport<ContainerSetIntCall>("SetCaptureSidetoneLevel");
        _getVersion = GetExport<EmptyPointerCall>("GetDLLVersion");
        _freeMalloc = GetExport<FreeMallocCall>("FreeMalloc");
    }

    internal string Version => ReadAndFree(_getVersion());

    internal void Initialize(string containerId)
    {
        if (_initialized)
        {
            return;
        }
        using var container = new Utf8Argument(containerId);
        if (!_deviceInitialize(container.Pointer))
        {
            throw new InvalidOperationException("The THX V3 device could not be initialized.");
        }
        _initialized = true;
    }

    internal bool GetSpatial(string containerId) => CallBool(containerId, _getSpatial);
    internal bool SetSpatial(string containerId, bool enabled) => CallSetBool(containerId, _setSpatial, enabled);
    internal string GetPresetList(string containerId) => GetJson(containerId, _getPresetList);
    internal string GetPreset(string containerId) => GetJson(containerId, _getPreset);
    internal string GetEqGains(string containerId) => GetJson(containerId, _getEqGains);

    internal bool SetPreset(string containerId, string preset) => CallSetString(containerId, _setPreset, preset);
    internal bool SetEqGains(string containerId, string gainsJson) => CallSetString(containerId, _setEqGains, gainsJson);
    internal bool ResetEqGains(string containerId) => CallBool(containerId, _resetEqGains);
    internal bool GetNormalization(string containerId) => CallBool(containerId, _getNormalization);
    internal bool SetNormalization(string containerId, bool enabled) => CallSetBool(containerId, _setNormalization, enabled);
    internal int GetNormalizationLevel(string containerId) => CallInt(containerId, _getNormalizationLevel);
    internal bool SetNormalizationLevel(string containerId, int level) => CallSetInt(containerId, _setNormalizationLevel, level);
    internal bool GetVocalClarity(string containerId) => CallBool(containerId, _getVocalClarity);
    internal bool SetVocalClarity(string containerId, bool enabled) => CallSetBool(containerId, _setVocalClarity, enabled);
    internal int GetVocalClarityLevel(string containerId) => CallInt(containerId, _getVocalClarityLevel);
    internal bool SetVocalClarityLevel(string containerId, int level) => CallSetInt(containerId, _setVocalClarityLevel, level);
    internal bool GetSidetone(string containerId) => CallBool(containerId, _getSidetone);
    internal bool SetSidetone(string containerId, bool enabled) => CallSetBool(containerId, _setSidetone, enabled);
    internal int GetSidetoneLevel(string containerId) => CallInt(containerId, _getSidetoneLevel);
    internal bool SetSidetoneLevel(string containerId, int level) => CallSetInt(containerId, _setSidetoneLevel, level);

    public void Dispose()
    {
        // The helper exits immediately after the request; terminating the device
        // here can race the DLL's worker threads.
    }

    private static bool CallBool(string containerId, ContainerBoolCall call)
    {
        using var container = new Utf8Argument(containerId);
        return call(container.Pointer);
    }

    private static bool CallSetBool(string containerId, ContainerSetBoolCall call, bool value)
    {
        using var container = new Utf8Argument(containerId);
        return call(container.Pointer, value);
    }

    private static bool CallSetString(string containerId, ContainerSetStringCall call, string value)
    {
        using var container = new Utf8Argument(containerId);
        using var argument = new Utf8Argument(value);
        return call(container.Pointer, argument.Pointer);
    }

    private static int CallInt(string containerId, ContainerIntCall call)
    {
        using var container = new Utf8Argument(containerId);
        return call(container.Pointer);
    }

    private static bool CallSetInt(string containerId, ContainerSetIntCall call, int value)
    {
        using var container = new Utf8Argument(containerId);
        return call(container.Pointer, value);
    }

    private string GetJson(string containerId, ContainerPointerCall call)
    {
        using var container = new Utf8Argument(containerId);
        return ReadAndFree(call(container.Pointer));
    }

    private string ReadAndFree(nint pointer)
    {
        if (pointer == nint.Zero)
        {
            throw new InvalidDataException("The THX V3 component returned no value.");
        }

        try
        {
            var length = 0;
            while (length < MaximumJsonBytes && Marshal.ReadByte(pointer, length) != 0)
            {
                length++;
            }
            if (length == MaximumJsonBytes)
            {
                throw new InvalidDataException("The THX V3 value exceeds 64 KiB.");
            }

            var bytes = new byte[length];
            Marshal.Copy(pointer, bytes, 0, length);
            return new UTF8Encoding(false, true).GetString(bytes);
        }
        finally
        {
            _freeMalloc(pointer);
        }
    }

    private static bool VerifyFile(string path, long expectedLength, string expectedSha256)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            return stream.Length == expectedLength &&
                CryptographicOperations.FixedTimeEquals(
                    Convert.FromHexString(expectedSha256),
                    SHA256.HashData(stream));
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or CryptographicException or ArgumentException)
        {
            return false;
        }
    }

    private T GetExport<T>(string name) where T : Delegate
    {
        var address = GetProcAddress(_library, name);
        if (address == nint.Zero)
        {
            throw new EntryPointNotFoundException($"The THX V3 component does not export {name}.");
        }
        return Marshal.GetDelegateForFunctionPointer<T>(address);
    }

    [DllImport("kernel32.dll", EntryPoint = "LoadLibraryExW", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern nint LoadLibraryExW(string fileName, nint file, uint flags);

    [DllImport("kernel32.dll", EntryPoint = "GetProcAddress", SetLastError = true, CharSet = CharSet.Ansi)]
    private static extern nint GetProcAddress(nint module, string procedureName);

    private sealed class Utf8Argument : IDisposable
    {
        private nint _pointer;

        internal Utf8Argument(string value)
        {
            ArgumentNullException.ThrowIfNull(value);
            _pointer = Marshal.StringToCoTaskMemUTF8(value);
        }

        internal nint Pointer => _pointer;

        public void Dispose()
        {
            var pointer = _pointer;
            _pointer = nint.Zero;
            if (pointer != nint.Zero)
            {
                Marshal.FreeCoTaskMem(pointer);
            }
        }
    }
}
