using System.Text.Json;
using OpenSynapse.Core.Devices;
using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.Windows.Devices;

internal sealed record RazerHidCollection(
    ushort UsagePage,
    ushort Usage,
    ushort FeatureReportLength);

internal sealed record RazerFirmwareRelease(string Version, string Url);

internal sealed record RazerFirmwareInfo(
    RazerFirmwareRelease? Latest,
    RazerFirmwareRelease? Driver);

internal sealed record RazerDeviceMetadata(
    string? Name,
    string? ShortName,
    IReadOnlyList<int> EditionIds,
    IReadOnlyList<int> LayoutIds,
    string? RzDeviceType,
    RazerFirmwareInfo? Firmware)
{
    internal static RazerDeviceMetadata? Parse(
        JsonElement? source,
        string manifestId,
        JsonSerializerOptions options)
    {
        if (source is null || source.Value.ValueKind == JsonValueKind.Null)
        {
            return null;
        }
        if (source.Value.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidOperationException(
                $"Manifest '{manifestId}' device must be an object.");
        }

        var json = source.Value.Deserialize<RazerDeviceJson>(options)
            ?? throw new InvalidOperationException(
                $"Manifest '{manifestId}' device deserialized to null.");
        return new RazerDeviceMetadata(
            OptionalText(json.Name, "device.name", manifestId),
            OptionalText(json.ShortName, "device.shortName", manifestId),
            json.EditionIds ?? [],
            json.LayoutIds ?? [],
            OptionalText(json.RzDeviceType, "device.rzDeviceType", manifestId),
            json.Firmware is null
                ? null
                : new RazerFirmwareInfo(
                    Release(json.Firmware.Latest, "latest", manifestId),
                    Release(json.Firmware.Driver, "driver", manifestId)));
    }

    private static string? OptionalText(string? value, string field, string manifestId)
    {
        if (value is null)
        {
            return null;
        }
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException(
                $"Manifest '{manifestId}' {field} cannot be empty.");
        }
        return value.Trim();
    }

    private static RazerFirmwareRelease? Release(
        RazerFirmwareReleaseJson? source,
        string kind,
        string manifestId)
    {
        if (source is null)
        {
            return null;
        }

        var version = OptionalText(source.Version, $"device.firmware.{kind}.version", manifestId)
            ?? throw new InvalidOperationException(
                $"Manifest '{manifestId}' device.firmware.{kind}.version is required.");
        var url = OptionalText(source.Url, $"device.firmware.{kind}.url", manifestId)
            ?? throw new InvalidOperationException(
                $"Manifest '{manifestId}' device.firmware.{kind}.url is required.");
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
        {
            throw new InvalidOperationException(
                $"Manifest '{manifestId}' device.firmware.{kind}.url must be an absolute HTTP(S) URL.");
        }
        return new RazerFirmwareRelease(version, url);
    }
}

internal sealed class RazerDeviceJson
{
    public string? Name { get; init; }
    public string? ShortName { get; init; }
    public List<int>? EditionIds { get; init; }
    public List<int>? LayoutIds { get; init; }
    public string? RzDeviceType { get; init; }
    public RazerFirmwareJson? Firmware { get; init; }
}

internal sealed class RazerFirmwareJson
{
    public RazerFirmwareReleaseJson? Latest { get; init; }
    public RazerFirmwareReleaseJson? Driver { get; init; }
}

internal sealed class RazerFirmwareReleaseJson
{
    public string? Version { get; init; }
    public string? Url { get; init; }
}

internal sealed class RazerRequestDescriptor
{
    private readonly byte[] _arguments;

    internal RazerRequestDescriptor(
        byte transactionId,
        byte maximumDataSize,
        byte commandClass,
        byte commandId,
        byte[] arguments,
        int waitMilliseconds,
        bool allowRemainingPacketsMismatch)
    {
        TransactionId = transactionId;
        MaximumDataSize = maximumDataSize;
        CommandClass = commandClass;
        CommandId = commandId;
        _arguments = arguments;
        Wait = TimeSpan.FromMilliseconds(waitMilliseconds);
        AllowRemainingPacketsMismatch = allowRemainingPacketsMismatch;
    }

    internal byte TransactionId { get; }
    internal byte MaximumDataSize { get; }
    internal byte CommandClass { get; }
    internal byte CommandId { get; }
    internal ReadOnlyMemory<byte> Arguments => _arguments;
    internal TimeSpan Wait { get; }
    internal bool AllowRemainingPacketsMismatch { get; }

    internal byte[] CreateRequest() =>
        RazerFeatureReport.CreateRequest(
            TransactionId,
            MaximumDataSize,
            CommandClass,
            CommandId,
            _arguments);

    internal byte[] CreateRequest(ReadOnlySpan<byte> arguments, byte? dataSize = null)
    {
        var actualDataSize = dataSize ?? MaximumDataSize;
        if (actualDataSize > MaximumDataSize || arguments.Length > actualDataSize)
        {
            throw new ArgumentOutOfRangeException(nameof(dataSize), "Request arguments exceed the maximum data size declared by the manifest.");
        }

        return RazerFeatureReport.CreateRequest(
            TransactionId,
            actualDataSize,
            CommandClass,
            CommandId,
            arguments);
    }
}

internal sealed class RazerDeviceManifest
{
    internal RazerDeviceManifest(
        string sourceName,
        string id,
        string displayName,
        ushort vendorId,
        IReadOnlyList<ushort> productIds,
        RazerHidCollection collection,
        string protocolFamily,
        IReadOnlyDictionary<string, RazerRequestDescriptor> capabilities,
        DeviceCategory category,
        int matrixRows = 0,
        int matrixColumns = 0,
        RazerDeviceMetadata? device = null,
        BladeLightingDialect lightingDialect = BladeLightingDialect.Unknown)
    {
        SourceName = sourceName;
        Id = id;
        DisplayName = displayName;
        VendorId = vendorId;
        ProductIds = productIds;
        Collection = collection;
        ProtocolFamily = protocolFamily;
        Capabilities = capabilities;
        Category = category;
        MatrixRows = matrixRows;
        MatrixColumns = matrixColumns;
        Device = device;
        LightingDialect = lightingDialect;
    }

    internal string SourceName { get; }
    internal string Id { get; }
    internal string DisplayName { get; }
    internal ushort VendorId { get; }
    internal IReadOnlyList<ushort> ProductIds { get; }
    internal RazerHidCollection Collection { get; }
    internal string ProtocolFamily { get; }
    internal IReadOnlyDictionary<string, RazerRequestDescriptor> Capabilities { get; }
    internal DeviceCategory Category { get; }
    internal int MatrixRows { get; }
    internal int MatrixColumns { get; }
    internal RazerDeviceMetadata? Device { get; }
    internal BladeLightingDialect LightingDialect { get; }

    internal RazerRequestDescriptor GetRequiredCapability(string capabilityId) =>
        Capabilities.TryGetValue(capabilityId, out var descriptor)
            ? descriptor
            : throw new InvalidOperationException(
                $"Device manifest '{Id}' is missing required capability '{capabilityId}'.");
}
