using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using OpenSynapse.Core.Devices;

namespace OpenSynapse.Windows.Devices;

public sealed record RazerDualDongleName(int Eid, string Name, string ChineseName);

public sealed record RazerDualDongleEntry(
    ushort DongleProductId,
    DeviceCategory Category,
    IReadOnlyList<ushort> Buddies,
    IReadOnlyList<ushort> Siblings,
    IReadOnlyList<RazerDualDongleName> Names);

/// <summary>
/// Dual-dongle compatibility table.
/// Source: https://apps.razer.com/synapse/dashboard/DualDongleCompatibleDevices.json
/// (the request needs Referer: https://apps.razer.com/synapse/chroma-studio/).
/// </summary>
public static class RazerDualDongleCatalog
{
    private const string ResourceName = "OpenSynapse.Windows.Devices.RazerDualDongleCompatibility.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private static readonly Lazy<IReadOnlyList<RazerDualDongleEntry>> HostsValue = new(LoadHosts);

    private static readonly Lazy<IReadOnlyDictionary<ushort, RazerDualDongleEntry>> ByProductId =
        new(() => HostsValue.Value.ToDictionary(entry => entry.DongleProductId));

    public static IReadOnlyList<RazerDualDongleEntry> Hosts => HostsValue.Value;

    public static bool TryGetHost(ushort productId, out RazerDualDongleEntry entry)
    {
        if (ByProductId.Value.TryGetValue(productId, out var found))
        {
            entry = found;
            return true;
        }

        entry = null!;
        return false;
    }

    public static bool CanHost(ushort productId) =>
        TryGetHost(productId, out var entry) && entry.Buddies.Count > 0;

    public static IReadOnlyList<RazerDualDongleEntry> GetCandidates(ushort hostProductId)
    {
        if (!TryGetHost(hostProductId, out var host) || host.Buddies.Count == 0)
        {
            return Array.Empty<RazerDualDongleEntry>();
        }

        return host.Buddies
            .Select(pid => ByProductId.Value.TryGetValue(pid, out var candidate) ? candidate : null)
            .Where(candidate => candidate is not null)
            .Select(candidate => candidate!)
            .ToArray();
    }

    public static string DisplayName(ushort productId, CultureInfo? culture = null)
    {
        if (!TryGetHost(productId, out var entry))
        {
            return $"PID {productId:X4}";
        }

        var preferChinese = (culture ?? CultureInfo.CurrentUICulture).TwoLetterISOLanguageName == "zh";
        var chinese = preferChinese
            ? entry.Names.Select(name => name.ChineseName).FirstOrDefault(name => !string.IsNullOrWhiteSpace(name))
            : null;
        return chinese
            ?? entry.Names.Select(name => name.Name).FirstOrDefault(name => !string.IsNullOrWhiteSpace(name))
            ?? $"PID {productId:X4}";
    }

    public static DeviceCategory CategoryOf(ushort productId) =>
        TryGetHost(productId, out var entry) ? entry.Category : DeviceCategory.Unknown;

    public static IReadOnlyList<RazerDualDongleEntry> FilterCandidates(
        ushort hostProductId,
        IEnumerable<ushort> connectedProductIds)
    {
        var connected = connectedProductIds.ToHashSet();
        var connectedCategories = connected
            .Select(CategoryOf)
            .Where(category => category != DeviceCategory.Unknown)
            .ToHashSet();
        return GetCandidates(hostProductId)
            .Where(candidate => !connected.Contains(candidate.DongleProductId))
            .Where(candidate => candidate.Category == DeviceCategory.Unknown
                || !connectedCategories.Contains(candidate.Category))
            .ToArray();
    }

    private static IReadOnlyList<RazerDualDongleEntry> LoadHosts()
    {
        using var stream = typeof(RazerDualDongleCatalog).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"Unable to read '{ResourceName}'.");
        var document = JsonSerializer.Deserialize<CompatibilityDocument>(stream, JsonOptions)
            ?? throw new InvalidOperationException($"'{ResourceName}' is empty.");
        return (document.Devices ?? []).Select(ToEntry).ToArray();
    }

    private static RazerDualDongleEntry ToEntry(DeviceJson device) => new(
        (ushort)device.DeviceDonglePid,
        ParseCategory(device.DeviceType),
        (device.DevicePairingBuddies ?? []).Select(pid => (ushort)pid).ToArray(),
        (device.DeviceSiblings ?? []).Select(pid => (ushort)pid).ToArray(),
        (device.ProductInfo ?? [])
            .Select(info => new RazerDualDongleName(
                info.Eid,
                info.Name ?? string.Empty,
                info.ChineseName ?? string.Empty))
            .ToArray());

    private static DeviceCategory ParseCategory(string? deviceType) => deviceType?.ToUpperInvariant() switch
    {
        "KEYBOARD" => DeviceCategory.Keyboard,
        "MOUSE" => DeviceCategory.Mouse,
        "ACCESSORY" => DeviceCategory.Accessory,
        "MOUSEMAT" => DeviceCategory.MouseMat,
        "DONGLE" => DeviceCategory.Accessory,
        _ => DeviceCategory.Unknown,
    };

    private sealed class CompatibilityDocument
    {
        [JsonPropertyName("Devices")]
        public List<DeviceJson>? Devices { get; set; }
    }

    private sealed class DeviceJson
    {
        [JsonPropertyName("ProductInfo")]
        public List<ProductInfoJson>? ProductInfo { get; set; }

        [JsonPropertyName("DeviceDonglePid")]
        public int DeviceDonglePid { get; set; }

        [JsonPropertyName("DevicePairingBuddies")]
        public List<int>? DevicePairingBuddies { get; set; }

        [JsonPropertyName("DeviceSiblings")]
        public List<int>? DeviceSiblings { get; set; }

        [JsonPropertyName("DeviceType")]
        public string? DeviceType { get; set; }
    }

    private sealed class ProductInfoJson
    {
        [JsonPropertyName("EID")]
        public int Eid { get; set; }

        [JsonPropertyName("Name")]
        public string? Name { get; set; }

        [JsonPropertyName("CHSName")]
        public string? ChineseName { get; set; }
    }
}
