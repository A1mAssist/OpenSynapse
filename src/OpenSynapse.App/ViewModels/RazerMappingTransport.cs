using OpenSynapse.Core.Devices;
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


/// <summary>
/// Product 184 keeps one profile and two layers. The Viper reader returns all sixteen
/// records at once, so this adapter caches them for the shared row model and writes one
/// assignment at a time through the same read/backup/readback path the native page used.
/// </summary>
public sealed class RazerViperMappingTransport(
    IRazerDeviceTelemetryReader reader,
    Func<IReadOnlyList<DeviceDescriptor>> devices) : IRazerMappingTransport
{
    private const byte ProfileId = 1;
    private readonly Dictionary<(byte ButtonId, byte Mode), ViperButtonAssignment> _assignments = [];

    public async Task<IReadOnlyList<byte>> GetButtonIdsAsync(
        CancellationToken cancellationToken = default)
    {
        var all = await reader.ReadViperButtonAssignmentsAsync(devices(), cancellationToken)
            .ConfigureAwait(false);
        _assignments.Clear();
        foreach (var assignment in all)
        {
            _assignments[(assignment.ButtonId, (byte)assignment.Layer)] = assignment;
        }

        return all
            .Select(assignment => assignment.ButtonId)
            .Distinct()
            .OrderBy(id => id)
            .ToArray();
    }

    public Task<RazerMappingAssignment> GetAssignmentAsync(
        byte buttonId,
        byte mode,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!_assignments.TryGetValue((buttonId, mode), out var assignment))
        {
            throw new InvalidOperationException(
                $"The Viper did not return button {buttonId} layer {mode}.");
        }

        return Task.FromResult(new RazerMappingAssignment(
            (byte)assignment.Function,
            assignment.FunctionData));
    }

    public async Task SetAssignmentAsync(
        byte buttonId,
        byte mode,
        byte functionId,
        IReadOnlyList<byte> data,
        CancellationToken cancellationToken = default)
    {
        var requested = new ViperButtonAssignment(
            ProfileId,
            buttonId,
            (ViperButtonMappingLayer)mode,
            (ViperButtonMappingFunction)functionId,
            data.ToArray());
        var actual = await reader.SetViperButtonAssignmentAsync(
            devices(), requested, cancellationToken).ConfigureAwait(false);
        _assignments[(buttonId, mode)] = actual;
    }
}


/// <summary>
/// The standalone keyboard service owns protocol 25/30 frames and the onboard macro page.
/// This adapter adds the same shared mapping row contract without creating a second
/// keyboard-specific action model.
/// </summary>
public sealed class RazerKeyboardMappingTransport(RazerKeyboardService service) : IRazerMappingTransport
{
    public Task<IReadOnlyList<byte>> GetButtonIdsAsync(CancellationToken cancellationToken = default) =>
        service.GetMappingButtonIdsAsync(cancellationToken);

    public async Task<RazerMappingAssignment> GetAssignmentAsync(
        byte buttonId,
        byte mode,
        CancellationToken cancellationToken = default)
    {
        var assignment = await service.GetMappingAssignmentAsync(
            buttonId, mode, cancellationToken).ConfigureAwait(false);
        return new(assignment.FunctionId, assignment.Data);
    }

    public Task SetAssignmentAsync(
        byte buttonId,
        byte mode,
        byte functionId,
        IReadOnlyList<byte> data,
        CancellationToken cancellationToken = default) =>
        service.SetMappingAssignmentAsync(
            buttonId, mode, functionId, data.ToArray(), cancellationToken);
}
