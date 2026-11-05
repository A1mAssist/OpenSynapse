using OpenSynapse.Windows.Devices;

namespace OpenSynapse.App.ViewModels;

/// <summary>One button assignment as the shared mapping model sees it.</summary>
public sealed record RazerMappingAssignment(byte FunctionId, IReadOnlyList<byte> Data);

/// <summary>
/// HID and BLE mapping share rows, actions and read-back rules; only the transport
/// differs. The two adapters below keep protocol constants out of the view model.
/// </summary>
public interface IRazerMappingTransport
{
    Task<IReadOnlyList<byte>> GetButtonIdsAsync(CancellationToken cancellationToken = default);

    Task<RazerMappingAssignment> GetAssignmentAsync(
        byte buttonId,
        byte mode,
        CancellationToken cancellationToken = default);

    Task SetAssignmentAsync(
        byte buttonId,
        byte mode,
        byte functionId,
        IReadOnlyList<byte> data,
        CancellationToken cancellationToken = default);
}

public sealed class RazerHidMappingTransport(
    RazerHidDeviceService service,
    RazerHidDeviceConnection connection) : IRazerMappingTransport
{
    public Task<IReadOnlyList<byte>> GetButtonIdsAsync(CancellationToken cancellationToken = default) =>
        service.GetMappingButtonIdsAsync(connection, cancellationToken);

    public async Task<RazerMappingAssignment> GetAssignmentAsync(
        byte buttonId,
        byte mode,
        CancellationToken cancellationToken = default)
    {
        var assignment = await service.GetButtonAssignmentAsync(
            connection, buttonId, mode, cancellationToken).ConfigureAwait(false);
        return new(assignment.FunctionId, assignment.Data);
    }

    public Task SetAssignmentAsync(
        byte buttonId,
        byte mode,
        byte functionId,
        IReadOnlyList<byte> data,
        CancellationToken cancellationToken = default) =>
        service.SetButtonAssignmentAsync(
            connection, buttonId, mode, functionId, data, cancellationToken);
}

public sealed class RazerBleMappingTransport(RazerBleDeviceService service) : IRazerMappingTransport
{
    public Task<IReadOnlyList<byte>> GetButtonIdsAsync(CancellationToken cancellationToken = default) =>
        service.GetMappingButtonIdsAsync(cancellationToken);

    public async Task<RazerMappingAssignment> GetAssignmentAsync(
        byte buttonId,
        byte mode,
        CancellationToken cancellationToken = default)
    {
        var assignment = await service.GetButtonAssignmentAsync(
            buttonId, mode, cancellationToken).ConfigureAwait(false);
        return new(assignment.FunctionId, assignment.Data);
    }

    public Task SetAssignmentAsync(
        byte buttonId,
        byte mode,
        byte functionId,
        IReadOnlyList<byte> data,
        CancellationToken cancellationToken = default) =>
        service.SetButtonAssignmentAsync(
            buttonId, mode, functionId, data.ToArray(), cancellationToken);
}
