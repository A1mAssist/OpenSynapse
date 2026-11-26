using System.Globalization;
using Microsoft.UI.Xaml;
using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.App.ViewModels;

public sealed partial class RazerHeadsetAudioViewModel
{
    private static readonly (byte Value, string Key)[] CarolPresets =
    [
        (1, "RazerHeadsetPresetGame"), (2, "RazerHeadsetPresetMusic"),
        (3, "RazerHeadsetPresetMovie"), (16, "RazerHeadsetPresetCustom"),
    ];
    private bool _hasCarolPower;
    private byte? _carolPreset;
    private byte[]? _carolBands;
    private byte? _carolExternalPowerStatus;

    public Visibility CarolExternalPowerVisibility => IsCarol && _carolExternalPowerStatus is not null
        ? Visibility.Visible : Visibility.Collapsed;
    public string CarolExternalPowerText => AppStrings.Text("BladeLightingPowerPluggedIn") + ": " +
        (_carolExternalPowerStatus switch
        {
            0 => AppStrings.Text("TaskbarBatteryDisconnected"),
            1 => AppStrings.Text("BlackSharkStatusConnected"),
            { } value => $"0x{value:X2}",
            _ => "--",
        });

    private void ApplyCarolExternalPower(byte? status)
    {
        _carolExternalPowerStatus = status;
        OnPropertyChanged(nameof(CarolExternalPowerVisibility));
        OnPropertyChanged(nameof(CarolExternalPowerText));
    }

    private void ApplyCarolPowerState(RazerCarolPowerState power)
    {
        AutoSleepEnabled = power.Enabled;
        SelectAutoSleepMinutes(power.Seconds / 60);
        ConfirmAudioSetting(HeadsetAudioSetting.AutoSleep, (AutoSleepEnabled, SelectedAutoSleepMinutesIndex));
        _hasCarolPower = true;
        OnPropertyChanged(nameof(MakoXPowerVisibility));
    }

    private static Dictionary<string, int> CarolBandMap(IReadOnlyList<byte> bands) =>
        bands.Select((value, index) => (index, value)).ToDictionary(
            item => item.index.ToString(CultureInfo.InvariantCulture), item => (int)unchecked((sbyte)item.value));

    private async Task SaveCarolEqAsync(bool custom, CancellationToken cancellationToken)
    {
        var preset = custom ? (byte)16 : ActivePresets[SelectedEqPresetIndex].Value;
        var bands = EqBands.Select(band => ToFirmwareBand(band.Level)).ToArray();
        byte previousPreset = 0;
        byte[] previousBands = [];
        if (!await RunWriteAsync(async (service, token) =>
        {
            var previous = await service.ReadStateAsync(token);
            previousPreset = previous.EqPreset ?? throw new IOException("Carol EQ preset could not be read.");
            previousBands = previous.EqBands?.ToArray() ?? throw new IOException("Carol EQ bands could not be read.");
            if (custom) await service.SetCustomEqAsync(preset, bands, token);
            else await service.SetEqPresetAsync(preset, token);
        }, cancellationToken))
        {
            if (_carolPreset is { } selected) SelectedEqPresetIndex = Array.FindIndex(CarolPresets, item => item.Value == selected);
            if (_carolBands is { } current) ApplyEqBands(current);
            return;
        }
        ConfirmAudioSetting(HeadsetAudioSetting.EqPreset, Array.FindIndex(CarolPresets, item => item.Value == preset));
        SelectedEqPresetIndex = Array.FindIndex(CarolPresets, item => item.Value == preset);
        await SaveHeadsetSettingAsync(
            settings =>
            {
                settings.EqPreset = preset.ToString("X2");
                if (custom)
                {
                    settings.EqBandsTenths = CarolBandMap(bands);
                    settings.EqBands = null;
                }
            },
            settings =>
            {
                settings.EqPreset = previousPreset.ToString("X2");
                settings.EqBandsTenths = CarolBandMap(previousBands);
            },
            () =>
            {
                ConfirmAudioSetting(HeadsetAudioSetting.EqPreset, Array.FindIndex(CarolPresets, item => item.Value == previousPreset));
                SelectedEqPresetIndex = Array.FindIndex(CarolPresets, item => item.Value == previousPreset);
                ApplyEqBands(previousBands);
            },
            token => OpenService().SetCustomEqAsync(previousPreset, previousBands, token), cancellationToken);
        await RefreshEqBandsAsync(cancellationToken);
    }

    private async Task SaveCarolPowerAsync(CancellationToken cancellationToken)
    {
        var enabled = AutoSleepEnabled;
        var minutes = AutoSleepMinutes;
        RazerCarolPowerState? previous = null;
        if (!await RunWriteAsync(async (service, token) =>
        {
            previous = await service.ReadCarolPowerAsync(token);
            await service.SetCarolPowerAsync(enabled, minutes, token);
            ApplyCarolPowerState(await service.ReadCarolPowerAsync(token));
        }, cancellationToken))
        {
            if (previous is { } original) ApplyCarolPowerState(original);
            return;
        }
        var snapshot = previous!.Value;
        await SaveHeadsetSettingAsync(
            settings => { settings.AutoSleepEnabled = enabled; settings.AutoShutdownMinutes = minutes; },
            settings => { settings.AutoSleepEnabled = snapshot.Enabled; settings.AutoShutdownMinutes = snapshot.Seconds / 60; },
            () => ApplyCarolPowerState(snapshot),
            token => OpenService().RestoreCarolPowerAsync(snapshot, token), cancellationToken);
    }
}
