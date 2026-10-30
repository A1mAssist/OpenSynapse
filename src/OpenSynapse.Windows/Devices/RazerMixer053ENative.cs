using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace OpenSynapse.Windows.Devices;

/// <summary>
/// Optional read-only bridge to Razer's 053E mixer DLL. The DLL is not
/// redistributed; when it is absent the provider is simply unavailable and the
/// existing HID subset remains authoritative. No method in this type writes a
/// mixer property.
/// </summary>
public sealed class RazerMixer053ENative : IDisposable
{
    public const string DllFileName = "RzNative_053E.dll";

    private const int PeakBufferLength = 8;
    private const int MaxDevices = 64;
    private static readonly TimeSpan InitializeTimeout = TimeSpan.FromSeconds(5);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate bool BoolDelegate();

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int GetDeviceCountDelegate(int dataFlow);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate IntPtr GetDeviceByIdDelegate(int dataFlow, int index);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate IntPtr PropertyControlDelegate(
        int id,
        int jackType,
        int dataFlow,
        int deviceState,
        [MarshalAs(UnmanagedType.LPStr)] string propertyName,
        IntPtr value,
        int valueLength,
        IntPtr extra,
        int extraLength,
        int isSet);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void FreeMallocDelegate(IntPtr pointer);

    private sealed record Exports(
        BoolDelegate Initialize,
        BoolDelegate Terminate,
        GetDeviceCountDelegate GetDeviceCount,
        GetDeviceByIdDelegate GetDeviceById,
        PropertyControlDelegate PropertyControl,
        FreeMallocDelegate FreeMalloc);

    private readonly IntPtr _handle;
    private readonly Exports? _exports;
    private readonly Action<string>? _diagnostic;
    private readonly object _nativeGate = new();
    private readonly object _initializeGate = new();
    private Task<bool>? _initializeTask;
    private Exception? _initializeError;
    private bool _initialized;
    private bool _disposed;

    private RazerMixer053ENative(IntPtr handle, string? dllPath, Exports? exports, Action<string>? diagnostic)
    {
        _handle = handle;
        DllPath = dllPath;
        _exports = exports;
        _diagnostic = diagnostic;
    }

    public bool IsAvailable => !_disposed && _handle != IntPtr.Zero && _exports is not null;

    public string? DllPath { get; }

    public static RazerMixer053ENative Load(Action<string>? diagnostic = null) =>
        Load(
            AppContext.BaseDirectory,
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            diagnostic);

    internal static RazerMixer053ENative Load(
        string? appDirectory,
        string? localAppData,
        Action<string>? diagnostic)
    {
        var path = FindDll(appDirectory, localAppData);
        if (path is null)
        {
            diagnostic?.Invoke("053E native DLL not found; using HID subset.");
            return new RazerMixer053ENative(IntPtr.Zero, null, null, diagnostic);
        }

        IntPtr handle = IntPtr.Zero;
        try
        {
            handle = NativeLibrary.Load(path);
            var exports = new Exports(
                LoadRequired<BoolDelegate>(handle, "Initialize"),
                LoadRequired<BoolDelegate>(handle, "Terminate"),
                LoadRequired<GetDeviceCountDelegate>(handle, "MixerSDKLib_GetDeviceCount"),
                LoadRequired<GetDeviceByIdDelegate>(handle, "MixerSDKLib_GetDeviceById"),
                LoadRequired<PropertyControlDelegate>(handle, "MixerSDKLib_PropertyControl"),
                LoadRequired<FreeMallocDelegate>(handle, "FreeMalloc"));
            return new RazerMixer053ENative(handle, path, exports, diagnostic);
        }
        catch (Exception exception) when (IsLoadFailure(exception))
        {
            if (handle != IntPtr.Zero)
            {
                NativeLibrary.Free(handle);
            }

            diagnostic?.Invoke($"053E native DLL unavailable ({path}): {exception.Message}");
            return new RazerMixer053ENative(IntPtr.Zero, path, null, diagnostic);
        }
    }

    /// <summary>
    /// Initializes the native mixer on a worker thread. The caller waits at most
    /// five seconds; a timeout leaves the provider unavailable and the HID path
    /// untouched.
    /// </summary>
    public async Task<bool> InitializeAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!IsAvailable)
        {
            return false;
        }

        if (_initialized)
        {
            return true;
        }

        Task<bool> initializeTask;
        lock (_initializeGate)
        {
            _initializeTask ??= Task.Run(InitializeCore);
            initializeTask = _initializeTask;
        }

        try
        {
            var initialized = await initializeTask
                .WaitAsync(InitializeTimeout, cancellationToken)
                .ConfigureAwait(false);
            if (!initialized)
            {
                _diagnostic?.Invoke(
                    $"053E native Initialize failed: {_initializeError?.Message ?? "unknown error"}");
                return false;
            }

            _initialized = true;
            return true;
        }
        catch (TimeoutException)
        {
            _diagnostic?.Invoke("053E native Initialize timed out; using HID subset.");
            return false;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            _diagnostic?.Invoke($"053E native Initialize failed: {exception.Message}");
            return false;
        }
    }

    public IReadOnlyList<RazerMixer053ENativeDevice> EnumerateDevices(int dataFlow)
    {
        if (!_initialized || _disposed || dataFlow is < 0 or > 1)
        {
            return [];
        }

        int count;
        try
        {
            lock (_nativeGate)
            {
                count = _exports!.GetDeviceCount(dataFlow);
            }
        }
        catch (Exception exception)
        {
            _diagnostic?.Invoke($"053E native GetDeviceCount failed: {exception.Message}");
            return [];
        }

        if (count <= 0)
        {
            return [];
        }

        var devices = new List<RazerMixer053ENativeDevice>(Math.Min(count, MaxDevices));
        for (var index = 0; index < Math.Min(count, MaxDevices); index++)
        {
            string json;
            try
            {
                IntPtr pointer;
                lock (_nativeGate)
                {
                    pointer = _exports!.GetDeviceById(dataFlow, index);
                }

                json = ReadString(pointer);
            }
            catch (Exception exception)
            {
                _diagnostic?.Invoke($"053E native GetDeviceById({dataFlow}, {index}) failed: {exception.Message}");
                continue;
            }

            if (TryParseDevice(json, out var device))
            {
                devices.Add(device);
            }
            else
            {
                _diagnostic?.Invoke($"053E native device JSON was not recognized: {json}");
            }
        }

        return devices;
    }

    /// <summary>
    /// Reads one peak value through RazerT2GetPeakValue. This is a read-only
    /// property call; the returned bytes are two little-endian floats.
    /// </summary>
    public RazerMixer053ENativePeak? ReadPeak(RazerMixer053ENativeDevice device)
    {
        ArgumentNullException.ThrowIfNull(device);
        if (!_initialized || _disposed)
        {
            return null;
        }

        var value = Marshal.AllocHGlobal(PeakBufferLength);
        var extra = Marshal.AllocHGlobal(PeakBufferLength);
        try
        {
            Marshal.WriteInt64(value, 0);
            Marshal.WriteInt64(extra, 0);

            IntPtr pointer;
            lock (_nativeGate)
            {
                pointer = _exports!.PropertyControl(
                    device.Id,
                    device.JackType,
                    device.DataFlow,
                    device.DeviceState,
                    "RazerT2GetPeakValue",
                    value,
                    PeakBufferLength,
                    extra,
                    PeakBufferLength,
                    0);
            }

            var json = ReadString(pointer);
            if (TryParsePeakJson(json, out var peak))
            {
                return peak;
            }

            _diagnostic?.Invoke($"053E native peak response was not recognized: {json}");
            return null;
        }
        catch (Exception exception)
        {
            _diagnostic?.Invoke($"053E native peak read failed: {exception.Message}");
            return null;
        }
        finally
        {
            Marshal.FreeHGlobal(value);
            Marshal.FreeHGlobal(extra);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_initialized)
        {
            try
            {
                _exports!.Terminate();
            }
            catch (Exception exception)
            {
                _diagnostic?.Invoke($"053E native Terminate failed: {exception.Message}");
            }
        }

        // shortcut: an Initialize that exceeded the five-second wait may still be
        // running inside the DLL; freeing it there would be unsafe.
        if ((_initializeTask is null || _initializeTask.IsCompleted) && _handle != IntPtr.Zero)
        {
            NativeLibrary.Free(_handle);
        }
    }

    internal static IReadOnlyList<string> BuildCandidatePaths(string? appDirectory, string? localAppData)
    {
        var candidates = new List<string>(3);
        if (!string.IsNullOrWhiteSpace(localAppData))
        {
            candidates.Add(Path.Combine(
                localAppData,
                "Razer",
                "Synapse3",
                "Apps",
                "Synapse",
                DllFileName));
        }

        if (!string.IsNullOrWhiteSpace(appDirectory))
        {
            candidates.Add(Path.Combine(appDirectory, DllFileName));
        }

        if (!string.IsNullOrWhiteSpace(localAppData))
        {
            candidates.Add(Path.Combine(
                localAppData,
                "Razer",
                "RazerAppEngine",
                "User Data",
                "Apps",
                "Synapse",
                DllFileName));
        }

        return candidates.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    internal static string? FindDll(string? appDirectory, string? localAppData) =>
        BuildCandidatePaths(appDirectory, localAppData).FirstOrDefault(File.Exists);

    internal static bool TryParseDevice(
        string? json,
        out RazerMixer053ENativeDevice device)
    {
        device = default!;
        if (string.IsNullOrWhiteSpace(json))
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (!TryGetInt32(root, "ID", out var id) ||
                !TryGetInt32(root, "JackType", out var jackType) ||
                !TryGetInt32(root, "DataFlow", out var dataFlow) ||
                !TryGetInt32(root, "DeviceState", out var deviceState))
            {
                return false;
            }

            device = new RazerMixer053ENativeDevice(id, jackType, dataFlow, deviceState, json);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    internal static bool TryParsePeakJson(string? json, out RazerMixer053ENativePeak peak)
    {
        peak = default;
        if (string.IsNullOrWhiteSpace(json))
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            if (!document.RootElement.TryGetProperty("data", out var data) ||
                data.ValueKind != JsonValueKind.Array ||
                data.GetArrayLength() < PeakBufferLength)
            {
                return false;
            }

            var bytes = new byte[data.GetArrayLength()];
            var index = 0;
            foreach (var item in data.EnumerateArray())
            {
                if (!item.TryGetInt32(out var value) || value is < 0 or > 255)
                {
                    return false;
                }

                bytes[index++] = (byte)value;
            }

            return TryConvertPeak(bytes, out peak);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    internal static bool TryConvertPeak(
        ReadOnlySpan<byte> data,
        out RazerMixer053ENativePeak peak)
    {
        peak = default;
        if (data.Length < PeakBufferLength)
        {
            return false;
        }

        var left = BinaryPrimitives.ReadSingleLittleEndian(data[..4]);
        var right = BinaryPrimitives.ReadSingleLittleEndian(data[4..8]);
        if (!float.IsFinite(left) || !float.IsFinite(right))
        {
            return false;
        }

        peak = new RazerMixer053ENativePeak(left, right);
        return true;
    }

    private bool InitializeCore()
    {
        try
        {
            return _exports!.Initialize();
        }
        catch (Exception exception)
        {
            _initializeError = exception;
            return false;
        }
    }

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
            _exports!.FreeMalloc(pointer);
        }
    }

    private static T LoadRequired<T>(IntPtr handle, string export) where T : Delegate
    {
        if (!NativeLibrary.TryGetExport(handle, export, out var address))
        {
            throw new EntryPointNotFoundException(export);
        }

        return Marshal.GetDelegateForFunctionPointer<T>(address);
    }

    private static bool TryGetInt32(JsonElement root, string name, out int value)
    {
        value = 0;
        return root.TryGetProperty(name, out var property) && property.TryGetInt32(out value);
    }

    private static bool IsLoadFailure(Exception exception) =>
        exception is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException or
            IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or
            MarshalDirectiveException or InvalidOperationException or TimeoutException;
}

public sealed record RazerMixer053ENativeDevice(
    int Id,
    int JackType,
    int DataFlow,
    int DeviceState,
    string Json);

public readonly record struct RazerMixer053ENativePeak(float Left, float Right);

public sealed record RazerMixer053ENativeDevicePeak(
    RazerMixer053ENativeDevice Device,
    RazerMixer053ENativePeak Peak);

public sealed record RazerMixer053ENativeProbe(
    IReadOnlyList<RazerMixer053ENativeDevice> CaptureDevices,
    IReadOnlyList<RazerMixer053ENativeDevice> RenderDevices,
    IReadOnlyList<RazerMixer053ENativeDevicePeak> Peaks);
