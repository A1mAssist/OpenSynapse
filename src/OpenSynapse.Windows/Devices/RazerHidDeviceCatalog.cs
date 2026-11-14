using System.Collections.ObjectModel;
using System.Collections.Frozen;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using OpenSynapse.Core.Devices;
using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.Windows.Devices;

public sealed record RazerHidMatrixDimensions(byte Rows, byte Columns);

/// <summary>
/// The axis a device's Wave effect travels along. The wire direction ids differ per axis:
/// left/right is 1/2, up/down is 3/4, and clockwise/counter-clockwise is 1/2. The value
/// comes from the product bundle's <c>WAVE_DIRECTION</c> declaration.
/// </summary>
public enum RazerWaveDirectionAxis
{
    Unknown,
    LeftRight,
    UpDown,
    Clockwise,
}

internal sealed record RazerHidCommandStep(byte TransactionId, byte FixedArgument0);

public sealed class RazerHidDeviceDefinition
{
    internal RazerHidDeviceDefinition(
        ushort productId,
        string displayName,
        DeviceCategory category,
        IReadOnlySet<string> capabilities,
        int? maximumDpi,
        IReadOnlyList<int> pollingRates,
        RazerHidMatrixDimensions? matrixDimensions,
        TimeSpan deviceWait,
        IReadOnlyDictionary<string, byte> transactions,
        IReadOnlyDictionary<string, IReadOnlyList<ReadOnlyMemory<byte>>> builderArgumentPrefixes,
        RazerHidStorage defaultStorage,
        RazerHidLedZone defaultLedZone,
        IReadOnlyDictionary<string, IReadOnlyList<RazerHidCommandStep>> commandSequences,
        IReadOnlyDictionary<string, byte>? commandSizes = null,
        bool supportsIndependentDpi = true,
        bool usesP1DpiScale = false,
        bool usesLegacyInfo = false,
        bool usesDynamicTransactions = false,
        RazerWaveDirectionAxis waveAxis = RazerWaveDirectionAxis.Unknown)
    {
        ProductId = productId;
        DisplayName = displayName;
        Category = category;
        SourceCapabilities = capabilities;
        MaximumDpi = maximumDpi;
        PollingRates = pollingRates;
        MatrixDimensions = matrixDimensions;
        LayoutSupport = RazerHidLayoutCatalog.GetSupport(matrixDimensions, capabilities.Contains("set_key_row"));
        DeviceWait = deviceWait;
        Transactions = transactions;
        BuilderArgumentPrefixes = builderArgumentPrefixes;
        DefaultStorage = defaultStorage;
        DefaultLedZone = defaultLedZone;
        CommandSequences = commandSequences;
        CommandSizes = commandSizes ?? FrozenDictionary<string, byte>.Empty;
        SupportsIndependentDpi = supportsIndependentDpi;
        UsesP1DpiScale = usesP1DpiScale;
        UsesLegacyInfo = usesLegacyInfo;
        UsesDynamicTransactions = usesDynamicTransactions;
        WaveAxis = waveAxis;
    }

    public const ushort VendorId = 0x1532;
    public ushort ProductId { get; }
    public string DisplayName { get; }
    public DeviceCategory Category { get; }
    internal IReadOnlySet<string> SourceCapabilities { get; }
    public int? MaximumDpi { get; }
    public IReadOnlyList<int> PollingRates { get; }
    public RazerHidMatrixDimensions? MatrixDimensions { get; }
    public RazerHidLayoutSupport LayoutSupport { get; }
    public TimeSpan DeviceWait { get; }
    public RazerHidStorage DefaultStorage { get; }
    public RazerHidLedZone DefaultLedZone { get; }
    internal IReadOnlyDictionary<string, byte> Transactions { get; }
    internal IReadOnlyDictionary<string, IReadOnlyList<ReadOnlyMemory<byte>>> BuilderArgumentPrefixes { get; }
    internal IReadOnlyDictionary<string, IReadOnlyList<RazerHidCommandStep>> CommandSequences { get; }
    internal IReadOnlyDictionary<string, byte> CommandSizes { get; }
    internal bool SupportsIndependentDpi { get; }
    internal bool UsesP1DpiScale { get; }
    internal bool UsesLegacyInfo { get; }
    internal bool UsesDynamicTransactions { get; }
    public RazerWaveDirectionAxis WaveAxis { get; }

    internal bool HasSourceCapability(string capability) => SourceCapabilities.Contains(capability);

    internal byte GetRequiredTransaction(string builderName) =>
        Transactions.TryGetValue(builderName, out var transactionId)
            ? transactionId
            : throw new NotSupportedException(
                $"RazerHid device 1532:{ProductId:X4} has no unambiguous transaction for '{builderName}'.");

    internal RazerHidDeviceDefinition WithDisplayName(string displayName) =>
        new(ProductId, displayName, Category, SourceCapabilities, MaximumDpi, PollingRates,
            MatrixDimensions, DeviceWait, Transactions, BuilderArgumentPrefixes, DefaultStorage,
            DefaultLedZone, CommandSequences, CommandSizes, SupportsIndependentDpi, UsesP1DpiScale,
            UsesLegacyInfo, UsesDynamicTransactions, WaveAxis);

    internal byte GetRequiredFixedArgument(string builderName, int argumentIndex)
    {
        if (argumentIndex < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(argumentIndex));
        }
        if (!BuilderArgumentPrefixes.TryGetValue(builderName, out var prefixes))
        {
            throw new NotSupportedException(
                $"RazerHid device 1532:{ProductId:X4} has no fixed arguments for '{builderName}'.");
        }
        var values = prefixes
            .Where(prefix => prefix.Length > argumentIndex)
            .Select(prefix => prefix.Span[argumentIndex])
            .Distinct()
            .ToArray();
        return values.Length == 1
            ? values[0]
            : throw new NotSupportedException(
                $"RazerHid device 1532:{ProductId:X4} has no single fixed argument {argumentIndex} for '{builderName}'.");
    }

    internal byte? GetFixedArgumentOrNull(string builderName, int argumentIndex) =>
        BuilderArgumentPrefixes.ContainsKey(builderName)
            ? GetRequiredFixedArgument(builderName, argumentIndex)
            : null;

    internal byte GetCommandSize(string builderName, byte fallback) =>
        CommandSizes.TryGetValue(builderName, out var size) ? size : fallback;
}

public sealed partial class RazerHidDeviceCatalog
{
    internal const string SourceCommit = "477f4d6d3ab7e9deb6310d5bd3742560c5cbe80d";
    private const int ExpectedDeviceCount = 263;
    private static readonly ushort[] ReservedProductIds = [0x00B8, 0x02C6];
    private static readonly HashSet<ushort> MappingProductIds = [];
    private static readonly string[] ResourceSuffixes =
    [
        ".Devices.RazerHid.accessories.json",
        ".Devices.RazerHid.keyboards.json",
        ".Devices.RazerHid.laptops.json",
        ".Devices.RazerHid.mice.json",
    ];
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };
    private static readonly Lazy<RazerHidDeviceCatalog> BuiltInValue = new(LoadBuiltIn);
    private readonly IReadOnlyDictionary<ushort, RazerHidDeviceDefinition> _byProductId;

    private RazerHidDeviceCatalog(IReadOnlyDictionary<ushort, RazerHidDeviceDefinition> byProductId)
    {
        _byProductId = byProductId;
        Devices = new ReadOnlyCollection<RazerHidDeviceDefinition>(
            byProductId.Values.OrderBy(device => device.ProductId).ToArray());
    }

    public static RazerHidDeviceCatalog BuiltIn => BuiltInValue.Value;
    public IReadOnlyList<RazerHidDeviceDefinition> Devices { get; }

    internal static bool IsLeviathanV2X(ushort productId) => productId == LeviathanV2XProtocol.ProductId;

    /// <summary>
    /// Known middleware mapping products. This is a hint for diagnostics and tests; the
    /// runtime button-list probe decides whether the mapping page is exposed.
    /// </summary>
    internal static bool HasOnboardMapping(ushort productId)
    {
        _ = BuiltIn;
        return MappingProductIds.Contains(productId);
    }
    public RazerHidDeviceDefinition? Find(ushort vendorId, ushort productId) =>
        vendorId == RazerHidDeviceDefinition.VendorId
            ? _byProductId.GetValueOrDefault(productId)
            : null;

    private static RazerHidDeviceCatalog LoadBuiltIn()
    {
        var assembly = typeof(RazerHidDeviceCatalog).Assembly;
        var resourceNames = assembly.GetManifestResourceNames();
        var devices = new Dictionary<ushort, RazerHidDeviceDefinition>();

        foreach (var suffix in ResourceSuffixes)
        {
            var resourceName = resourceNames.SingleOrDefault(name => name.EndsWith(suffix, StringComparison.Ordinal))
                ?? throw new InvalidOperationException($"Missing embedded RazerHid catalog resource '{suffix}'.");
            using var stream = assembly.GetManifestResourceStream(resourceName)
                ?? throw new InvalidOperationException($"Cannot open embedded RazerHid catalog resource '{resourceName}'.");
            var document = JsonSerializer.Deserialize<CatalogDocument>(stream, JsonOptions)
                ?? throw new InvalidOperationException($"RazerHid catalog '{resourceName}' is empty.");
            AddDocument(document, resourceName, devices);
        }

        if (devices.Count != ExpectedDeviceCount)
        {
            throw new InvalidOperationException(
                $"RazerHid catalog contains {devices.Count} devices; expected {ExpectedDeviceCount}.");
        }
        if (ReservedProductIds.Any(devices.ContainsKey))
        {
            throw new InvalidOperationException("RazerHid catalog claims an existing Blade or Viper product ID.");
        }

        AddLocallyAdmittedDevices(devices);

        return new RazerHidDeviceCatalog(devices);
    }

    private static void AddDocument(
        CatalogDocument document,
        string sourceName,
        IDictionary<ushort, RazerHidDeviceDefinition> output)
    {
        if (document.SchemaVersion != 1 ||
            document.Source.Repository != "openrazer/openrazer" ||
            document.Source.Commit != SourceCommit)
        {
            throw new InvalidOperationException($"RazerHid catalog '{sourceName}' has an unsupported schema or source.");
        }

        foreach (var device in document.Devices)
        {
            if (!ushort.TryParse(device.ProductId, System.Globalization.NumberStyles.HexNumber, null, out var productId) ||
                string.IsNullOrWhiteSpace(device.DisplayName) ||
                !document.CapabilitySets.TryGetValue(device.CapabilitySet, out var capabilities) ||
                device.WaitMicroseconds is < 0 or > 1_000_000 ||
                device.AmbiguousTransactions.Intersect(device.Transactions.Keys, StringComparer.Ordinal).Any() ||
                device.AmbiguousTransactions.Intersect(device.CommandSequences.Keys, StringComparer.Ordinal).Any() ||
                device.Transactions.Keys.Intersect(device.CommandSequences.Keys, StringComparer.Ordinal).Any())
            {
                throw new InvalidOperationException($"RazerHid catalog '{sourceName}' contains an invalid device entry.");
            }

            var capabilitySet = capabilities.ToFrozenSet(StringComparer.Ordinal);
            if (capabilitySet.Count != capabilities.Length || capabilitySet.Any(string.IsNullOrWhiteSpace))
            {
                throw new InvalidOperationException($"RazerHid catalog '{sourceName}' contains invalid capabilities.");
            }

            var transactions = new Dictionary<string, byte>(StringComparer.Ordinal);
            foreach (var (builder, value) in device.Transactions)
            {
                if (string.IsNullOrWhiteSpace(builder) ||
                    !byte.TryParse(value, System.Globalization.NumberStyles.HexNumber, null, out var transactionId))
                {
                    throw new InvalidOperationException($"RazerHid catalog '{sourceName}' contains an invalid transaction.");
                }
                transactions.Add(builder, transactionId);
            }

            var builderArgumentPrefixes = new Dictionary<string, IReadOnlyList<ReadOnlyMemory<byte>>>(StringComparer.Ordinal);
            foreach (var (builder, values) in device.BuilderArgumentPrefixes)
            {
                if (string.IsNullOrWhiteSpace(builder) || values.Length == 0)
                {
                    throw new InvalidOperationException($"RazerHid catalog '{sourceName}' contains invalid builder arguments.");
                }
                var prefixes = values.Select(value =>
                {
                    try
                    {
                        var bytes = Convert.FromHexString(value);
                        return bytes.Length is > 0 and <= 80
                            ? new ReadOnlyMemory<byte>(bytes)
                            : throw new FormatException();
                    }
                    catch (FormatException)
                    {
                        throw new InvalidOperationException(
                            $"RazerHid catalog '{sourceName}' contains invalid builder arguments.");
                    }
                }).ToArray();
                builderArgumentPrefixes.Add(builder, Array.AsReadOnly(prefixes));
            }

            CompleteClassicLedFacts(productId, capabilitySet, transactions, builderArgumentPrefixes);

            var commandSequences = new Dictionary<string, IReadOnlyList<RazerHidCommandStep>>(StringComparer.Ordinal);
            foreach (var (builder, steps) in device.CommandSequences)
            {
                if (string.IsNullOrWhiteSpace(builder) || steps.Length < 2 ||
                    steps.Any(step =>
                        !byte.TryParse(step.TransactionId, System.Globalization.NumberStyles.HexNumber, null, out _) ||
                        !byte.TryParse(step.FixedArgument0, System.Globalization.NumberStyles.HexNumber, null, out _)))
                {
                    throw new InvalidOperationException($"RazerHid catalog '{sourceName}' contains an invalid command sequence.");
                }
                commandSequences.Add(builder, Array.AsReadOnly(steps.Select(step => new RazerHidCommandStep(
                    byte.Parse(step.TransactionId, System.Globalization.NumberStyles.HexNumber),
                    byte.Parse(step.FixedArgument0, System.Globalization.NumberStyles.HexNumber))).ToArray()));
            }

            var commandSizes = new Dictionary<string, byte>(StringComparer.Ordinal);
            foreach (var (builder, value) in device.CommandSizes ?? [])
            {
                if (string.IsNullOrWhiteSpace(builder) ||
                    !transactions.ContainsKey(builder) ||
                    !byte.TryParse(value, System.Globalization.NumberStyles.HexNumber, null, out var size) ||
                    size is 0 or > 80)
                {
                    throw new InvalidOperationException($"RazerHid catalog '{sourceName}' contains invalid command sizes.");
                }
                commandSizes.Add(builder, size);
            }

            RazerHidMatrixDimensions? matrix = device.MatrixDimensions switch
            {
                null => null,
                [> 0 and <= byte.MaxValue, > 0 and <= byte.MaxValue] dimensions =>
                    new((byte)dimensions[0], (byte)dimensions[1]),
                _ => throw new InvalidOperationException($"RazerHid catalog '{sourceName}' contains invalid matrix dimensions."),
            };
            var waveAxis = device.WaveDirection switch
            {
                null => RazerWaveDirectionAxis.Unknown,
                "LeftRight" => RazerWaveDirectionAxis.LeftRight,
                "UpDown" => RazerWaveDirectionAxis.UpDown,
                "CWCCW" => RazerWaveDirectionAxis.Clockwise,
                _ => throw new InvalidOperationException(
                    $"RazerHid catalog '{sourceName}' contains an invalid wave direction."),
            };
            var definition = new RazerHidDeviceDefinition(
                productId,
                device.DisplayName.Trim(),
                ParseCategory(device.Category),
                capabilitySet,
                device.MaximumDpi,
                Array.AsReadOnly(device.PollRates ?? []),
                matrix,
                TimeSpan.FromMicroseconds(device.WaitMicroseconds),
                transactions.ToFrozenDictionary(StringComparer.Ordinal),
                builderArgumentPrefixes.ToFrozenDictionary(StringComparer.Ordinal),
                ParseStorage(device.DefaultStorage),
                ParseLedZone(device.DefaultLedZone),
                commandSequences.ToFrozenDictionary(StringComparer.Ordinal),
                commandSizes.ToFrozenDictionary(StringComparer.Ordinal),
                usesDynamicTransactions: device.DynamicTransactions ?? false,
                waveAxis: waveAxis);
            if (!output.TryAdd(productId, definition))
            {
                throw new InvalidOperationException($"Duplicate RazerHid product ID 1532:{productId:X4}.");
            }
        }
    }

    /// <summary>
    /// Admits product ids RazerHid has not catalogued so a read-only probe can reach the
    /// firmware and record what it answers. These entries deliberately declare only the
    /// read half of a known sibling device: no set_* builder is present, so every write
    /// path stays closed until this exact product id has on-device evidence.
    /// </summary>
    private static void AddLocallyAdmittedDevices(IDictionary<ushort, RazerHidDeviceDefinition> devices)
    {
        // DeathAdder 2000 RZ01-0145 ships as 1532:004F, which RazerHid catalogues. Two
        // independent USB ID databases also report 1532:005F as the same product, and
        // 1532:004D as a Cynosa Pro bundle variant. Upstream defines no entry for either,
        // so a 005F owner currently gets no device page at all.
        const ushort deathAdder2000 = 0x004F;
        foreach (var productId in new ushort[] { 0x005F, 0x004D })
        {
            if (!devices.TryGetValue(deathAdder2000, out var sibling) ||
                devices.ContainsKey(productId))
            {
                continue;
            }

            // An explicit read allowlist, not a write denylist. A builder this file has not
            // seen must default to excluded, so a future RazerHid entry cannot leak a write
            // into the probe.
            string[] readBuilders =
            [
                "razer_chroma_standard_get_firmware_version",
                "razer_chroma_standard_get_serial",
                "razer_chroma_standard_get_device_mode",
                "razer_chroma_misc_get_dpi_xy",
                "razer_chroma_misc_get_polling_rate",
                "razer_chroma_standard_get_led_brightness",
            ];
            var readOnly = sibling.Transactions
                .Where(entry => readBuilders.Contains(entry.Key, StringComparer.Ordinal))
                .ToFrozenDictionary(StringComparer.Ordinal);
            var readOnlyPrefixes = sibling.BuilderArgumentPrefixes
                .Where(entry => readOnly.ContainsKey(entry.Key))
                .ToFrozenDictionary(StringComparer.Ordinal);
            var readOnlyCommandSizes = sibling.CommandSizes
                .Where(entry => readOnly.ContainsKey(entry.Key))
                .ToFrozenDictionary(StringComparer.Ordinal);

            devices.Add(productId, new RazerHidDeviceDefinition(
                productId,
                $"{sibling.DisplayName} (read-only probe)",
                sibling.Category,
                sibling.SourceCapabilities,
                sibling.MaximumDpi,
                Array.AsReadOnly(Array.Empty<int>()), // Read-only probe: no writable ladder.
                sibling.MatrixDimensions,
                sibling.DeviceWait,
                readOnly,
                readOnlyPrefixes,
                sibling.DefaultStorage,
                sibling.DefaultLedZone,
                new Dictionary<string, IReadOnlyList<RazerHidCommandStep>>(),
                readOnlyCommandSizes));
        }

        AddLeviathanV2X();

        // Upstream supports 00DE/00DF as Viper V3 Pro SE aliases. Windows reports their
        // feature report as 91 bytes including the report-id byte, same as the V3 Pro.
        // The wireless SE firmware rejects the V3 Pro's high-rate polling commands
        // (00:C0 / 00:40) and only answers the legacy 00:85 / 00:05 pair.
        AddViperV3ProSeAlias(0x00C0, 0x00DE, "Razer Viper V3 Pro SE (Wired)");
        AddViperV3ProSeAlias(0x00C1, 0x00DF, "Razer Viper V3 Pro SE (Wireless)",
            legacyPolling: true);

        AddMiddlewareMice();
        AddLegacyReadProbes(devices);

        // The official 00AA bundle declares productId 170 and dongleId 171 with
        // isOBMDevice:true, so both wired and wireless identities expose the OBM pages.
        MappingProductIds.Add(0x00AA);
        MappingProductIds.Add(0x00AB);

        void AddMiddlewareMice()
        {
            (ushort Sibling, ushort Product, string Name, int MaxDpi, int[] Rates)[] entries =
            [
                (0x006A, 0x0063, "Razer Jugan", 7200, []),
                (0x0073, 0x0069, "Razer Mamba Hyperflux", 16000, [125, 500, 1000]),
                (0x006B, 0x0074, "Razer Abyssus Lite", 6400, [125, 500, 1000]),
                (0x005A, 0x0075, "Razer Turret Mouse Xbox One Edition", 16000, [125, 500, 1000]),
                (0x0053, 0x0093, "Razer Naga Classic Edition", 16000, [125, 500, 1000]),
                (0x00BF, 0x00DD, "Razer Boomslang 20th Anniversary Edition", 45000, [125, 500, 1000, 2000, 4000, 8000]),
                (0x00DB, 0x00E0, "Razer Orochi V2", 18000, [125, 500, 1000]),
                (0x00CD, 0x00E2, "Razer Basilisk V4 Pro (Wired)", 50000, [125, 500, 1000, 2000, 4000, 8000]),
                (0x00CD, 0x00E3, "Razer Basilisk V4 Pro (Wireless)", 50000, [125, 500, 1000, 2000, 4000, 8000]),
                (0x00C1, 0x00E5, "Razer Viper V4 Pro (Wired)", 50000, [125, 500, 1000, 2000, 4000, 8000]),
                (0x00C1, 0x00E6, "Razer Viper V4 Pro (Wireless)", 50000, [125, 500, 1000, 2000, 4000, 8000]),
                (0x00A8, 0x00E7, "Razer Naga V3 Pro (Wired)", 30000, [125, 500, 1000]),
                (0x00A8, 0x00E8, "Razer Naga V3 Pro (Wireless)", 30000, [125, 500, 1000]),
                (0x00CD, 0x00EA, "Razer Basilisk V4 HyperSpeed (Wireless)", 33000, [125, 500, 1000, 2000, 4000, 8000]),
                (0x00CD, 0x00EB, "Razer Basilisk V4 HyperSpeed (Wired)", 33000, [125, 500, 1000, 2000, 4000, 8000]),
                (0x00BF, 0x00EF, "Razer DeathAdder V4 Pro Carbon Fiber Edition (Wired)", 50000, [125, 500, 1000, 2000, 4000, 8000]),
                (0x00BF, 0x00F0, "Razer DeathAdder V4 Pro Carbon Fiber Edition (Wireless)", 50000, [125, 500, 1000, 2000, 4000, 8000]),
                (0x00A3, 0x00A2, "Razer Cobra", 8500, [125, 500, 1000]),
                // Viper V4 HyperSpeed's bundle declares productId == dongleId 45061 and a BLE
                // identity 45062, so one USB entry covers both the cable and the receiver.
                (0x00C1, 0xB005, "Razer Viper V4 HyperSpeed", 33000, [125, 500, 1000, 2000, 4000, 8000]),
                // DeathAdder V4 HyperSpeed declares 45063 for both cable and receiver, BLE 45064.
                (0x00BF, 0xB007, "Razer DeathAdder V4 HyperSpeed", 33000, [125, 500, 1000, 2000, 4000, 8000]),
                // The Xbox 25th Anniversary edition is the 35K identity 45066 with the
                // receiver on 45067; its DeviceInfo declares 4 OBM slots and scroll lighting.
                (0x00CC, 0xB00A, "Razer Basilisk V3 Pro 35K - Xbox 25 Anniversary Edition (Wired)", 35000, [125, 500, 1000]),
                (0x00CD, 0xB00B, "Razer Basilisk V3 Pro 35K - Xbox 25 Anniversary Edition (Wireless)", 35000, [125, 500, 1000]),
            ];

            foreach (var entry in entries)
            {
                if (!devices.TryGetValue(entry.Sibling, out var sibling) ||
                    devices.ContainsKey(entry.Product))
                {
                    continue;
                }

                devices.Add(entry.Product, CreateMiddlewareMouseDefinition(
                    sibling, entry.Product, entry.Name, entry.MaxDpi, entry.Rates));
                MappingProductIds.Add(entry.Product);
            }

            // The Basilisk V4 middleware exposes the 8 kHz path through
            // 00:C0/00:40 with profile id 1. The catalog sibling is the 35K mouse,
            // whose RazerHid entry only declares the legacy 00:85/00:05 pair.
            // Keep the shared definition and add the reports the products actually use.
            AddHighRatePolling(0x00E2);
            AddHighRatePolling(0x00E3);
            AddHighRatePolling(0x00EA);
            AddHighRatePolling(0x00EB);

            void AddHighRatePolling(ushort productId)
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
                    commandSizes);
            }
        }

        void AddLeviathanV2X()
        {
            if (devices.ContainsKey(LeviathanV2XProtocol.ProductId))
            {
                return;
            }

            // The 1354 middleware uses the same 91-byte feature-report envelope as the
            // RazerHid catalog. Only the identity and brightness half is admitted here;
            // EQ, bass and input-source controls live in the dedicated audio overlay.
            var capabilities = new HashSet<string>(StringComparer.Ordinal)
            {
                "get_firmware",
                "get_serial",
                "get_brightness",
                "set_brightness",
                // The soundbar keeps its Chroma channel on class 0x0F, so only the effect
                // selectors the 1354 payload can express are admitted.
                "set_none_effect",
                "set_static_effect",
                "set_spectrum_effect",
                "set_wave_effect",
                "set_breath_dual_effect",
            };
            var transactions = new Dictionary<string, byte>(StringComparer.Ordinal)
            {
                // The transport replaces these placeholders with the 054A transaction
                // sequence (0..31) before sending.
                ["razer_chroma_standard_get_firmware_version"] = 0x00,
                ["razer_chroma_standard_get_serial"] = 0x00,
                ["razer_standard_get_edition_info"] = 0x00,
                ["razer_chroma_extended_matrix_get_region_ids"] = 0x00,
                ["razer_chroma_extended_matrix_get_brightness"] = 0x00,
                ["razer_chroma_extended_matrix_brightness"] = 0x00,
                ["razer_chroma_extended_matrix_effect_none"] = 0x00,
                ["razer_chroma_extended_matrix_effect_static"] = 0x00,
                ["razer_chroma_extended_matrix_effect_spectrum"] = 0x00,
                ["razer_chroma_extended_matrix_effect_wave"] = 0x00,
                ["razer_chroma_extended_matrix_effect_breathing_dual"] = 0x00,
            };
            var zonePrefix = Array.AsReadOnly(
                new[] { new ReadOnlyMemory<byte>(new byte[] { 0x00, 0x00 }) });
            var prefixes = new Dictionary<string, IReadOnlyList<ReadOnlyMemory<byte>>>(StringComparer.Ordinal)
            {
                // Profile 0 is the same profile the soundbar panel reads and writes; the
                // runtime replaces the region byte with the id it reports.
                ["razer_chroma_extended_matrix_get_brightness"] = zonePrefix,
                ["razer_chroma_extended_matrix_brightness"] = zonePrefix,
                ["razer_chroma_extended_matrix_effect_none"] = zonePrefix,
                ["razer_chroma_extended_matrix_effect_static"] = zonePrefix,
                ["razer_chroma_extended_matrix_effect_spectrum"] = zonePrefix,
                ["razer_chroma_extended_matrix_effect_wave"] = zonePrefix,
                ["razer_chroma_extended_matrix_effect_breathing_dual"] = zonePrefix,
            };
            devices.Add(LeviathanV2XProtocol.ProductId, new RazerHidDeviceDefinition(
                LeviathanV2XProtocol.ProductId,
                "Razer Leviathan V2 X",
                DeviceCategory.Accessory,
                capabilities,
                null,
                Array.Empty<int>(),
                null,
                // The 1354 middleware uses SLEEP_TIME_BETWEEN_OUT_IN = 5 ms.
                TimeSpan.FromMilliseconds(5),
                transactions,
                prefixes,
                RazerHidStorage.Temporary,
                RazerHidLedZone.All,
                new Dictionary<string, IReadOnlyList<RazerHidCommandStep>>(StringComparer.Ordinal)));
        }

        void AddViperV3ProSeAlias(
            ushort siblingId,
            ushort productId,
            string displayName,
            bool legacyPolling = false)
        {
            if (!devices.TryGetValue(siblingId, out var sibling) ||
                devices.ContainsKey(productId))
            {
                return;
            }

            var transactions = sibling.Transactions;
            var pollingRates = sibling.PollingRates;
            var commandSequences = sibling.CommandSequences;
            var commandSizes = sibling.CommandSizes;
            if (legacyPolling)
            {
                var legacyTransactions = sibling.Transactions
                    .Where(entry => entry.Key is not
                        ("razer_chroma_misc_get_polling_rate2" or "razer_chroma_misc_set_polling_rate2"))
                    .ToDictionary(entry => entry.Key, entry => entry.Value, StringComparer.Ordinal);
                legacyTransactions["razer_chroma_misc_get_polling_rate"] = 0x1F;
                legacyTransactions["razer_chroma_misc_set_polling_rate"] = 0x1F;
                transactions = legacyTransactions.ToFrozenDictionary(StringComparer.Ordinal);
                pollingRates = Array.AsReadOnly([125, 500, 1000]);
                commandSequences = sibling.CommandSequences
                    .Where(entry => entry.Key != "razer_chroma_misc_set_polling_rate2")
                    .ToFrozenDictionary(StringComparer.Ordinal);
                // A command size is only valid for a command the device declares.
                commandSizes = sibling.CommandSizes
                    .Where(entry => entry.Key is not
                        ("razer_chroma_misc_get_polling_rate2" or "razer_chroma_misc_set_polling_rate2"))
                    .ToFrozenDictionary(StringComparer.Ordinal);
            }

            devices.Add(productId, new RazerHidDeviceDefinition(
                productId,
                displayName,
                sibling.Category,
                sibling.SourceCapabilities,
                sibling.MaximumDpi,
                pollingRates,
                sibling.MatrixDimensions,
                sibling.DeviceWait,
                transactions,
                sibling.BuilderArgumentPrefixes,
                sibling.DefaultStorage,
                sibling.DefaultLedZone,
                commandSequences,
                commandSizes));
        }
    }

    private static void CompleteClassicLedFacts(
        ushort productId,
        IReadOnlySet<string> capabilities,
        IDictionary<string, byte> transactions,
        IDictionary<string, IReadOnlyList<ReadOnlyMemory<byte>>> prefixes)
    {
        ushort[] classicColorMice = [0x001F, 0x0024, 0x0025, 0x0037, 0x003E, 0x003F, 0x0043, 0x0054, 0x005B];
        if (classicColorMice.Contains(productId))
        {
            AddTransaction("razer_chroma_standard_set_led_rgb", 0x3F);
            AddPrefix("razer_chroma_standard_set_led_rgb", new byte[] { 0x01 });
        }

        // Static is the base effect of every lighting family. The generated inventory drops
        // it on many devices even though none/wave/... prove the family exists, so reuse
        // the none builder's transaction and prefixes.
        foreach (var family in new[]
        {
            "razer_chroma_standard_matrix_effect_",
            "razer_chroma_extended_matrix_effect_",
            "razer_chroma_mouse_extended_matrix_effect_",
        })
        {
            if (!transactions.TryGetValue(family + "none", out var noneTransaction) ||
                transactions.ContainsKey(family + "static"))
            {
                continue;
            }
            AddTransaction(family + "static", noneTransaction);
            if (prefixes.TryGetValue(family + "none", out var nonePrefixes))
            {
                foreach (var prefix in nonePrefixes) AddPrefix(family + "static", prefix);
            }
        }

        // RazerHid's Anansi static and macro branches issue these as the second/third
        // report; the source-derived single-builder inventory cannot attribute them.
        if (productId == 0x010F)
        {
            AddTransaction("razer_chroma_standard_set_led_rgb", 0xFF);
            AddPrefix("razer_chroma_standard_set_led_rgb", new byte[] { 0x01, 0x05 });
            AddTransaction("razer_chroma_standard_set_led_blinking", 0xFF);
            AddPrefix("razer_chroma_standard_set_led_blinking", new byte[] { 0x00, 0x07 });
        }

        // Both read handlers use transaction FF; their storage/LED arguments are fixed
        // in the upstream driver even though the call sites have no PID switch.
        if (capabilities.Contains("get_macro_effect"))
        {
            AddTransaction("razer_chroma_standard_get_led_effect", 0xFF);
            AddPrefix("razer_chroma_standard_get_led_effect", new byte[] { 0x01, 0x07 });
        }
        if (capabilities.Contains("get_logo_active"))
        {
            AddTransaction("razer_chroma_standard_get_led_effect", 0xFF);
            AddPrefix("razer_chroma_standard_get_led_effect", new byte[] { 0x01, 0x04 });
        }

        void AddPrefix(string builder, ReadOnlyMemory<byte> prefix)
        {
            if (!prefixes.TryGetValue(builder, out var existing))
            {
                prefixes.Add(builder, Array.AsReadOnly([prefix]));
                return;
            }
            if (!existing.Any(value => value.Span.SequenceEqual(prefix.Span)))
            {
                prefixes[builder] = Array.AsReadOnly(existing.Append(prefix).ToArray());
            }
        }

        void AddTransaction(string builder, byte transactionId)
        {
            if (!transactions.ContainsKey(builder))
            {
                transactions.Add(builder, transactionId);
            }
        }
    }

    private static DeviceCategory ParseCategory(string value) => value switch
    {
        "accessory" => DeviceCategory.Accessory,
        "keyboard" => DeviceCategory.Keyboard,
        "laptop" => DeviceCategory.Laptop,
        "mouse" => DeviceCategory.Mouse,
        "mouse-mat" => DeviceCategory.MouseMat,
        "monitor" => DeviceCategory.Monitor,
        _ => throw new InvalidOperationException($"Unsupported RazerHid category '{value}'."),
    };

    private static RazerHidStorage ParseStorage(string? value) => value switch
    {
        null => RazerHidStorage.Persistent,
        "00" => RazerHidStorage.Temporary,
        "01" => RazerHidStorage.Persistent,
        _ => throw new InvalidOperationException($"Unsupported RazerHid storage '{value}'."),
    };

    private static RazerHidLedZone ParseLedZone(string? value) => value switch
    {
        null => RazerHidLedZone.All,
        "00" => RazerHidLedZone.All,
        "01" => RazerHidLedZone.ScrollWheel,
        "04" => RazerHidLedZone.Logo,
        "05" => RazerHidLedZone.Backlight,
        "07" => RazerHidLedZone.Macro,
        "08" => RazerHidLedZone.Game,
        "10" => RazerHidLedZone.RightSide,
        "11" => RazerHidLedZone.LeftSide,
        "20" => RazerHidLedZone.Charging,
        "21" => RazerHidLedZone.FastCharging,
        "22" => RazerHidLedZone.FullyCharged,
        _ => throw new InvalidOperationException($"Unsupported RazerHid LED zone '{value}'."),
    };

    private sealed record CatalogDocument(
        int SchemaVersion,
        CatalogSource Source,
        Dictionary<string, string[]> CapabilitySets,
        CatalogDevice[] Devices);

    private sealed record CatalogSource(string Repository, string Commit);

    private sealed record CatalogDevice(
        string ProductId,
        string DisplayName,
        string Category,
        string CapabilitySet,
        int? MaximumDpi,
        int[]? PollRates,
        int[]? MatrixDimensions,
        int WaitMicroseconds,
        Dictionary<string, string> Transactions,
        Dictionary<string, string[]> BuilderArgumentPrefixes,
        string? DefaultStorage,
        string? DefaultLedZone,
        Dictionary<string, CatalogCommandStep[]> CommandSequences,
        Dictionary<string, string>? CommandSizes,
        string[] AmbiguousTransactions,
        bool? DynamicTransactions,
        string? WaveDirection);

    private sealed record CatalogCommandStep(string TransactionId, string FixedArgument0);
}
