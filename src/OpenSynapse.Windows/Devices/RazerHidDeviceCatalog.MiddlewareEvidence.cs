using System.Collections.Frozen;

namespace OpenSynapse.Windows.Devices;

public sealed partial class RazerHidDeviceCatalog
{
    private static RazerHidDeviceDefinition CreateMiddlewareMouseDefinition(
        RazerHidDeviceDefinition sibling,
        ushort productId,
        string displayName,
        int maximumDpi,
        int[] pollingRates)
    {
        var allowedWrites = GetMiddlewareMouseWriteEvidence(productId);
        var sourceCapabilities = sibling.SourceCapabilities.Where(capability =>
            !ExcludeLighting(capability) &&
            (!IsWriteCapability(capability) || allowedWrites.Any(group => Matches(group, capability))))
            .ToFrozenSet(StringComparer.Ordinal);
        var transactions = sibling.Transactions.Where(item =>
                !ExcludeLighting(item.Key) &&
                (!IsWriteBuilder(item.Key) || allowedWrites.Any(group => Matches(group, item.Key))))
            .ToFrozenDictionary(StringComparer.Ordinal);
        var prefixes = sibling.BuilderArgumentPrefixes.Where(item => transactions.ContainsKey(item.Key))
            .ToFrozenDictionary(StringComparer.Ordinal);
        var commandSequences = sibling.CommandSequences.Where(item =>
                !ExcludeLighting(item.Key) &&
                (!IsWriteBuilder(item.Key) || allowedWrites.Any(group => Matches(group, item.Key))))
            .ToFrozenDictionary(StringComparer.Ordinal);
        var commandSizes = sibling.CommandSizes.Where(item =>
                transactions.ContainsKey(item.Key) || commandSequences.ContainsKey(item.Key))
            .ToFrozenDictionary(StringComparer.Ordinal);
        return Copy(sourceCapabilities, transactions, prefixes, commandSequences, commandSizes,
            supportsIndependentDpi: productId != 0x00E0 && productId != 0x0063 && productId != 0x0074 && productId != 0x00A2);

        RazerHidDeviceDefinition Copy(
            IReadOnlySet<string> capabilities,
            IReadOnlyDictionary<string, byte> copiedTransactions,
            IReadOnlyDictionary<string, IReadOnlyList<ReadOnlyMemory<byte>>> copiedPrefixes,
            IReadOnlyDictionary<string, IReadOnlyList<RazerHidCommandStep>> copiedSequences,
            IReadOnlyDictionary<string, byte> copiedSizes,
            bool supportsIndependentDpi) => new(
                productId, displayName, sibling.Category, capabilities, maximumDpi,
                Array.AsReadOnly(IsMiddlewareMouseReadOnly(productId) ? Array.Empty<int>() : pollingRates),
                sibling.MatrixDimensions, sibling.DeviceWait,
                copiedTransactions, copiedPrefixes, sibling.DefaultStorage, sibling.DefaultLedZone,
                copiedSequences, copiedSizes, supportsIndependentDpi);

        bool ExcludeLighting(string name) => productId == 0x00E0 &&
            (name.Contains("brightness", StringComparison.Ordinal) || name.Contains("effect", StringComparison.Ordinal) ||
             name.Contains("led", StringComparison.Ordinal) || name.Contains("matrix", StringComparison.Ordinal) ||
             name == "set_key_row");
    }

    private static FrozenSet<string> GetMiddlewareMouseWriteEvidence(ushort productId) => (productId switch
    {
        0x0063 or 0x0074 => new[] { "dpi", "poll" },
        0x0069 or 0x0075 or 0x0093 or 0x00E7 or 0x00E8 =>
            ["dpi", "dpi_stages", "poll", "idle", "low_battery"],
        0x00DD or 0x00E0 or 0x00E5 or 0x00E6 or 0x00EF or 0x00F0 or 0xB005 or 0xB007 =>
            ["dpi", "dpi_stages", "poll", "idle", "low_battery"],
        0x00E2 or 0x00E3 or 0x00EA or 0x00EB =>
            ["dpi", "dpi_stages", "poll", "idle", "low_battery",
             "scroll_mode", "scroll_acceleration", "scroll_smart_reel"],
        // This directory's main declares productId:163 (00A3), not target 00A2.
        0x00A2 => [],
        0xB00A or 0xB00B =>
            ["dpi", "dpi_stages", "poll", "idle", "low_battery",
             "scroll_mode", "scroll_acceleration", "scroll_smart_reel", "brightness", "effect", "custom_frame"],
        _ => [],
    }).ToFrozenSet(StringComparer.Ordinal);

    internal static bool IsMiddlewareMouseReadOnly(ushort productId) => productId == 0x00A2;

    private static bool IsWriteCapability(string capability) =>
        capability.StartsWith("set_", StringComparison.Ordinal) || capability is "add_macro" or "delete_macro";

    private static bool IsWriteBuilder(string builder) =>
        !builder.Contains("_get_", StringComparison.Ordinal) &&
        (builder.Contains("set_", StringComparison.Ordinal) ||
         builder.Contains("_effect_", StringComparison.Ordinal) ||
         builder.Contains("_brightness", StringComparison.Ordinal));

    private static bool Matches(string group, string value) => group switch
    {
        "dpi" => value.Contains("dpi_xy", StringComparison.Ordinal),
        "dpi_stages" => value.Contains("dpi_stages", StringComparison.Ordinal),
        "poll" => value.Contains("poll_rate", StringComparison.Ordinal) ||
            value.Contains("polling_rate", StringComparison.Ordinal),
        "idle" => value.Contains("idle_time", StringComparison.Ordinal),
        "low_battery" => value.Contains("low_battery", StringComparison.Ordinal),
        "scroll_mode" => value.Contains("scroll_mode", StringComparison.Ordinal),
        "scroll_acceleration" => value.Contains("scroll_acceleration", StringComparison.Ordinal),
        "scroll_smart_reel" => value.Contains("scroll_smart_reel", StringComparison.Ordinal),
        "brightness" => value.Contains("brightness", StringComparison.Ordinal),
        "effect" => value.Contains("effect", StringComparison.Ordinal),
        "custom_frame" => value.Contains("custom_frame", StringComparison.Ordinal) || value == "set_key_row",
        _ => false,
    };
}
