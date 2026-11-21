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
                IsRzDevice30Family(productId) ? null : sibling.MatrixDimensions, sibling.DeviceWait,
                copiedTransactions, copiedPrefixes, sibling.DefaultStorage, sibling.DefaultLedZone,
                copiedSequences, copiedSizes, supportsIndependentDpi);

        bool ExcludeLighting(string name) => (productId == 0x00E0 || IsRzDevice30Family(productId)) &&
            (name.Contains("brightness", StringComparison.Ordinal) || name.Contains("effect", StringComparison.Ordinal) ||
             name.Contains("led", StringComparison.Ordinal) || name.Contains("matrix", StringComparison.Ordinal) ||
             name == "set_key_row");
    }

    private static FrozenSet<string> GetMiddlewareMouseWriteEvidence(ushort productId) => (productId switch
    {
        0x0063 or 0x0074 => new[] { "dpi", "poll" },
        0x0069 or 0x0075 or 0x0093 or 0x00E7 or 0x00E8 =>
            ["dpi", "dpi_stages", "poll", "idle", "low_battery"],
        0x00E0 => ["dpi", "dpi_stages", "poll", "idle", "low_battery", "battery_curve"],
        0x00DD or 0x00E5 or 0x00E6 or 0x00EF or 0x00F0 or 0xB005 or 0xB007 =>
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

    // rzDevice30 drives the Basilisk V4 family through 0x10:0x03/0x04. The 0x0F matrix
    // frame inherited from the 35K sibling is answered with busy and leaves a receipt that
    // poisons later reads, so matrix lighting stays off until 0x10 is verified on hardware.
    internal static bool IsRzDevice30Family(ushort productId) =>
        productId is 0x00E2 or 0x00E3 or 0x00EA or 0x00EB;

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
        "battery_curve" => value.Contains("battery_discharge_curve", StringComparison.Ordinal),
        "scroll_mode" => value.Contains("scroll_mode", StringComparison.Ordinal),
        "scroll_acceleration" => value.Contains("scroll_acceleration", StringComparison.Ordinal),
        "scroll_smart_reel" => value.Contains("scroll_smart_reel", StringComparison.Ordinal),
        "brightness" => value.Contains("brightness", StringComparison.Ordinal),
        "effect" => value.Contains("effect", StringComparison.Ordinal),
        "custom_frame" => value.Contains("custom_frame", StringComparison.Ordinal) || value == "set_key_row",
        _ => false,
    };

    /// <summary>
    /// The official oD() check sends 00:C0/00:40 to a body only when
    /// isHyperpollingDevice is set, and to a dongle when isHyperpollingWireless is set
    /// or the dongle is a dual-link HyperPolling master. These are the dongle halves of
    /// bundles whose feature is on but whose RazerHid rows still declare only the
    /// legacy 00:85/00:05 pair. Bodies stay on legacy; 00DF stays legacy because its
    /// bundle sets isHyperpollingWireless:false.
    /// </summary>
    private static void AddDongleHighRatePolling(IDictionary<ushort, RazerHidDeviceDefinition> devices)
    {
        ushort[] productIds =
        [
            0x00A6, // Viper V2 Pro
            0x00AB, // Basilisk V3 Pro
            0x00B0, // Cobra Pro
            0x00B7, // DeathAdder V3 Pro
            0x00C5, // DeathAdder V3 HyperSpeed
            0x00CD, // Basilisk V3 Pro 35K
            0x00DB, // Cobra HyperSpeed
            0x00E8, // Naga V3 Pro
            0xB00B, // Basilisk V3 Pro 35K Xbox 25th Anniversary Edition
        ];
        foreach (var productId in productIds)
        {
            AddHighRatePolling(devices, productId);
        }
    }

    private static void AddHighRatePolling(
        IDictionary<ushort, RazerHidDeviceDefinition> devices,
        ushort productId)
    {
        if (!devices.TryGetValue(productId, out var device))
        {
            return;
        }

        var transactions = new Dictionary<string, byte>(device.Transactions, StringComparer.Ordinal)
        {
            ["razer_chroma_misc_get_polling_rate2"] = 0x00,
            ["razer_chroma_misc_set_polling_rate2"] = 0x00,
        };
        var prefixes = new Dictionary<string, IReadOnlyList<ReadOnlyMemory<byte>>>(
            device.BuilderArgumentPrefixes, StringComparer.Ordinal)
        {
            ["razer_chroma_misc_get_polling_rate2"] = [new ReadOnlyMemory<byte>([0x01])],
            ["razer_chroma_misc_set_polling_rate2"] = [new ReadOnlyMemory<byte>([0x01])],
        };
        var commandSizes = new Dictionary<string, byte>(device.CommandSizes, StringComparer.Ordinal)
        {
            ["razer_chroma_misc_get_polling_rate2"] = 0x02,
            ["razer_chroma_misc_set_polling_rate2"] = 0x02,
        };

        devices[productId] = new RazerHidDeviceDefinition(
            device.ProductId,
            device.DisplayName,
            device.Category,
            device.SourceCapabilities,
            device.MaximumDpi,
            device.PollingRates,
            device.MatrixDimensions,
            device.DeviceWait,
            transactions,
            prefixes,
            device.DefaultStorage,
            device.DefaultLedZone,
            device.CommandSequences,
            commandSizes,
            // Rebuilding the definition must not drop evidence the bundle already gave us.
            device.SupportsIndependentDpi,
            device.UsesP1DpiScale,
            device.UsesLegacyInfo,
            device.UsesDynamicTransactions,
            device.WaveAxis);
    }
}
