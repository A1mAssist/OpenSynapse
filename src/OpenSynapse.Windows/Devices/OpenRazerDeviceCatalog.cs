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
    internal const string SourceCommit = "6820f9da169d354bc7e6e93a0aa8683a6bb75792";
    private const int ExpectedDeviceCount = 254;
    private static readonly ushort[] ReservedProductIds = [0x00B8, 0x02C6];
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
