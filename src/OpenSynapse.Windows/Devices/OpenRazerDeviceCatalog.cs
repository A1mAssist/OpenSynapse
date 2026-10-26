using System.Collections.ObjectModel;
using System.Collections.Frozen;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using OpenSynapse.Core.Devices;
using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.Windows.Devices;

public sealed record OpenRazerMatrixDimensions(byte Rows, byte Columns);

internal sealed record OpenRazerCommandStep(byte TransactionId, byte FixedArgument0);

public sealed class OpenRazerDeviceDefinition
{
    internal OpenRazerDeviceDefinition(
        ushort productId,
        string displayName,
        DeviceCategory category,
        IReadOnlySet<string> capabilities,
        int? maximumDpi,
        IReadOnlyList<int> pollingRates,
        OpenRazerMatrixDimensions? matrixDimensions,
        TimeSpan deviceWait,
        IReadOnlyDictionary<string, byte> transactions,
        IReadOnlyDictionary<string, IReadOnlyList<ReadOnlyMemory<byte>>> builderArgumentPrefixes,
        OpenRazerStorage defaultStorage,
        OpenRazerLedZone defaultLedZone,
        IReadOnlyDictionary<string, IReadOnlyList<OpenRazerCommandStep>> commandSequences)
    {
        ProductId = productId;
        DisplayName = displayName;
        Category = category;
        SourceCapabilities = capabilities;
        MaximumDpi = maximumDpi;
        PollingRates = pollingRates;
        MatrixDimensions = matrixDimensions;
        LayoutSupport = OpenRazerLayoutCatalog.GetSupport(matrixDimensions, capabilities.Contains("set_key_row"));
        DeviceWait = deviceWait;
        Transactions = transactions;
        BuilderArgumentPrefixes = builderArgumentPrefixes;
        DefaultStorage = defaultStorage;
        DefaultLedZone = defaultLedZone;
        CommandSequences = commandSequences;
    }

    public const ushort VendorId = 0x1532;
    public ushort ProductId { get; }
    public string DisplayName { get; }
    public DeviceCategory Category { get; }
    internal IReadOnlySet<string> SourceCapabilities { get; }
    public int? MaximumDpi { get; }
    public IReadOnlyList<int> PollingRates { get; }
    public OpenRazerMatrixDimensions? MatrixDimensions { get; }
    public OpenRazerLayoutSupport LayoutSupport { get; }
    public TimeSpan DeviceWait { get; }
    public OpenRazerStorage DefaultStorage { get; }
    public OpenRazerLedZone DefaultLedZone { get; }
    internal IReadOnlyDictionary<string, byte> Transactions { get; }
    internal IReadOnlyDictionary<string, IReadOnlyList<ReadOnlyMemory<byte>>> BuilderArgumentPrefixes { get; }
    internal IReadOnlyDictionary<string, IReadOnlyList<OpenRazerCommandStep>> CommandSequences { get; }

    internal bool HasSourceCapability(string capability) => SourceCapabilities.Contains(capability);

    internal byte GetRequiredTransaction(string builderName) =>
        Transactions.TryGetValue(builderName, out var transactionId)
            ? transactionId
            : throw new NotSupportedException(
                $"OpenRazer device 1532:{ProductId:X4} has no unambiguous transaction for '{builderName}'.");

    internal OpenRazerDeviceDefinition WithDisplayName(string displayName) =>
        new(ProductId, displayName, Category, SourceCapabilities, MaximumDpi, PollingRates,
            MatrixDimensions, DeviceWait, Transactions, BuilderArgumentPrefixes, DefaultStorage,
            DefaultLedZone, CommandSequences);

    internal byte GetRequiredFixedArgument(string builderName, int argumentIndex)
    {
        if (argumentIndex < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(argumentIndex));
        }
        if (!BuilderArgumentPrefixes.TryGetValue(builderName, out var prefixes))
        {
            throw new NotSupportedException(
                $"OpenRazer device 1532:{ProductId:X4} has no fixed arguments for '{builderName}'.");
        }
        var values = prefixes
            .Where(prefix => prefix.Length > argumentIndex)
            .Select(prefix => prefix.Span[argumentIndex])
            .Distinct()
            .ToArray();
        return values.Length == 1
            ? values[0]
            : throw new NotSupportedException(
                $"OpenRazer device 1532:{ProductId:X4} has no single fixed argument {argumentIndex} for '{builderName}'.");
    }
}

public sealed class OpenRazerDeviceCatalog
{
    internal const string SourceCommit = "477f4d6d3ab7e9deb6310d5bd3742560c5cbe80d";
    private const int ExpectedDeviceCount = 259;
    private static readonly ushort[] ReservedProductIds = [0x00B8, 0x02C6];
    private static readonly HashSet<ushort> MappingProductIds = [];
    private static readonly string[] ResourceSuffixes =
    [
        ".Devices.OpenRazer.accessories.json",
        ".Devices.OpenRazer.keyboards.json",
        ".Devices.OpenRazer.laptops.json",
        ".Devices.OpenRazer.mice.json",
    ];
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };
    private static readonly Lazy<OpenRazerDeviceCatalog> BuiltInValue = new(LoadBuiltIn);
    private readonly IReadOnlyDictionary<ushort, OpenRazerDeviceDefinition> _byProductId;

    private OpenRazerDeviceCatalog(IReadOnlyDictionary<ushort, OpenRazerDeviceDefinition> byProductId)
    {
        _byProductId = byProductId;
        Devices = new ReadOnlyCollection<OpenRazerDeviceDefinition>(
            byProductId.Values.OrderBy(device => device.ProductId).ToArray());
    }

    public static OpenRazerDeviceCatalog BuiltIn => BuiltInValue.Value;
    public IReadOnlyList<OpenRazerDeviceDefinition> Devices { get; }

    internal static bool IsLeviathanV2X(ushort productId) => productId == LeviathanV2XProtocol.ProductId;

    /// <summary>
    /// The middleware mice derive their onboard button-assignment commands from the
    /// product bundle instead of the OpenRazer snapshot; the runtime read still decides.
    /// </summary>
    internal static bool HasOnboardMapping(ushort productId)
    {
        _ = BuiltIn;
        return MappingProductIds.Contains(productId);
    }
    public OpenRazerDeviceDefinition? Find(ushort vendorId, ushort productId) =>
        vendorId == OpenRazerDeviceDefinition.VendorId
            ? _byProductId.GetValueOrDefault(productId)
            : null;

    private static OpenRazerDeviceCatalog LoadBuiltIn()
    {
        var assembly = typeof(OpenRazerDeviceCatalog).Assembly;
        var resourceNames = assembly.GetManifestResourceNames();
        var devices = new Dictionary<ushort, OpenRazerDeviceDefinition>();

        foreach (var suffix in ResourceSuffixes)
        {
            var resourceName = resourceNames.SingleOrDefault(name => name.EndsWith(suffix, StringComparison.Ordinal))
                ?? throw new InvalidOperationException($"Missing embedded OpenRazer catalog resource '{suffix}'.");
            using var stream = assembly.GetManifestResourceStream(resourceName)
                ?? throw new InvalidOperationException($"Cannot open embedded OpenRazer catalog resource '{resourceName}'.");
            var document = JsonSerializer.Deserialize<CatalogDocument>(stream, JsonOptions)
                ?? throw new InvalidOperationException($"OpenRazer catalog '{resourceName}' is empty.");
            AddDocument(document, resourceName, devices);
        }

        if (devices.Count != ExpectedDeviceCount)
        {
            throw new InvalidOperationException(
                $"OpenRazer catalog contains {devices.Count} devices; expected {ExpectedDeviceCount}.");
        }
        if (ReservedProductIds.Any(devices.ContainsKey))
        {
            throw new InvalidOperationException("OpenRazer catalog claims an existing Blade or Viper product ID.");
        }

        AddLocallyAdmittedDevices(devices);

        return new OpenRazerDeviceCatalog(devices);
    }

    private static void AddDocument(
        CatalogDocument document,
        string sourceName,
        IDictionary<ushort, OpenRazerDeviceDefinition> output)
    {
        if (document.SchemaVersion != 1 ||
            document.Source.Repository != "openrazer/openrazer" ||
            document.Source.Commit != SourceCommit)
        {
            throw new InvalidOperationException($"OpenRazer catalog '{sourceName}' has an unsupported schema or source.");
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
                throw new InvalidOperationException($"OpenRazer catalog '{sourceName}' contains an invalid device entry.");
            }

            var capabilitySet = capabilities.ToFrozenSet(StringComparer.Ordinal);
            if (capabilitySet.Count != capabilities.Length || capabilitySet.Any(string.IsNullOrWhiteSpace))
            {
                throw new InvalidOperationException($"OpenRazer catalog '{sourceName}' contains invalid capabilities.");
            }

            var transactions = new Dictionary<string, byte>(StringComparer.Ordinal);
            foreach (var (builder, value) in device.Transactions)
            {
                if (string.IsNullOrWhiteSpace(builder) ||
                    !byte.TryParse(value, System.Globalization.NumberStyles.HexNumber, null, out var transactionId))
                {
                    throw new InvalidOperationException($"OpenRazer catalog '{sourceName}' contains an invalid transaction.");
                }
                transactions.Add(builder, transactionId);
            }

            var builderArgumentPrefixes = new Dictionary<string, IReadOnlyList<ReadOnlyMemory<byte>>>(StringComparer.Ordinal);
            foreach (var (builder, values) in device.BuilderArgumentPrefixes)
            {
                if (string.IsNullOrWhiteSpace(builder) || values.Length == 0)
                {
                    throw new InvalidOperationException($"OpenRazer catalog '{sourceName}' contains invalid builder arguments.");
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
                            $"OpenRazer catalog '{sourceName}' contains invalid builder arguments.");
                    }
                }).ToArray();
                builderArgumentPrefixes.Add(builder, Array.AsReadOnly(prefixes));
            }

            CompleteClassicLedFacts(productId, capabilitySet, transactions, builderArgumentPrefixes);

            var commandSequences = new Dictionary<string, IReadOnlyList<OpenRazerCommandStep>>(StringComparer.Ordinal);
            foreach (var (builder, steps) in device.CommandSequences)
            {
                if (string.IsNullOrWhiteSpace(builder) || steps.Length < 2 ||
                    steps.Any(step =>
                        !byte.TryParse(step.TransactionId, System.Globalization.NumberStyles.HexNumber, null, out _) ||
                        !byte.TryParse(step.FixedArgument0, System.Globalization.NumberStyles.HexNumber, null, out _)))
                {
                    throw new InvalidOperationException($"OpenRazer catalog '{sourceName}' contains an invalid command sequence.");
                }
                commandSequences.Add(builder, Array.AsReadOnly(steps.Select(step => new OpenRazerCommandStep(
                    byte.Parse(step.TransactionId, System.Globalization.NumberStyles.HexNumber),
                    byte.Parse(step.FixedArgument0, System.Globalization.NumberStyles.HexNumber))).ToArray()));
            }

            OpenRazerMatrixDimensions? matrix = device.MatrixDimensions switch
            {
                null => null,
                [> 0 and <= byte.MaxValue, > 0 and <= byte.MaxValue] dimensions =>
                    new((byte)dimensions[0], (byte)dimensions[1]),
                _ => throw new InvalidOperationException($"OpenRazer catalog '{sourceName}' contains invalid matrix dimensions."),
            };
            var definition = new OpenRazerDeviceDefinition(
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
                commandSequences.ToFrozenDictionary(StringComparer.Ordinal));
            if (!output.TryAdd(productId, definition))
            {
                throw new InvalidOperationException($"Duplicate OpenRazer product ID 1532:{productId:X4}.");
            }
        }
    }

    /// <summary>
    /// Admits product ids OpenRazer has not catalogued so a read-only probe can reach the
    /// firmware and record what it answers. These entries deliberately declare only the
    /// read half of a known sibling device: no set_* builder is present, so every write
    /// path stays closed until this exact product id has on-device evidence.
    /// </summary>
    private static void AddLocallyAdmittedDevices(IDictionary<ushort, OpenRazerDeviceDefinition> devices)
    {
        // DeathAdder 2000 RZ01-0145 ships as 1532:004F, which OpenRazer catalogues. Two
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
            // seen must default to excluded, so a future OpenRazer entry cannot leak a write
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

            devices.Add(productId, new OpenRazerDeviceDefinition(
                productId,
                $"{sibling.DisplayName} (read-only probe)",
                sibling.Category,
                sibling.SourceCapabilities,
                sibling.MaximumDpi,
                sibling.PollingRates,
                sibling.MatrixDimensions,
                sibling.DeviceWait,
                readOnly,
                readOnlyPrefixes,
                sibling.DefaultStorage,
                sibling.DefaultLedZone,
                new Dictionary<string, IReadOnlyList<OpenRazerCommandStep>>()));
        }

        AddLeviathanV2X();

        // Upstream supports 00DE/00DF as Viper V3 Pro SE aliases. Windows reports their
        // feature report as 91 bytes including the report-id byte, same as the V3 Pro.
        AddViperV3ProSeAlias(0x00C0, 0x00DE, "Razer Viper V3 Pro SE (Wired)");
        AddViperV3ProSeAlias(0x00C1, 0x00DF, "Razer Viper V3 Pro SE (Wireless)");

        AddMiddlewareMice();

        // Product definitions come from the Razer middleware bundles: each product's
        // DeviceInfo carries the DPI range, polling rates and feature set. Upstream OpenRazer
        // has no entry for these product ids, so each one reuses the closest catalogued
        // sibling's protocol. A wrong guess only fails the runtime read probe, which keeps
        // the matching write path closed.
        void AddMiddlewareMice()
        {
            (ushort Sibling, ushort Product, string Name, int MaxDpi, int[] Rates)[] entries =
            [
                (0x006A, 0x0063, "Razer Jugan", 7200, []),
                (0x0073, 0x0069, "Razer Mamba Hyperflux", 16000, [125, 500, 1000]),
                (0x006B, 0x0074, "Razer Abyssus Lite", 6400, [125, 500, 1000]),
                (0x005A, 0x0075, "Razer Turret Mouse Xbox One Edition", 16000, [125, 500, 1000]),
                (0x0053, 0x0093, "Razer Naga Classic Edition", 16000, [125, 500, 1000]),
                (0x00BF, 0x00DD, "Razer Krakoff", 45000, [125, 500, 1000, 2000, 4000, 8000]),
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
            ];

            foreach (var entry in entries)
            {
                if (!devices.TryGetValue(entry.Sibling, out var sibling) ||
                    devices.ContainsKey(entry.Product))
                {
                    continue;
                }

                devices.Add(entry.Product, new OpenRazerDeviceDefinition(
                    entry.Product,
                    entry.Name,
                    sibling.Category,
                    sibling.SourceCapabilities,
                    entry.MaxDpi,
                    Array.AsReadOnly(entry.Rates),
                    sibling.MatrixDimensions,
                    sibling.DeviceWait,
                    sibling.Transactions,
                    sibling.BuilderArgumentPrefixes,
                    sibling.DefaultStorage,
                    sibling.DefaultLedZone,
                    sibling.CommandSequences));
                MappingProductIds.Add(entry.Product);
            }
        }

        void AddLeviathanV2X()
        {
            if (devices.ContainsKey(LeviathanV2XProtocol.ProductId))
            {
                return;
            }

            // The 1354 middleware uses the same 91-byte feature-report envelope as the
            // OpenRazer catalog. Only the identity and brightness half is admitted here;
            // EQ, bass and input-source controls live in the dedicated audio overlay.
            var capabilities = new HashSet<string>(StringComparer.Ordinal)
            {
                "get_firmware",
                "get_serial",
                "get_brightness",
                "set_brightness",
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
            };
            var brightnessPrefix = Array.AsReadOnly(
                new[] { new ReadOnlyMemory<byte>(new byte[] { 0x00, 0x00 }) });
            var prefixes = new Dictionary<string, IReadOnlyList<ReadOnlyMemory<byte>>>(StringComparer.Ordinal)
            {
                ["razer_chroma_extended_matrix_get_brightness"] = brightnessPrefix,
                ["razer_chroma_extended_matrix_brightness"] = brightnessPrefix,
            };
            devices.Add(LeviathanV2XProtocol.ProductId, new OpenRazerDeviceDefinition(
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
                OpenRazerStorage.Temporary,
                OpenRazerLedZone.All,
                new Dictionary<string, IReadOnlyList<OpenRazerCommandStep>>(StringComparer.Ordinal)));
        }

        void AddViperV3ProSeAlias(ushort siblingId, ushort productId, string displayName)
        {
            if (!devices.TryGetValue(siblingId, out var sibling) ||
                devices.ContainsKey(productId))
            {
                return;
            }

            devices.Add(productId, new OpenRazerDeviceDefinition(
                productId,
                displayName,
                sibling.Category,
                sibling.SourceCapabilities,
                sibling.MaximumDpi,
                sibling.PollingRates,
                sibling.MatrixDimensions,
                sibling.DeviceWait,
                sibling.Transactions,
                sibling.BuilderArgumentPrefixes,
                sibling.DefaultStorage,
                sibling.DefaultLedZone,
                sibling.CommandSequences));
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

        // OpenRazer's Anansi static and macro branches issue these as the second/third
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
        _ => throw new InvalidOperationException($"Unsupported OpenRazer category '{value}'."),
    };

    private static OpenRazerStorage ParseStorage(string? value) => value switch
    {
        null => OpenRazerStorage.Persistent,
        "00" => OpenRazerStorage.Temporary,
        "01" => OpenRazerStorage.Persistent,
        _ => throw new InvalidOperationException($"Unsupported OpenRazer storage '{value}'."),
    };

    private static OpenRazerLedZone ParseLedZone(string? value) => value switch
    {
        null => OpenRazerLedZone.All,
        "00" => OpenRazerLedZone.All,
        "01" => OpenRazerLedZone.ScrollWheel,
        "04" => OpenRazerLedZone.Logo,
        "05" => OpenRazerLedZone.Backlight,
        "07" => OpenRazerLedZone.Macro,
        "08" => OpenRazerLedZone.Game,
        "10" => OpenRazerLedZone.RightSide,
        "11" => OpenRazerLedZone.LeftSide,
        "20" => OpenRazerLedZone.Charging,
        "21" => OpenRazerLedZone.FastCharging,
        "22" => OpenRazerLedZone.FullyCharged,
        _ => throw new InvalidOperationException($"Unsupported OpenRazer LED zone '{value}'."),
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
        string[] AmbiguousTransactions);

    private sealed record CatalogCommandStep(string TransactionId, string FixedArgument0);
}
