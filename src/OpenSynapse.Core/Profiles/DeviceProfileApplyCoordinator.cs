using OpenSynapse.Core.Devices;
using OpenSynapse.Core.Sensors;

namespace OpenSynapse.Core.Profiles;

public interface IDeviceProfileApplier
{
    IReadOnlyCollection<DeviceCategory> Categories { get; }

    /// <summary>
    /// Lets two transports (for example RazerHid and BLE) claim the same category.
    /// </summary>
    bool Accepts(DeviceDescriptor device) => true;


    Task<ProfileDeviceApplyResult> ApplyAsync(
        DeviceProfileApplyContext context,
        CancellationToken cancellationToken);
}

public sealed record DeviceProfileApplyContext(
    ProfileDocument Document,
    DeviceDescriptor Device,
    PowerSourceKind PowerSource,
    ResolvedProfile Profile);

public enum ProfileSettingApplyStatus
{
    Applied,
    AppliedUnverified,
    Skipped,
    Failed,
}

public sealed record ProfileSettingApplyResult(
    string Key,
    ProfileSettingApplyStatus Status,
    string? Error = null);

public enum ProfileDeviceApplyStatus
{
    Applied,
    Skipped,
    Failed,
}

public sealed record ProfileDeviceApplyResult(
    string DeviceId,
    ProfileDeviceApplyStatus Status,
    IReadOnlyList<ProfileSettingApplyResult> Items);

public sealed record DeviceProfileApplySummary(
    IReadOnlyList<ProfileDeviceApplyResult> Devices,
    IReadOnlyList<string> Errors)
{
    public bool Succeeded => Errors.Count == 0;
}

/// <summary>
/// Dispatches one resolved profile to the appliers of the connected devices.
/// Each device is applied serially and appliers run with a bounded overlap; one
/// device failing never stops the others.
/// </summary>
public sealed class DeviceProfileApplyCoordinator
{
    private readonly IReadOnlyList<IDeviceProfileApplier> _appliers;
    private readonly int _maxConcurrentDevices;

    public DeviceProfileApplyCoordinator(
        IEnumerable<IDeviceProfileApplier> appliers,
        int maxConcurrentDevices = 2)
    {
        ArgumentNullException.ThrowIfNull(appliers);
        if (maxConcurrentDevices < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(maxConcurrentDevices));
        }

        _appliers = appliers.ToArray();
        foreach (var applier in _appliers)
        {
            ArgumentNullException.ThrowIfNull(applier);
            if (applier.Categories.Count == 0)
            {
                throw new ArgumentException("A profile applier must declare at least one category.", nameof(appliers));
            }
        }
        _maxConcurrentDevices = maxConcurrentDevices;
    }

    public async Task<DeviceProfileApplySummary> ApplyAsync(
        ProfileDocument document,
        IReadOnlyList<DeviceDescriptor> devices,
        PowerSourceKind powerSource,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(devices);

        var uniqueDevices = devices
            .Where(device => !string.IsNullOrWhiteSpace(device.Id))
            .DistinctBy(device => device.Id)
            .ToArray();
        var results = new ProfileDeviceApplyResult[uniqueDevices.Length];
        using var gate = new SemaphoreSlim(_maxConcurrentDevices);

        async Task RunAsync(DeviceDescriptor device, int index)
        {
            await gate.WaitAsync(cancellationToken);
            try
            {
                results[index] = await ApplyDeviceAsync(document, device, powerSource, cancellationToken);
            }
            finally
            {
                gate.Release();
            }
        }

        await Task.WhenAll(uniqueDevices.Select(RunAsync));
        var errors = results
            .SelectMany(result => result.Items
                .Where(item => item.Status == ProfileSettingApplyStatus.Failed)
                .Select(item => $"{result.DeviceId}/{item.Key}: {item.Error}"))
            .ToArray();
        return new DeviceProfileApplySummary(results, errors);
    }

    private async Task<ProfileDeviceApplyResult> ApplyDeviceAsync(
        ProfileDocument document,
        DeviceDescriptor device,
        PowerSourceKind powerSource,
        CancellationToken cancellationToken)
    {
        var applier = _appliers.FirstOrDefault(candidate =>
            candidate.Categories.Contains(device.Category) && candidate.Accepts(device));
        if (applier is null)
        {
            return new ProfileDeviceApplyResult(device.Id, ProfileDeviceApplyStatus.Skipped, []);
        }

        var profile = ProfileResolver.Resolve(document, device, powerSource);
        try
        {
            var result = await applier.ApplyAsync(
                new DeviceProfileApplyContext(document, device, powerSource, profile),
                cancellationToken);
            return result with { DeviceId = device.Id };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            return new ProfileDeviceApplyResult(
                device.Id,
                ProfileDeviceApplyStatus.Failed,
                [new ProfileSettingApplyResult("device", ProfileSettingApplyStatus.Failed, exception.Message)]);
        }
    }
}
