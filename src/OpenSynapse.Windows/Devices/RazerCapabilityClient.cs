using System.ComponentModel;
using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.Windows.Devices;

/// <summary>
/// Turns a resolved device manifest into feature-report requests. Readers never build a
/// request from raw command numbers or re-check manifest limits themselves.
/// </summary>
internal sealed class RazerCapabilityClient(IRazerFeatureTransport transport)
{
    private readonly IRazerFeatureTransport _transport =
        transport ?? throw new ArgumentNullException(nameof(transport));

    public Task<byte[]> QueryAsync(
        ResolvedRazerDevice device,
        string capabilityId,
        CancellationToken cancellationToken) =>
        QueryAsync(
            device,
            capabilityId,
            device.GetCapability(capabilityId).Arguments,
            cancellationToken);

    public Task<byte[]> QueryAsync(
        ResolvedRazerDevice device,
        string capabilityId,
        ReadOnlyMemory<byte> arguments,
        CancellationToken cancellationToken,
        byte? dataSize = null)
    {
        var request = device.GetCapability(capabilityId);
        var actualDataSize = dataSize ?? request.MaximumDataSize;
        if (actualDataSize > request.MaximumDataSize || arguments.Length > actualDataSize)
        {
            throw new InvalidOperationException(
                $"Dynamic arguments for capability '{capabilityId}' exceed the manifest limit.");
        }

        return _transport.QueryAsync(
            device.Descriptor.Id,
            request.TransactionId,
            actualDataSize,
            request.CommandClass,
            request.CommandId,
            arguments,
            request.Wait,
            cancellationToken,
            request.AllowRemainingPacketsMismatch);
    }

    public Task<byte[]> QueryBuiltRequestAsync(
        ResolvedRazerDevice device,
        string capabilityId,
        byte[] builtRequest,
        CancellationToken cancellationToken)
    {
        var request = CreateConfiguredRequest(device, capabilityId, builtRequest);
        return QueryAsync(
            device,
            capabilityId,
            request.AsMemory(RazerFeatureReport.ArgumentsOffset, request[6]),
            cancellationToken,
            request[6]);
    }

    public Task<byte[]> QueryCommandAsync(
        ResolvedRazerDevice device,
        string capabilityId,
        byte commandId,
        ReadOnlyMemory<byte> arguments,
        CancellationToken cancellationToken,
        byte? dataSize = null)
    {
        var request = device.GetCapability(capabilityId);
        var actualDataSize = dataSize ?? request.MaximumDataSize;
        if (actualDataSize > request.MaximumDataSize || arguments.Length > actualDataSize)
        {
            throw new InvalidOperationException(
                $"Dynamic arguments for capability '{capabilityId}' exceed the manifest limit.");
        }

        return _transport.QueryAsync(
            device.Descriptor.Id,
            request.TransactionId,
            actualDataSize,
            request.CommandClass,
            commandId,
            arguments,
            request.Wait,
            cancellationToken,
            request.AllowRemainingPacketsMismatch);
    }

    public static byte[] CreateRequest(ResolvedRazerDevice device, string capabilityId) =>
        device.GetCapability(capabilityId).CreateRequest();

    public static byte[] CreateConfiguredRequest(
        ResolvedRazerDevice device,
        string capabilityId,
        byte[] builtRequest)
    {
        if (builtRequest.Length != RazerFeatureReport.Length)
        {
            throw new InvalidOperationException(
                "The strongly typed builder returned an invalid feature-report length.");
        }

        return device.GetCapability(capabilityId).CreateRequest(
            builtRequest.AsSpan(RazerFeatureReport.ArgumentsOffset, builtRequest[6]),
            builtRequest[6]);
    }

    public static void EnsureDataSize(byte[] response, byte minimum, string query)
    {
        if (response[6] < minimum)
        {
            throw new InvalidOperationException(
                $"{query} response is too short: {response[6]} < {minimum}.");
        }
    }

    public static void EnsureValidated(string? validatedPath, string currentPath, string message)
    {
        if (!StringComparer.OrdinalIgnoreCase.Equals(validatedPath, currentPath))
        {
            throw new InvalidOperationException(message);
        }
    }

    public static bool IsExpectedHardwareException(Exception exception) =>
        exception is Win32Exception or IOException or UnauthorizedAccessException or
        InvalidOperationException or NotSupportedException or ArgumentException;
}
