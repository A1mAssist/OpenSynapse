using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace OpenSynapse.Windows.Audio;

internal sealed class ThxAudioNativeClient : IDisposable
{
    private const uint LoadLibrarySearchSystem32 = 0x00000800;
    private const int MaximumJsonBytes = 64 * 1024;

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    private delegate bool DeviceCall(nint deviceJson);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate nint GetValueCall(nint deviceJson, nint id);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    private delegate bool SetValueCall(nint deviceJson, nint id, nint value, nint propertyType);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    private delegate bool SelectPresetCall(nint deviceJson, nint groupId, nint presetId);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void FreeMallocCall(nint value);

    private readonly nint _library;
    private readonly DeviceCall _addDevice;
    private readonly DeviceCall _checkStatus;
    private readonly DeviceCall _removeDevice;
    private readonly GetValueCall _getProperty;
    private readonly SetValueCall _setProperty;
    private readonly GetValueCall _getPresetList;
    private readonly GetValueCall _getSelectedPreset;
    private readonly SelectPresetCall _selectPreset;
    private readonly FreeMallocCall _freeMalloc;

    internal ThxAudioNativeClient()
        : this(ThxAudioAdmission.NativeDllPath, ThxAudioAdmission.NativeDllLength, ThxAudioAdmission.NativeDllSha256)
    {
    }

    internal ThxAudioNativeClient(string path, long expectedLength, string expectedSha256)
    {
        VerifyFile(path, expectedLength, expectedSha256);
        _library = LoadLibraryExW(path, nint.Zero, LoadLibrarySearchSystem32);
        if (_library == nint.Zero)
        {
            throw new InvalidOperationException(
                $"The THX native component could not be loaded (0x{Marshal.GetLastPInvokeError():X8}).");
        }

        _addDevice = GetExport<DeviceCall>("AddDevice");
        _checkStatus = GetExport<DeviceCall>("CheckStatus");
        _removeDevice = GetExport<DeviceCall>("RemoveDevice");
        _getProperty = GetExport<GetValueCall>("GetProperty");
        _setProperty = GetExport<SetValueCall>("SetProperty");
        _getPresetList = GetExport<GetValueCall>("GetPresetList");
        _getSelectedPreset = GetExport<GetValueCall>("GetSelectedPreset");
        _selectPreset = GetExport<SelectPresetCall>("SelectPreset");
        _freeMalloc = GetExport<FreeMallocCall>("FreeMalloc");
    }

    internal bool AddDevice(string deviceJson) => Invoke(deviceJson, _addDevice);
    internal bool CheckStatus(string deviceJson) => Invoke(deviceJson, _checkStatus);

    internal string GetProperty(string deviceJson, string propertyId) =>
        GetJson(deviceJson, propertyId, _getProperty);

    internal bool SetProperty(string deviceJson, string propertyId, string value, string? propertyType)
    {
        using var device = new Utf8Argument(deviceJson);
        using var property = new Utf8Argument(propertyId);
        using var json = new Utf8Argument(value);
        using var type = propertyType is null ? null : new Utf8Argument(propertyType);
        return _setProperty(device.Pointer, property.Pointer, json.Pointer, type?.Pointer ?? nint.Zero);
    }

    internal string GetPresetList(string deviceJson, string groupId) =>
        GetJson(deviceJson, groupId, _getPresetList);

    internal string GetSelectedPreset(string deviceJson, string groupId) =>
        GetJson(deviceJson, groupId, _getSelectedPreset);

    internal bool SelectPreset(string deviceJson, string groupId, string presetId)
    {
        using var device = new Utf8Argument(deviceJson);
        using var group = new Utf8Argument(groupId);
        using var preset = new Utf8Argument(presetId);
        return _selectPreset(device.Pointer, group.Pointer, preset.Pointer);
    }

    public void Dispose()
    {
        // The native client leaves asynchronous worker threads alive after
        // RemoveDevice; keep the image mapped until the helper process exits.
    }

    private static bool Invoke(string value, DeviceCall call)
    {
        using var argument = new Utf8Argument(value);
        return call(argument.Pointer);
    }

    private string GetJson(string deviceJson, string id, GetValueCall call)
    {
        using var device = new Utf8Argument(deviceJson);
        using var identifier = new Utf8Argument(id);
        var result = call(device.Pointer, identifier.Pointer);
        if (result == nint.Zero)
        {
            throw new InvalidDataException("The THX native component returned no value.");
        }

        try
        {
            var length = 0;
            while (length < MaximumJsonBytes && Marshal.ReadByte(result, length) != 0)
            {
                length++;
            }
            if (length == MaximumJsonBytes)
            {
                throw new InvalidDataException("The THX native value exceeds 64 KiB.");
            }

            var bytes = new byte[length];
            Marshal.Copy(result, bytes, 0, length);
            return new UTF8Encoding(false, true).GetString(bytes);
        }
        finally
        {
            _freeMalloc(result);
        }
    }

    private T GetExport<T>(string name) where T : Delegate
    {
        var address = GetProcAddress(_library, name);
        if (address == nint.Zero)
        {
            throw new EntryPointNotFoundException($"The THX native component does not export {name}.");
        }
        return Marshal.GetDelegateForFunctionPointer<T>(address);
    }

    private static void VerifyFile(string path, long expectedLength, string expectedSha256)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stream.Length != expectedLength ||
            !CryptographicOperations.FixedTimeEquals(
                Convert.FromHexString(expectedSha256),
                SHA256.HashData(stream)))
        {
            throw new InvalidOperationException("The THX native component has changed.");
        }
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

internal sealed record ThxVendorPreset(string Id, string Name);

internal static class ThxVendorJson
{
    internal static bool ParseBoolean(string json)
    {
        using var document = Parse(json);
        return document.RootElement.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => throw new InvalidDataException("The THX property is not Boolean."),
        };
    }

    internal static IReadOnlyList<string> ParsePresetIds(string json)
    {
        using var document = Parse(json);
        if (document.RootElement.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidDataException("The THX preset list is not an array.");
        }

        var ids = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var element in document.RootElement.EnumerateArray())
        {
            if (element.ValueKind != JsonValueKind.String ||
                element.GetString() is not { } id ||
                !ValidId(id) ||
                !seen.Add(id))
            {
                throw new InvalidDataException("The THX preset list contains an invalid ID.");
            }
            ids.Add(id);
            if (ids.Count > 256)
            {
                throw new InvalidDataException("The THX preset list is too long.");
            }
        }
        return ids;
    }

    internal static double ParseFloat(string json, double minimum, double maximum)
    {
        using var document = Parse(json);
        if (document.RootElement.ValueKind != JsonValueKind.Number ||
            !document.RootElement.TryGetDouble(out var value) ||
            !double.IsFinite(value) ||
            value < minimum ||
            value > maximum)
        {
            throw new InvalidDataException("The THX property is not a valid Float.");
        }
        return value;
    }

    internal static string? ParseSelectedPreset(string json)
    {
        if (json != "null" && ValidId(json) &&
            json.All(character => char.IsAsciiLetterOrDigit(character) || character is '_' or '-' or '.'))
        {
            return json;
        }

        using var document = Parse(json);
        if (document.RootElement.ValueKind == JsonValueKind.Null)
        {
            return null;
        }
        if (document.RootElement.ValueKind != JsonValueKind.String ||
            document.RootElement.GetString() is not { } id ||
            !ValidId(id))
        {
            throw new InvalidDataException("The selected THX preset is invalid.");
        }
        return id;
    }

    internal static IReadOnlyList<ThxVendorPreset> ParsePresetEntries(string json)
    {
        using var document = Parse(json);
        var root = document.RootElement;
        if (root.ValueKind == JsonValueKind.Object &&
            root.TryGetProperty("presets", out var nested))
        {
            root = nested;
        }
        if (root.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidDataException("The THX preset list is not an array.");
        }

        var entries = new List<ThxVendorPreset>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var element in root.EnumerateArray())
        {
            var entry = element.ValueKind switch
            {
                JsonValueKind.String when element.GetString() is { } id && ValidId(id) =>
                    new ThxVendorPreset(id, id),
                JsonValueKind.Object => ReadPresetEntry(element),
                _ => null,
            };
            if (entry is null || !seen.Add(entry.Id))
            {
                continue;
            }
            entries.Add(entry);
            if (entries.Count > 256)
            {
                throw new InvalidDataException("The THX preset list is too long.");
            }
        }
        return entries;
    }

    internal static string? ParseSelectedPresetValue(string json)
    {
        using var document = Parse(json);
        var root = document.RootElement;
        return root.ValueKind switch
        {
            JsonValueKind.Null => null,
            JsonValueKind.String when root.GetString() is { } id && ValidId(id) => id,
            JsonValueKind.Object => ReadPresetEntry(root)?.Id,
            _ => throw new InvalidDataException("The selected THX preset is invalid."),
        };
    }

    internal static IReadOnlyList<double> ParseGains(string json)
    {
        using var document = Parse(json);
        var root = document.RootElement;
        if (root.ValueKind == JsonValueKind.Object)
        {
            root = new[] { "gains", "eq", "bands", "values" }
                .Select(name => root.TryGetProperty(name, out var value) ? value : default)
                .FirstOrDefault(value => value.ValueKind == JsonValueKind.Array);
        }
        if (root.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidDataException("The THX equalizer value is not an array.");
        }

        var gains = new List<double>();
        foreach (var element in root.EnumerateArray())
        {
            JsonElement value = default;
            if (element.ValueKind == JsonValueKind.Number)
            {
                value = element;
            }
            else if (element.ValueKind == JsonValueKind.Object)
            {
                foreach (var name in new[] { "gain", "gainDb", "value" })
                {
                    if (element.TryGetProperty(name, out var candidate) &&
                        candidate.ValueKind == JsonValueKind.Number)
                    {
                        value = candidate;
                        break;
                    }
                }
            }

            if (value.ValueKind != JsonValueKind.Number ||
                !value.TryGetDouble(out var gain) ||
                !double.IsFinite(gain) ||
                gain is < -24 or > 24)
            {
                throw new InvalidDataException("The THX equalizer value is invalid.");
            }
            gains.Add(gain);
            if (gains.Count > 16)
            {
                throw new InvalidDataException("The THX equalizer value is too long.");
            }
        }
        return gains;
    }

    internal static int ParseInteger(string json, int minimum, int maximum)
    {
        using var document = Parse(json);
        if (document.RootElement.ValueKind != JsonValueKind.Number ||
            !document.RootElement.TryGetInt32(out var value) ||
            value < minimum ||
            value > maximum)
        {
            throw new InvalidDataException("The THX property is not a valid integer.");
        }
        return value;
    }

    private static ThxVendorPreset? ReadPresetEntry(JsonElement element)
    {
        string? id = null;
        foreach (var name in new[] { "id", "preset", "value", "key" })
        {
            if (element.TryGetProperty(name, out var candidate) &&
                candidate.ValueKind == JsonValueKind.String &&
                candidate.GetString() is { } text &&
                ValidId(text))
            {
                id = text;
                break;
            }
        }
        if (id is null &&
            element.TryGetProperty("name", out var nameCandidate) &&
            nameCandidate.ValueKind == JsonValueKind.String &&
            nameCandidate.GetString() is { } nameId &&
            ValidId(nameId))
        {
            id = nameId;
        }
        if (id is null)
        {
            return null;
        }

        var label = id;
        foreach (var name in new[] { "name", "label", "displayName" })
        {
            if (element.TryGetProperty(name, out var candidate) &&
                candidate.ValueKind == JsonValueKind.String &&
                candidate.GetString() is { } text &&
                !string.IsNullOrWhiteSpace(text) &&
                text.Length <= 256)
            {
                label = text.Trim();
                break;
            }
        }
        return new ThxVendorPreset(id, label);
    }

    private static JsonDocument Parse(string json)
    {
        if (string.IsNullOrEmpty(json) || json.Length > 64 * 1024)
        {
            throw new InvalidDataException("The THX JSON value is invalid.");
        }
        try
        {
            return JsonDocument.Parse(json, new JsonDocumentOptions
            {
                AllowTrailingCommas = false,
                CommentHandling = JsonCommentHandling.Disallow,
                MaxDepth = 8,
            });
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("The THX JSON value is malformed.", exception);
        }
    }

    private static bool ValidId(string value) =>
        !string.IsNullOrWhiteSpace(value) &&
        value.Length <= 256 &&
        string.Equals(value, value.Trim(), StringComparison.Ordinal) &&
        !value.Any(char.IsControl);
}
