using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.Windows.Devices;

internal sealed record RazerHidCollection(
    ushort UsagePage,
    ushort Usage,
    ushort FeatureReportLength);

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
            throw new ArgumentOutOfRangeException(nameof(dataSize), "请求参数超过 manifest 声明的最大 dataSize。");
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
        IReadOnlyDictionary<string, RazerRequestDescriptor> capabilities)
    {
        SourceName = sourceName;
        Id = id;
        DisplayName = displayName;
        VendorId = vendorId;
        ProductIds = productIds;
        Collection = collection;
        ProtocolFamily = protocolFamily;
        Capabilities = capabilities;
    }

    internal string SourceName { get; }
    internal string Id { get; }
    internal string DisplayName { get; }
    internal ushort VendorId { get; }
    internal IReadOnlyList<ushort> ProductIds { get; }
    internal RazerHidCollection Collection { get; }
    internal string ProtocolFamily { get; }
    internal IReadOnlyDictionary<string, RazerRequestDescriptor> Capabilities { get; }

    internal RazerRequestDescriptor GetRequiredCapability(string capabilityId) =>
        Capabilities.TryGetValue(capabilityId, out var descriptor)
            ? descriptor
            : throw new InvalidOperationException(
                $"设备 manifest '{Id}' 缺少必需 capability '{capabilityId}'。");
}
