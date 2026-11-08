using OpenSynapse.Windows.Protocols;
using OpenSynapse.Core.Devices;
using OpenSynapse.Core.Profiles;
using OpenSynapse.Windows.Devices;

namespace OpenSynapse.App.ViewModels;

public sealed partial class RazerAwBleHeadsetViewModel
{
    [ProfileSetting("headset.custom-eq", DeviceCategory.Headset, powerScoped: true)]
    public Task ApplyCustomEqAsync(CancellationToken cancellationToken = default)
    {
        var levels = CustomEqBands
            .Select(band => (sbyte)Math.Round(band.Level))
            .ToArray();
        var enabled = CustomEqEnabled;
        return RunWriteAsync(
            service => CreateCustomEqSnapshotAsync(service, levels, enabled, cancellationToken),
            applied =>
            {
                ApplyCustomEqBands(applied.Levels);
                SelectedEqPresetIndex = IndexOfPreset(applied.Preset);
            },
            PersistCustomEqAsync,
            ct => ReadCustomEqSnapshotAsync(ct),
            RestoreCustomEqAsync);
    }

    [ProfileSetting("headset.mic-custom-eq", DeviceCategory.Headset, powerScoped: true)]
    public Task ApplyMicCustomEqAsync(CancellationToken cancellationToken = default)
    {
        var levels = MicCustomEqBands
            .Select(band => (sbyte)Math.Round(band.Level))
            .ToArray();
        var enabled = MicCustomEqEnabled;
        return RunWriteAsync(
            service => CreateMicCustomEqSnapshotAsync(service, levels, enabled, cancellationToken),
            applied =>
            {
                ApplyMicCustomEqBands(applied.Levels);
                SelectedMicEqPresetIndex = IndexOfMicPreset(applied.Preset);
            },
            PersistMicCustomEqAsync,
            ct => ReadMicCustomEqSnapshotAsync(ct),
            RestoreMicCustomEqAsync);
    }

    private async Task<CustomEqSnapshot> CreateCustomEqSnapshotAsync(
        RazerAwBleHeadsetService service,
        IReadOnlyList<sbyte> levels,
        bool enabled,
        CancellationToken cancellationToken)
    {
        var applied = await service.SetCustomEqAsync(levels, enabled, cancellationToken);
        return new CustomEqSnapshot(
            enabled ? RazerAwBleProtocol.CustomEqPreset : RazerAwBleProtocol.DefaultEqPreset,
            applied);
    }

    private async Task<CustomEqSnapshot> CreateMicCustomEqSnapshotAsync(
        RazerAwBleHeadsetService service,
        IReadOnlyList<sbyte> levels,
        bool enabled,
        CancellationToken cancellationToken)
    {
        var applied = await service.SetMicCustomEqAsync(levels, enabled, cancellationToken);
        return new CustomEqSnapshot(
            enabled ? RazerAwBleProtocol.CustomEqPreset : RazerAwBleProtocol.DefaultEqPreset,
            applied);
    }

    private async Task<CustomEqSnapshot> ReadCustomEqSnapshotAsync(CancellationToken cancellationToken)
    {
        var preset = await _service!.ReadEqPresetAsync(cancellationToken);
        var levels = await _service!.ReadCustomEqBandsAsync(cancellationToken);
        return new CustomEqSnapshot(preset, levels);
    }

    private async Task<CustomEqSnapshot> ReadMicCustomEqSnapshotAsync(CancellationToken cancellationToken)
    {
        var preset = await _service!.ReadMicEqPresetAsync(cancellationToken);
        var levels = await _service!.ReadMicCustomEqBandsAsync(cancellationToken);
        return new CustomEqSnapshot(preset, levels);
    }

    private Task RestoreCustomEqAsync(CustomEqSnapshot previous, CancellationToken cancellationToken) =>
        RestoreCustomEqCoreAsync(previous, custom: true, cancellationToken);

    private Task RestoreMicCustomEqAsync(CustomEqSnapshot previous, CancellationToken cancellationToken) =>
        RestoreCustomEqCoreAsync(previous, custom: false, cancellationToken);

    private async Task RestoreCustomEqCoreAsync(
        CustomEqSnapshot previous,
        bool custom,
        CancellationToken cancellationToken)
    {
        if (custom)
        {
            await _service!.SetCustomEqBandsAsync(previous.Levels, cancellationToken);
            await _service!.SetEqPresetAsync(previous.Preset, cancellationToken);
        }
        else
        {
            await _service!.SetMicCustomEqBandsAsync(previous.Levels, cancellationToken);
            await _service!.SetMicEqPresetAsync(previous.Preset, cancellationToken);
        }
    }
}
