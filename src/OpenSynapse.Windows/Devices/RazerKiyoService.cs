using System.Globalization;
using System.Text.Json;

namespace OpenSynapse.Windows.Devices;

/// <summary>One VideoProcAmp/CameraControl value returned by a Razer camera DLL.</summary>
public readonly record struct RazerKiyoValue(int Value, bool IsAuto);

/// <summary>Snapshot of the values exposed by a Razer camera DLL.</summary>
public sealed record RazerKiyoSnapshot(
    RazerKiyoValue? Brightness,
    RazerKiyoValue? Contrast,
    RazerKiyoValue? Saturation,
    RazerKiyoValue? WhiteBalance,
    RazerKiyoValue? Focus,
    RazerKiyoValue? Zoom,
    string FirmwareVersion,
    string SerialNumber);

/// <summary>Base API actually exported by a Razer camera DLL.</summary>
[Flags]
public enum RazerKiyoApi
{
    None = 0,
    ReadBrightness = 1 << 0,
    ReadContrast = 1 << 1,
    ReadSaturation = 1 << 2,
    ReadWhiteBalance = 1 << 3,
    ReadFirmwareVersion = 1 << 4,
    ReadSerialNumber = 1 << 5,
    ReadCameraFocus = 1 << 6,
    ReadCameraZoom = 1 << 7,
    WriteBrightness = 1 << 8,
    WriteContrast = 1 << 9,
    WriteSaturation = 1 << 10,
    WriteWhiteBalance = 1 << 11,
    WriteCameraFocus = 1 << 12,
    WriteCameraZoom = 1 << 13,
    SettingsDialog = 1 << 14,
}

/// <summary>
/// PID to native DLL/product mapping taken from the middleware bundle manifests.
/// The product name is the manifest's deviceName; the registry friendly name wins
/// for display when Windows provides one.
/// </summary>
public sealed record RazerKiyoDevice(
    ushort ProductId,
    string DllName,
    string ProductName)
{
    public string Identity => $"VID_1532 / PID_{ProductId:X4}";
}

public static class RazerKiyoDevices
{
    public const ushort DefaultProductId = 0x0E03;

    // 0E0B/0E0C declare rzCamKimberly + RzNativeDLLGenericCamera in the bundle;
    // the stale dllFileName string in main.js is not the DLL load path.
    public static IReadOnlyList<RazerKiyoDevice> All { get; } =
    [
        new(0x0E03, "RzNative_0E03.dll", "Razer Kiyo"),
        new(0x0E05, "RzNative_0E05.dll", "Razer Kiyo Pro"),
        new(0x0E06, "RzNative_0E06.dll", "Razer Kiyo X"),
        new(0x0E08, "RzNative_0E08.dll", "Razer Kiyo Pro Ultra"),
        new(0x0E0A, "RzNative_GenericCamera.dll", "Razer Kiyo V2 Pro"),
        new(0x0E0B, "RzNative_GenericCamera.dll", "Razer Kiyo V2"),
        new(0x0E0C, "RzNative_GenericCamera.dll", "Razer Kiyo V2 X"),
    ];

    public static IReadOnlySet<ushort> SupportedProductIds { get; } =
        All.Select(device => device.ProductId).ToHashSet();

    public static RazerKiyoDevice? Find(ushort productId) =>
        All.FirstOrDefault(device => device.ProductId == productId);
}

/// <summary>
/// Parses the JSON written by a Razer camera DLL. The DLL quotes both fields:
/// <c>{"value":"50", "isAuto":"false"} \r\n</c>; the tolerant form also accepts
/// plain numbers/booleans so a DLL revision does not break the page.
/// </summary>
public static class RazerKiyoJson
{
    public static RazerKiyoValue? ParseValue(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object ||
                !document.RootElement.TryGetProperty("value", out var valueElement))
            {
                return null;
            }

            if (!TryReadInt32(valueElement, out var value))
            {
                return null;
            }

            var isAuto = document.RootElement.TryGetProperty("isAuto", out var autoElement) &&
                TryReadBoolean(autoElement, out var parsedAuto) &&
                parsedAuto;
            return new RazerKiyoValue(value, isAuto);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public static string ParseFirmwareVersion(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return string.Empty;
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return string.Empty;
            }

            var parts = new List<string>(4);
            foreach (var name in new[] { "major", "minor", "internal", "reserved" })
            {
                if (!document.RootElement.TryGetProperty(name, out var element) ||
                    !TryReadInt32(element, out var part))
                {
                    return string.Empty;
                }

                parts.Add(part.ToString(CultureInfo.InvariantCulture));
            }

            return string.Join('.', parts);
        }
        catch (JsonException)
        {
            return string.Empty;
        }
    }

    public static string ParseSerialNumber(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return string.Empty;
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            return document.RootElement.ValueKind == JsonValueKind.Object &&
                document.RootElement.TryGetProperty("serialNumberAscii", out var element) &&
                element.ValueKind == JsonValueKind.String
                    ? element.GetString() ?? string.Empty
                    : string.Empty;
        }
        catch (JsonException)
        {
            return string.Empty;
        }
    }

    private static bool TryReadInt32(JsonElement element, out int value)
    {
        value = 0;
        if (element.ValueKind == JsonValueKind.Number && element.TryGetInt32(out value))
        {
            return true;
        }

        return element.ValueKind == JsonValueKind.String &&
            int.TryParse(element.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
    }

    private static bool TryReadBoolean(JsonElement element, out bool value)
    {
        value = false;
        if (element.ValueKind is JsonValueKind.True or JsonValueKind.False)
        {
            value = element.GetBoolean();
            return true;
        }

        return element.ValueKind == JsonValueKind.String &&
            bool.TryParse(element.GetString(), out value);
    }
}

/// <summary>
/// Reads and applies Razer camera image settings through the DLL Synapse already
/// installed. Numeric slider ranges are not exposed by the DLL, so
/// brightness/contrast/saturation writes stay in Razer's native settings dialog.
/// White balance has an explicit auto flag and is applied here.
/// </summary>
public sealed class RazerKiyoService : IDisposable
{
    public const string DeviceName = "Razer Kiyo";
    public const ushort DefaultProductId = RazerKiyoDevices.DefaultProductId;

    private readonly RazerKiyoNative? _native;
    private readonly string _containerId;
    private readonly RazerKiyoDevice _device;
    private readonly string _failure;

    private RazerKiyoService(
        RazerKiyoNative native,
        string containerId,
        RazerKiyoDevice device,
        string? friendlyName)
    {
        _native = native;
        _containerId = containerId;
        _device = device;
        _failure = string.Empty;
        Name = string.IsNullOrWhiteSpace(friendlyName) ? device.ProductName : friendlyName.Trim();
    }

    private RazerKiyoService(RazerKiyoDevice device, string? friendlyName, string failure)
    {
        _containerId = string.Empty;
        _device = device;
        _failure = failure;
        Name = string.IsNullOrWhiteSpace(friendlyName) ? device.ProductName : friendlyName.Trim();
    }

    public string ContainerId => _containerId;

    public string Name { get; }

    public ushort ProductId => _device.ProductId;

    public string Identity => _device.Identity;

    public bool IsAvailable => _native is not null;

    public RazerKiyoApi Api => _native?.Api ?? RazerKiyoApi.None;

    public static async Task<RazerKiyoService?> ConnectAsync(
        Action<string>? diagnostic = null,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<RazerKiyoDeviceInfo> devices;
        try
        {
            devices = await Task.Run(RazerKiyoDiscovery.FindDevices, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (IsConnectFailure(exception))
        {
            diagnostic?.Invoke($"Camera discovery failed: {exception.Message}");
            return null;
        }

        RazerKiyoDeviceInfo? firstPresent = null;
        string? firstFailure = null;
        foreach (var device in devices)
        {
            var profile = RazerKiyoDevices.Find(device.ProductId);
            if (profile is null)
            {
                continue;
            }

            firstPresent ??= device;
            RazerKiyoNative? native = null;
            try
            {
                native = RazerKiyoNative.TryLoad(profile.DllName);
                if (native is null)
                {
                    firstFailure ??= $"{profile.DllName} not found";
                    diagnostic?.Invoke($"{profile.DllName} not found");
                    continue;
                }

                var formatted = device.ContainerId.ToString("B").ToUpperInvariant();
                var initialized = false;
                for (var attempt = 0; attempt < 10; attempt++)
                {
                    if (native.DeviceInitialize(formatted))
                    {
                        initialized = true;
                        break;
                    }

                    await Task.Delay(100, cancellationToken).ConfigureAwait(false);
                }

                if (!initialized)
                {
                    firstFailure ??= $"{profile.DllName} DeviceInitialize failed";
                    diagnostic?.Invoke($"{profile.DllName} DeviceInitialize failed");
                    native.Dispose();
                    native = null;
                    continue;
                }

                return new RazerKiyoService(native, formatted, profile, device.FriendlyName);
            }
            catch (OperationCanceledException)
            {
                native?.Dispose();
                throw;
            }
            catch (Exception exception) when (IsConnectFailure(exception))
            {
                firstFailure ??= $"{profile.DllName} connect failed: {exception.Message}";
                diagnostic?.Invoke($"{profile.DllName} connect failed: {exception.Message}");
                native?.Dispose();
            }
        }

        if (firstPresent is { } present)
        {
            var profile = RazerKiyoDevices.Find(present.ProductId);
            if (profile is not null)
            {
                return new RazerKiyoService(
                    profile,
                    present.FriendlyName,
                    firstFailure ?? "Camera DLL is not available.");
            }
        }

        return null;
    }

    public RazerKiyoSnapshot ReadSnapshot()
    {
        if (_native is null)
        {
            throw new InvalidOperationException(_failure);
        }

        var api = _native.Api;
        return new(
            api.HasFlag(RazerKiyoApi.ReadBrightness)
                ? RazerKiyoJson.ParseValue(_native.GetBrightness(_containerId))
                : null,
            api.HasFlag(RazerKiyoApi.ReadContrast)
                ? RazerKiyoJson.ParseValue(_native.GetContrast(_containerId))
                : null,
            api.HasFlag(RazerKiyoApi.ReadSaturation)
                ? RazerKiyoJson.ParseValue(_native.GetSaturation(_containerId))
                : null,
            api.HasFlag(RazerKiyoApi.ReadWhiteBalance)
                ? RazerKiyoJson.ParseValue(_native.GetWhiteBalance(_containerId))
                : null,
            api.HasFlag(RazerKiyoApi.ReadCameraFocus)
                ? RazerKiyoJson.ParseValue(_native.GetCameraFocus(_containerId))
                : null,
            api.HasFlag(RazerKiyoApi.ReadCameraZoom)
                ? RazerKiyoJson.ParseValue(_native.GetCameraZoom(_containerId))
                : null,
            api.HasFlag(RazerKiyoApi.ReadFirmwareVersion)
                ? RazerKiyoJson.ParseFirmwareVersion(_native.GetFirmwareVersion(_containerId))
                : string.Empty,
            api.HasFlag(RazerKiyoApi.ReadSerialNumber)
                ? RazerKiyoJson.ParseSerialNumber(_native.GetSerialNumber(_containerId))
                : string.Empty);
    }

    /// <summary>Writes one white-balance state and verifies it by reading the DLL back.</summary>
    public RazerKiyoValue SetWhiteBalanceAuto(bool auto)
    {
        if (_native is null ||
            !_native.Api.HasFlag(RazerKiyoApi.ReadWhiteBalance) ||
            !_native.Api.HasFlag(RazerKiyoApi.WriteWhiteBalance))
        {
            throw new InvalidOperationException("White balance is not available.");
        }

        var original = RazerKiyoJson.ParseValue(_native.GetWhiteBalance(_containerId)) ??
            throw new InvalidOperationException("White balance is not available.");

        if (!_native.SetWhiteBalance(_containerId, original.Value, auto))
        {
            throw new InvalidOperationException("SetWhiteBalance was rejected.");
        }

        var readback = RazerKiyoJson.ParseValue(_native.GetWhiteBalance(_containerId));
        if (readback is not { } updated || updated.IsAuto != auto)
        {
            _native.SetWhiteBalance(_containerId, original.Value, original.IsAuto);
            throw new InvalidOperationException("White balance readback mismatch.");
        }

        return updated;
    }

    public bool OpenSettings() =>
        _native is not null &&
        _native.Api.HasFlag(RazerKiyoApi.SettingsDialog) &&
        _native.DisplayCameraPropertiesDialog(_containerId);

    public void Dispose()
    {
        if (_native is null)
        {
            return;
        }

        try
        {
            _native.DeviceTerminate(_containerId);
        }
        finally
        {
            _native.Dispose();
        }
    }

    private static bool IsConnectFailure(Exception exception) =>
        exception is IOException or InvalidOperationException or NotSupportedException or
            UnauthorizedAccessException or ArgumentException or BadImageFormatException or
            DllNotFoundException or EntryPointNotFoundException or System.ComponentModel.Win32Exception or
            System.Runtime.InteropServices.MarshalDirectiveException;
}
