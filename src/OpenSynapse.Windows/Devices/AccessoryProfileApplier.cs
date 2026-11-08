using OpenSynapse.Core.Devices;
using OpenSynapse.Core.Profiles;
using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.Windows.Devices;

/// <summary>
/// Applies the mixer and camera sections. Both devices are Accessory rows, so
/// one applier dispatches by product ID and keeps each setting independent.
/// </summary>
public sealed class AccessoryProfileApplier : IDeviceProfileApplier
{
    private static readonly DeviceCategory[] SupportedCategories = [DeviceCategory.Accessory];
    private readonly Func<DeviceDescriptor, CancellationToken, Task<RazerMixer053EService?>> _connectMixer;
    private readonly Func<DeviceDescriptor, CancellationToken, Task<IKiyoProfileDevice?>> _connectKiyo;

    public AccessoryProfileApplier(Action<string>? diagnostic = null)
        : this(
            (device, cancellationToken) => ConnectMixerAsync(device, diagnostic, cancellationToken),
            (device, cancellationToken) => ConnectKiyoAsync(device, diagnostic, cancellationToken))
    {
    }

    internal AccessoryProfileApplier(
        Func<DeviceDescriptor, CancellationToken, Task<RazerMixer053EService?>> connectMixer,
        Func<DeviceDescriptor, CancellationToken, Task<IKiyoProfileDevice?>> connectKiyo)
    {
        _connectMixer = connectMixer ?? throw new ArgumentNullException(nameof(connectMixer));
        _connectKiyo = connectKiyo ?? throw new ArgumentNullException(nameof(connectKiyo));
    }

    public IReadOnlyCollection<DeviceCategory> Categories => SupportedCategories;

    public bool Accepts(DeviceDescriptor device) =>
        device.ProductId == RazerMixer053EProtocol.ProductId ||
        RazerKiyoDevices.SupportedProductIds.Contains(device.ProductId);

    public async Task<ProfileDeviceApplyResult> ApplyAsync(
        DeviceProfileApplyContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (context.Device.Category != DeviceCategory.Accessory)
        {
            return new ProfileDeviceApplyResult(context.Device.Id, ProfileDeviceApplyStatus.Skipped, []);
        }

        if (context.Device.ProductId == RazerMixer053EProtocol.ProductId)
        {
            await using var service = await _connectMixer(context.Device, cancellationToken);
            return service is null
                ? Failed(context.Device.Id, "The audio mixer is not available.")
                : await ApplyMixerAsync(context, service, cancellationToken);
        }

        if (RazerKiyoDevices.SupportedProductIds.Contains(context.Device.ProductId))
        {
            using var service = await _connectKiyo(context.Device, cancellationToken);
            return service is null
                ? Failed(context.Device.Id, "The camera is not available.")
                : await ApplyKiyoAsync(context, service, cancellationToken);
        }

        return new ProfileDeviceApplyResult(context.Device.Id, ProfileDeviceApplyStatus.Skipped, []);
    }

    private static async Task<ProfileDeviceApplyResult> ApplyMixerAsync(
        DeviceProfileApplyContext context,
        RazerMixer053EService service,
        CancellationToken cancellationToken)
    {
        RazerMixer053EState state;
        try
        {
            state = await service.ReadStateAsync(cancellationToken);
        }
        catch (Exception exception) when (IsExpected(exception))
        {
            return Failed(context.Device.Id, exception.Message);
        }

        if (!state.Available || state.Hardware is not { } hardware)
        {
            return Failed(context.Device.Id, "The audio mixer did not answer.");
        }

        var key = string.IsNullOrWhiteSpace(state.SerialNumber)
            ? ProfileResolver.GetDeviceKey(context.Device)
            : state.SerialNumber;
        var settings = ProfileResolver.ResolveWithDeviceKey(
            context.Document,
            context.Device,
            context.PowerSource,
            key).Mixer;
        var results = new List<ProfileSettingApplyResult>();

        if (settings.ChannelMutes is { Count: 4 } mutes)
        {
            for (var channel = 1; channel <= 4; channel++)
            {
                var desired = mutes[channel - 1];
                var itemKey = $"mixer.channel-{channel}-mute";
                if (hardware.IsMuted(channel) == desired)
                {
                    results.Add(new(itemKey, ProfileSettingApplyStatus.Skipped));
                    continue;
                }

                try
                {
                    await service.SetChannelMuteAsync(channel, desired, cancellationToken);
                    results.Add(new(itemKey, ProfileSettingApplyStatus.Applied));
                }
                catch (Exception exception) when (IsExpected(exception))
                {
                    results.Add(new(itemKey, ProfileSettingApplyStatus.Failed, exception.Message));
                }
            }
        }

        if (settings.MicMute is { } micMute)
        {
            const string itemKey = "mixer.mic-mute";
            if (hardware.MicMute == micMute)
            {
                results.Add(new(itemKey, ProfileSettingApplyStatus.Skipped));
            }
            else
            {
                try
                {
                    await service.SetMicMuteAsync(micMute, cancellationToken);
                    results.Add(new(itemKey, ProfileSettingApplyStatus.Applied));
                }
                catch (Exception exception) when (IsExpected(exception))
                {
                    results.Add(new(itemKey, ProfileSettingApplyStatus.Failed, exception.Message));
                }
            }
        }

        if (settings.BrightnessPercent is { } brightness)
        {
            const string itemKey = "mixer.brightness";
            if (state.Brightness == brightness)
            {
                results.Add(new(itemKey, ProfileSettingApplyStatus.Skipped));
            }
            else
            {
                try
                {
                    await service.SetBrightnessAsync((byte)brightness, cancellationToken: cancellationToken);
                    results.Add(new(itemKey, ProfileSettingApplyStatus.Applied));
                }
                catch (Exception exception) when (IsExpected(exception))
                {
                    results.Add(new(itemKey, ProfileSettingApplyStatus.Failed, exception.Message));
                }
            }
        }

        return new ProfileDeviceApplyResult(context.Device.Id, Status(results), results);
    }

    private static Task<ProfileDeviceApplyResult> ApplyKiyoAsync(
        DeviceProfileApplyContext context,
        IKiyoProfileDevice service,
        CancellationToken cancellationToken)
    {
        RazerKiyoSnapshot snapshot;
        try
        {
            snapshot = service.ReadSnapshot();
        }
        catch (Exception exception) when (IsExpected(exception))
        {
            return Task.FromResult(Failed(context.Device.Id, exception.Message));
        }

        if (snapshot.WhiteBalance is not { } current)
        {
            return Task.FromResult(Failed(context.Device.Id, "White balance is not available."));
        }

        var key = string.IsNullOrWhiteSpace(snapshot.SerialNumber)
            ? ProfileResolver.GetDeviceKey(context.Device)
            : snapshot.SerialNumber;
        var settings = ProfileResolver.ResolveWithDeviceKey(
            context.Document,
            context.Device,
            context.PowerSource,
            key).Kiyo;
        var results = new List<ProfileSettingApplyResult>();

        if (settings.WhiteBalanceAuto is { } auto)
        {
            const string itemKey = "kiyo.white-balance-auto";
            if (current.IsAuto == auto)
            {
                results.Add(new(itemKey, ProfileSettingApplyStatus.Skipped));
            }
            else if (!service.Api.HasFlag(RazerKiyoApi.WriteWhiteBalance))
            {
                results.Add(new(itemKey, ProfileSettingApplyStatus.Failed, "White balance is read-only."));
            }
            else
            {
                try
                {
                    current = service.SetWhiteBalanceAuto(auto);
                    results.Add(new(itemKey, ProfileSettingApplyStatus.Applied));
                }
                catch (Exception exception) when (IsExpected(exception))
                {
                    results.Add(new(itemKey, ProfileSettingApplyStatus.Failed, exception.Message));
                }
            }
        }

        if (settings.WhiteBalanceTemperature is { } temperature && !current.IsAuto)
        {
            const string itemKey = "kiyo.white-balance-temperature";
            results.Add(current.Value == temperature
                ? new(itemKey, ProfileSettingApplyStatus.Skipped)
                : new(
                    itemKey,
                    ProfileSettingApplyStatus.Failed,
                    "The camera DLL exposes no direct temperature write; use the native settings dialog."));
        }

        return Task.FromResult(new ProfileDeviceApplyResult(context.Device.Id, Status(results), results));
    }

    private static async Task<RazerMixer053EService?> ConnectMixerAsync(
        DeviceDescriptor device,
        Action<string>? diagnostic,
        CancellationToken cancellationToken)
    {
        var connection = await RazerHeadsetAudioService.FindAsync(diagnostic, cancellationToken);
        return connection is null || connection.ProductId != device.ProductId ||
            connection.ProductId != RazerMixer053EProtocol.ProductId
            ? null
            : new RazerMixer053EService(connection.DevicePath, diagnostic);
    }

    private static async Task<IKiyoProfileDevice?> ConnectKiyoAsync(
        DeviceDescriptor device,
        Action<string>? diagnostic,
        CancellationToken cancellationToken)
    {
        var service = await RazerKiyoService.ConnectAsync(diagnostic, cancellationToken);
        if (service is null || service.ProductId != device.ProductId)
        {
            service?.Dispose();
            return null;
        }

        return new KiyoProfileDevice(service);
    }

    private static ProfileDeviceApplyStatus Status(List<ProfileSettingApplyResult> results) =>
        results.Any(item => item.Status == ProfileSettingApplyStatus.Failed)
            ? ProfileDeviceApplyStatus.Failed
            : results.Any(item => item.Status is ProfileSettingApplyStatus.Applied or
                ProfileSettingApplyStatus.AppliedUnverified)
                ? ProfileDeviceApplyStatus.Applied
                : ProfileDeviceApplyStatus.Skipped;

    private static ProfileDeviceApplyResult Failed(string deviceId, string error) =>
        new(deviceId, ProfileDeviceApplyStatus.Failed,
            [new("device", ProfileSettingApplyStatus.Failed, error)]);

    private static bool IsExpected(Exception exception) =>
        exception is IOException or InvalidDataException or InvalidOperationException or
            NotSupportedException or UnauthorizedAccessException or ArgumentException or
            System.ComponentModel.Win32Exception or TimeoutException;
}

internal interface IKiyoProfileDevice : IDisposable
{
    int ProductId { get; }
    RazerKiyoApi Api { get; }
    RazerKiyoSnapshot ReadSnapshot();
    RazerKiyoValue SetWhiteBalanceAuto(bool auto);
}

internal sealed class KiyoProfileDevice(RazerKiyoService service) : IKiyoProfileDevice
{
    public int ProductId => service.ProductId;
    public RazerKiyoApi Api => service.Api;
    public RazerKiyoSnapshot ReadSnapshot() => service.ReadSnapshot();
    public RazerKiyoValue SetWhiteBalanceAuto(bool auto) => service.SetWhiteBalanceAuto(auto);
    public void Dispose() => service.Dispose();
}
