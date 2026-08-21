using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.Windows.Devices;

internal sealed record ManifestDocument(string SourceName, string Json, bool IsBuiltIn);

internal sealed record RazerDeviceRegistryLoadResult(
    RazerDeviceRegistry Registry,
    IReadOnlyList<string> Errors);

internal sealed class RazerDeviceRegistry
{
    private const int SchemaVersion = 1;
    private const int MaximumExternalManifestFiles = 64;
    private const long MaximumExternalManifestBytes = 64 * 1024;
    private static readonly Lazy<RazerDeviceRegistry> BuiltInValue = new(LoadBuiltIn);
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };
    private static readonly IReadOnlyDictionary<string, IReadOnlyDictionary<string, CapabilityContract>> FamilyCapabilities =
        new Dictionary<string, IReadOnlyDictionary<string, CapabilityContract>>(StringComparer.Ordinal)
        {
            ["blade-710"] = new Dictionary<string, CapabilityContract>(StringComparer.Ordinal)
            {
                ["keyboard-brightness.get"] = new(0x02, 0x0E, 0x84, "0100"),
                ["keyboard-brightness.set"] = new(0x02, 0x0E, 0x04, ""),
                ["thermal-state.get"] = new(0x04, 0x0D, 0x82, ""),
                ["thermal-state.set"] = new(0x04, 0x0D, 0x02, ""),
                ["fan-target.get"] = new(0x03, 0x0D, 0x81, ""),
                ["current-fan-rpm.get"] = new(0x03, 0x0D, 0x88, ""),
                ["advanced-fan-mode.get"] = new(0x03, 0x0D, 0x87, ""),
                ["boost.get"] = new(0x03, 0x0D, 0x87, ""),
                ["boost.set"] = new(0x03, 0x0D, 0x07, ""),
                ["charge-limit.get"] = new(0x01, 0x07, 0x92, "00", true),
                ["charge-limit.set"] = new(0x01, 0x07, 0x12, ""),
                ["max-fan.get"] = new(0x01, 0x07, 0x8F, "00", true),
                ["max-fan.set"] = new(0x01, 0x07, 0x0F, ""),
                ["gaming-mode.set"] = new(0x04, 0x00, 0x08, "", AllowZeroTransactionId: true),
                ["fn-key.set"] = new(0x02, 0x02, 0x06, "", AllowZeroTransactionId: true),
                ["startup-animation.get"] = new(0x01, 0x0F, 0x98, "00"),
                ["startup-animation.set"] = new(0x02, 0x0F, 0x18, ""),
                ["native-display-mode.get"] = new(0x01, 0x0D, 0x8E, "00", true),
                ["native-display-mode.set"] = new(0x01, 0x0D, 0x0E, ""),
                ["sku-hardware-configuration.get"] = new(0x01, 0x0D, 0x8F, "00", true),
                ["logo-power.get"] = new(0x03, 0x03, 0x80, "010400"),
                ["logo-power.set"] = new(0x03, 0x03, 0x00, ""),
                ["logo-mode.get"] = new(0x03, 0x03, 0x82, "010400"),
                ["logo-mode.set"] = new(0x03, 0x03, 0x02, ""),
                ["logo-effect.set"] = new(0x03, 0x03, 0x02, "", AllowZeroTransactionId: true),
                ["logo-state.set"] = new(0x03, 0x03, 0x00, "", AllowZeroTransactionId: true),
            },
            ["viper-184"] = new Dictionary<string, CapabilityContract>(StringComparer.Ordinal)
            {
                ["battery.get"] = new(0x02, 0x07, 0x80, ""),
                ["polling-rate.get"] = new(0x01, 0x00, 0x85, ""),
                ["polling-rate.set"] = new(0x01, 0x00, 0x05, ""),
                ["current-dpi.get"] = new(0x07, 0x04, 0x85, "00"),
                ["current-dpi.set"] = new(0x07, 0x04, 0x05, ""),
                ["idle-timeout.get"] = new(0x02, 0x07, 0x83, ""),
                ["idle-timeout.set"] = new(0x02, 0x07, 0x03, ""),
                ["dpi-stages.get"] = new(0x26, 0x04, 0x86, "01"),
                ["dpi-stages.set"] = new(0x26, 0x04, 0x06, ""),
                ["low-battery-threshold.get"] = new(0x01, 0x07, 0x81, ""),
                ["obm-maximum-profiles.get"] = new(0x01, 0x05, 0x8A, ""),
                ["obm-profile-count.get"] = new(0x01, 0x05, 0x80, ""),
                ["obm-profile-ids.get"] = new(0x50, 0x05, 0x81, ""),
                ["obm-button-ids.get"] = new(0x50, 0x02, 0x84, ""),
                ["obm-assignment.get"] = new(0x50, 0x02, 0x8C, ""),
                ["obm-assignment.set"] = new(0x50, 0x02, 0x0C, ""),
            },
        };

    private readonly IReadOnlyDictionary<(ushort VendorId, ushort ProductId), RazerDeviceManifest> _devices;

    private RazerDeviceRegistry(
        IReadOnlyList<RazerDeviceManifest> manifests,
        IReadOnlyDictionary<(ushort VendorId, ushort ProductId), RazerDeviceManifest> devices)
    {
        Manifests = manifests;
        _devices = devices;
    }

    internal static RazerDeviceRegistry BuiltIn => BuiltInValue.Value;
    internal IReadOnlyList<RazerDeviceManifest> Manifests { get; }

    internal RazerDeviceManifest? Find(ushort vendorId, ushort productId) =>
        _devices.GetValueOrDefault((vendorId, productId));

    internal static RazerDeviceRegistry LoadJson(IEnumerable<string> documents)
    {
        ArgumentNullException.ThrowIfNull(documents);
        var manifests = documents
            .Select((document, index) => Parse(new ManifestDocument($"memory-{index + 1}.json", document, false)))
            .ToArray();
        return CreateRegistry(manifests);
    }

    internal static RazerDeviceRegistryLoadResult Load(string? externalDirectory = null)
    {
        externalDirectory ??= Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "OpenSynapse",
            "devices");

        var errors = new List<string>();
        var externalDocuments = new List<ManifestDocument>();
        if (Directory.Exists(externalDirectory))
        {
            string[] files;
            try
            {
                files = Directory.EnumerateFiles(
                        externalDirectory,
                        "*.json",
                        SearchOption.TopDirectoryOnly)
                    .Order(StringComparer.OrdinalIgnoreCase)
                    .ThenBy(file => file, StringComparer.Ordinal)
                    .ToArray();
            }
            catch (UnauthorizedAccessException)
            {
                errors.Add("外部 manifest 目录：无权枚举文件。");
                files = [];
            }
            catch (IOException)
            {
                errors.Add("外部 manifest 目录：枚举文件失败。");
                files = [];
            }

            if (files.Length > MaximumExternalManifestFiles)
            {
                errors.Add($"外部 manifest 超过 {MaximumExternalManifestFiles} 个，已拒绝全部外部配置。");
            }
            else
            {
                foreach (var file in files)
                {
                    var name = Path.GetFileName(file);
                    try
                    {
                        externalDocuments.Add(new ManifestDocument(
                            name,
                            ReadExternalManifest(file),
                            false));
                    }
                    catch (UnauthorizedAccessException)
                    {
                        errors.Add($"{name}：无权读取文件。");
                    }
                    catch (IOException)
                    {
                        errors.Add($"{name}：读取文件失败。");
                    }
                    catch (InvalidOperationException exception)
                    {
                        errors.Add($"{name}：{exception.Message}");
                    }
                }
            }
        }

        return Merge(LoadBuiltInDocuments(), externalDocuments, errors);
    }

    private static string ReadExternalManifest(string path)
    {
        using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read);
        if (stream.Length > MaximumExternalManifestBytes)
        {
            throw new InvalidOperationException("文件超过 65536 字节。");
        }

        using var reader = new StreamReader(stream, detectEncodingFromByteOrderMarks: true);
        return reader.ReadToEnd();
    }

    private static RazerDeviceRegistry CreateRegistry(IReadOnlyList<RazerDeviceManifest> manifests)
    {
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var devices = new Dictionary<(ushort VendorId, ushort ProductId), RazerDeviceManifest>();

        foreach (var manifest in manifests)
        {
            if (!ids.Add(manifest.Id))
            {
                throw new InvalidOperationException($"重复的设备 manifest ID：'{manifest.Id}'。");
            }

            foreach (var productId in manifest.ProductIds)
            {
                if (!devices.TryAdd((manifest.VendorId, productId), manifest))
                {
                    throw new InvalidOperationException(
                        $"重复的 VID/PID：{manifest.VendorId:X4}:{productId:X4}。");
                }
            }
        }

        return new RazerDeviceRegistry(manifests, devices);
    }

    private static RazerDeviceRegistry LoadBuiltIn()
    {
        var result = Merge(LoadBuiltInDocuments(), [], []);
        return result.Registry;
    }

    private static IReadOnlyList<ManifestDocument> LoadBuiltInDocuments()
    {
        var assembly = typeof(RazerDeviceRegistry).Assembly;
        var resourceNames = assembly.GetManifestResourceNames()
            .Where(name => name.Contains(".Devices.Manifests.", StringComparison.Ordinal) &&
                           name.EndsWith(".json", StringComparison.Ordinal))
            .Order(StringComparer.Ordinal)
            .ToArray();
        if (resourceNames.Length == 0)
        {
            throw new InvalidOperationException("未找到内置 Razer 设备 manifest。");
        }

        return resourceNames.Select(name =>
        {
            using var stream = assembly.GetManifestResourceStream(name)
                ?? throw new InvalidOperationException($"无法读取内置资源 '{name}'。");
            using var reader = new StreamReader(stream);
            return new ManifestDocument(name, reader.ReadToEnd(), true);
        }).ToArray();
    }

    private static RazerDeviceRegistryLoadResult Merge(
        IReadOnlyList<ManifestDocument> builtInDocuments,
        IReadOnlyList<ManifestDocument> externalDocuments,
        List<string> errors)
    {
        var manifests = builtInDocuments.Select(Parse).ToList();
        var registry = CreateRegistry(manifests);
        var ids = registry.Manifests.Select(manifest => manifest.Id).ToHashSet(StringComparer.Ordinal);
        var devices = registry._devices.ToDictionary();

        foreach (var document in externalDocuments
                     .OrderBy(item => item.SourceName, StringComparer.OrdinalIgnoreCase)
                     .ThenBy(item => item.SourceName, StringComparer.Ordinal))
        {
            try
            {
                var manifest = Parse(document);
                var familyTemplate = manifests.First(item =>
                    item.ProtocolFamily == manifest.ProtocolFamily &&
                    builtInDocuments.Any(builtIn => builtIn.SourceName == item.SourceName));
                EnsureExternalContract(manifest, familyTemplate);
                if (ids.Contains(manifest.Id))
                {
                    throw new InvalidOperationException($"重复的设备 manifest ID：'{manifest.Id}'。");
                }

                ushort? conflict = null;
                foreach (var productId in manifest.ProductIds)
                {
                    if (devices.ContainsKey((manifest.VendorId, productId)))
                    {
                        conflict = productId;
                        break;
                    }
                }
                if (conflict is { } conflictingProductId)
                {
                    throw new InvalidOperationException(
                        $"重复的 VID/PID：{manifest.VendorId:X4}:{conflictingProductId:X4}。");
                }

                ids.Add(manifest.Id);
                manifests.Add(manifest);
                foreach (var productId in manifest.ProductIds)
                {
                    devices.Add((manifest.VendorId, productId), manifest);
                }
            }
            catch (InvalidOperationException exception)
            {
                errors.Add($"{document.SourceName}：{exception.Message}");
            }
        }

        return new RazerDeviceRegistryLoadResult(
            new RazerDeviceRegistry(manifests, devices),
            errors.ToArray());
    }

    private static void EnsureExternalContract(
        RazerDeviceManifest external,
        RazerDeviceManifest builtIn)
    {
        foreach (var (capabilityId, descriptor) in external.Capabilities)
        {
            var contract = builtIn.GetRequiredCapability(capabilityId);
            if (descriptor.TransactionId != contract.TransactionId)
            {
                throw new InvalidOperationException(
                    $"capability '{capabilityId}' 的 transaction ID 不符合内置协议族契约。");
            }
            if (descriptor.Wait < contract.Wait)
            {
                throw new InvalidOperationException(
                    $"capability '{capabilityId}' 的等待时间短于内置协议族契约。");
            }
        }
    }

    private static RazerDeviceManifest Parse(ManifestDocument document)
    {
        if (string.IsNullOrWhiteSpace(document.Json))
        {
            throw new InvalidOperationException("设备 manifest 不能为空。");
        }

        try
        {
            using var parsed = JsonDocument.Parse(document.Json);
            EnsureNoDuplicateProperties(parsed.RootElement);
            var source = JsonSerializer.Deserialize<ManifestJson>(document.Json, JsonOptions)
                ?? throw new InvalidOperationException("设备 manifest 反序列化结果为空。");
            return Validate(source, document.SourceName);
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException($"设备 manifest JSON 无效：{exception.Message}", exception);
        }
    }

    private static RazerDeviceManifest Validate(ManifestJson source, string sourceName)
    {
        if (source is null || source.ProductIds is null || source.Collection is null ||
            source.Transport is null || source.Capabilities is null)
        {
            throw new InvalidOperationException("manifest 的必需对象或集合不能为 null。");
        }
        if (source.SchemaVersion != SchemaVersion)
        {
            throw new InvalidOperationException($"不支持 manifest schemaVersion {source.SchemaVersion}。");
        }
        if (string.IsNullOrWhiteSpace(source.Id) || string.IsNullOrWhiteSpace(source.DisplayName))
        {
            throw new InvalidOperationException("manifest id 和 displayName 不能为空。");
        }
        if (string.IsNullOrWhiteSpace(source.ProtocolFamily) ||
            !FamilyCapabilities.TryGetValue(source.ProtocolFamily, out var admittedCapabilities))
        {
            throw new InvalidOperationException($"未知协议族：'{source.ProtocolFamily}'。");
        }
        if (source.ProductIds.Count == 0)
        {
            throw new InvalidOperationException($"manifest '{source.Id}' 至少需要一个 PID。");
        }
        if (source.Collection.FeatureReportLength != RazerFeatureReport.Length)
        {
            throw new InvalidOperationException(
                $"manifest '{source.Id}' 只允许 {RazerFeatureReport.Length} 字节 feature report。");
        }
        if (source.Transport.WaitMilliseconds is < 1 or > 1000)
        {
            throw new InvalidOperationException($"manifest '{source.Id}' 的等待时间无效。");
        }

        var vendorId = ParseWord(source.VendorId, "vendorId");
        var productIds = source.ProductIds.Select(value => ParseWord(value, "productIds")).ToArray();
        if (productIds.Distinct().Count() != productIds.Length)
        {
            throw new InvalidOperationException($"manifest '{source.Id}' 包含重复 PID。");
        }

        var capabilities = new Dictionary<string, RazerRequestDescriptor>(StringComparer.Ordinal);
        foreach (var (capabilityId, request) in source.Capabilities)
        {
            if (!admittedCapabilities.TryGetValue(capabilityId, out var contract))
            {
                throw new InvalidOperationException(
                    $"协议族 '{source.ProtocolFamily}' 不允许 capability '{capabilityId}'。");
            }

            if (request is null)
            {
                throw new InvalidOperationException(
                    $"manifest '{source.Id}' 的 capability '{capabilityId}' 不能为 null。");
            }

            var transactionId = ParseByte(request.TransactionId, $"{capabilityId}.transactionId");
            var dataSize = ParseByte(request.DataSize, $"{capabilityId}.dataSize");
            var commandClass = ParseByte(request.CommandClass, $"{capabilityId}.commandClass");
            var commandId = ParseByte(request.CommandId, $"{capabilityId}.commandId");
            var arguments = ParseBytes(request.Arguments, $"{capabilityId}.arguments");
            var waitMilliseconds = request.WaitMilliseconds ?? source.Transport.WaitMilliseconds;
            if ((transactionId == 0 && !contract.AllowZeroTransactionId) ||
                dataSize > 80 || arguments.Length > dataSize ||
                waitMilliseconds is < 1 or > 1000)
            {
                throw new InvalidOperationException(
                    $"manifest '{source.Id}' 的 capability '{capabilityId}' 参数无效。");
            }
            if (request.AllowRemainingPacketsMismatch &&
                (source.ProtocolFamily != "blade-710" ||
                 capabilityId is not ("charge-limit.get" or "max-fan.get" or
                     "native-display-mode.get" or "sku-hardware-configuration.get")))
            {
                throw new InvalidOperationException(
                    $"capability '{capabilityId}' 不允许 remaining-packets mismatch 例外。");
            }
            if (dataSize != contract.DataSize || commandClass != contract.CommandClass ||
                commandId != contract.CommandId ||
                !arguments.AsSpan().SequenceEqual(Convert.FromHexString(contract.Arguments)) ||
                request.AllowRemainingPacketsMismatch != contract.AllowRemainingPacketsMismatch)
            {
                throw new InvalidOperationException(
                    $"capability '{capabilityId}' 的报文语义不符合协议族 '{source.ProtocolFamily}' 契约。");
            }

            capabilities.Add(capabilityId, new RazerRequestDescriptor(
                transactionId,
                dataSize,
                commandClass,
                commandId,
                arguments,
                waitMilliseconds,
                request.AllowRemainingPacketsMismatch));
        }

        var missing = admittedCapabilities.Keys.Except(capabilities.Keys, StringComparer.Ordinal).ToArray();
        if (missing.Length > 0)
        {
            throw new InvalidOperationException(
                $"manifest '{source.Id}' 缺少协议族必需 capability：{string.Join(", ", missing)}。");
        }

        return new RazerDeviceManifest(
            sourceName,
            source.Id,
            source.DisplayName,
            vendorId,
            productIds,
            new RazerHidCollection(
                ParseWord(source.Collection.UsagePage, "collection.usagePage"),
                ParseWord(source.Collection.Usage, "collection.usage"),
                checked((ushort)source.Collection.FeatureReportLength)),
            source.ProtocolFamily,
            capabilities);
    }

    private static byte ParseByte(string value, string field)
    {
        var bytes = ParseHex(value, field, expectedCharacters: 2, allowEmpty: false);
        return bytes[0];
    }

    private static ushort ParseWord(string value, string field)
    {
        var bytes = ParseHex(value, field, expectedCharacters: 4, allowEmpty: false);
        return (ushort)((bytes[0] << 8) | bytes[1]);
    }

    private static byte[] ParseBytes(string value, string field) =>
        ParseHex(value, field, expectedCharacters: null, allowEmpty: true);

    private static byte[] ParseHex(string? value, string field, int? expectedCharacters, bool allowEmpty)
    {
        if (value is null || (!allowEmpty && value.Length == 0) || value.Length % 2 != 0 ||
            (expectedCharacters is not null && value.Length != expectedCharacters) ||
            value.Any(character => !char.IsAsciiHexDigit(character) ||
                                   (character is >= 'a' and <= 'f')))
        {
            throw new InvalidOperationException($"manifest 字段 '{field}' 不是有效的大写十六进制字符串。");
        }

        try
        {
            return Convert.FromHexString(value);
        }
        catch (FormatException exception)
        {
            throw new InvalidOperationException($"manifest 字段 '{field}' 不是有效的十六进制字符串。", exception);
        }
    }

    private static void EnsureNoDuplicateProperties(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in element.EnumerateObject())
            {
                if (!names.Add(property.Name))
                {
                    throw new InvalidOperationException($"manifest 包含重复属性 '{property.Name}'。");
                }
                EnsureNoDuplicateProperties(property.Value);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                EnsureNoDuplicateProperties(item);
            }
        }
    }

    private sealed class ManifestJson
    {
        public required int SchemaVersion { get; init; }
        public required string Id { get; init; }
        public required string DisplayName { get; init; }
        public required string VendorId { get; init; }
        public required List<string> ProductIds { get; init; }
        public required CollectionJson Collection { get; init; }
        public required string ProtocolFamily { get; init; }
        public required TransportJson Transport { get; init; }
        public required Dictionary<string, RequestJson> Capabilities { get; init; }
    }

    private sealed class CollectionJson
    {
        public required string UsagePage { get; init; }
        public required string Usage { get; init; }
        public required int FeatureReportLength { get; init; }
    }

    private sealed class TransportJson
    {
        public required int WaitMilliseconds { get; init; }
    }

    private sealed class RequestJson
    {
        public required string TransactionId { get; init; }
        public required string DataSize { get; init; }
        public required string CommandClass { get; init; }
        public required string CommandId { get; init; }
        public required string Arguments { get; init; }
        public int? WaitMilliseconds { get; init; }
        public bool AllowRemainingPacketsMismatch { get; init; }
    }

    private sealed record CapabilityContract(
        byte DataSize,
        byte CommandClass,
        byte CommandId,
        string Arguments,
        bool AllowRemainingPacketsMismatch = false,
        bool AllowZeroTransactionId = false);
}
