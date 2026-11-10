using System.Collections.Frozen;
using System.Text.Json;

namespace OpenSynapse.Windows.Input;

/// <summary>
/// The mapping page uses its own button ids (0x0B DPI up, 0x0E profile switch),
/// while the private key stream reports the DKM code (32, 80). This map is generated
/// from each product bundle by <c>tools/Extract-RazerSpecialKeyMap.ps1</c>.
/// </summary>
internal static class RazerSpecialKeyMap
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };
    private static readonly FrozenDictionary<ushort, IReadOnlyDictionary<byte, byte>> Maps = Load();

    internal static bool TryResolve(ushort productId, byte mappingButtonId, out byte razerKeyCode)
    {
        if (Maps.TryGetValue(productId, out var keys) &&
            keys.TryGetValue(mappingButtonId, out razerKeyCode))
        {
            return true;
        }

        razerKeyCode = 0;
        return false;
    }

    private static FrozenDictionary<ushort, IReadOnlyDictionary<byte, byte>> Load()
    {
        var assembly = typeof(RazerSpecialKeyMap).Assembly;
        var resourceName = assembly.GetManifestResourceNames()
            .SingleOrDefault(name => name.EndsWith(".Devices.RazerHid.mapping-private-keys.json",
                StringComparison.Ordinal))
            ?? throw new InvalidOperationException("Missing embedded mapping-private-keys.json.");
        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Cannot open {resourceName}.");
        var entries = JsonSerializer.Deserialize<Dictionary<string, Entry>>(stream, JsonOptions)
            ?? throw new InvalidOperationException("mapping-private-keys.json is empty.");

        var result = new Dictionary<ushort, IReadOnlyDictionary<byte, byte>>();
        foreach (var (productText, entry) in entries)
        {
            var keys = entry.Keys.ToDictionary(
                item => Convert.ToByte(item.Key, 16),
                item => checked((byte)item.Value.Code));
            result[Convert.ToUInt16(productText, 16)] = keys.ToFrozenDictionary();
        }

        return result.ToFrozenDictionary();
    }

    private sealed record Entry(Dictionary<string, KeyEntry> Keys);
    private sealed record KeyEntry(int Code);
}
