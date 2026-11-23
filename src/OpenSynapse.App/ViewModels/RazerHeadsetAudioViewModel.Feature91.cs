using System.Globalization;
using OpenSynapse.Windows.Devices;
using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.App.ViewModels;

public sealed partial class RazerHeadsetAudioViewModel
{
    private bool IsNommoV2Audio => _connection.Transport == RazerHeadsetAudioTransport.Feature91 &&
        RazerAudioFeatureProtocol.IsNommoV2(_connection.ProductId);
    private bool IsMarciAudio => _connection.Transport == RazerHeadsetAudioTransport.Feature91 &&
        _connection.ProductId == 0x0F66;
    private bool IsProductFeature91Audio => IsNommoV2Audio || IsMarciAudio;

    private static readonly (byte Value, string Key)[] NommoV2Presets =
    [
        (0, "RazerHeadsetPresetDefault"), (1, "RazerHeadsetPresetGame"),
        (2, "RazerHeadsetPresetMusic"), (3, "RazerHeadsetPresetMovie"),
        (4, "RazerHeadsetPresetTreble"),
    ];

    private static readonly (byte Value, string Key)[] MarciPresets =
    [
        (1, "RazerHeadsetPresetGame"), (2, "RazerHeadsetPresetMusic"),
        (3, "RazerHeadsetPresetMovie"), (16, "RazerHeadsetPresetCustom"),
    ];

    private static readonly HeadsetCapability[] NommoV2Capabilities =
        [HeadsetCapability.Firmware, HeadsetCapability.EqPreset, HeadsetCapability.Bass];
    private static readonly HeadsetCapability[] MarciCapabilities =
        [HeadsetCapability.Firmware, HeadsetCapability.EqPreset, HeadsetCapability.EqBands];
    private HeadsetCapability[] Feature91ProductCapabilities => IsNommoV2Audio
        ? NommoV2Capabilities : IsMarciAudio ? MarciCapabilities : Feature91Capabilities;

    private static Dictionary<string, int> MarciBandMap(IReadOnlyList<byte> bands) =>
        bands.Select((value, index) => (value, index)).ToDictionary(
            item => item.index.ToString(CultureInfo.InvariantCulture),
            item => (int)unchecked((sbyte)item.value));

    private async Task SaveMarciEqAsync(bool custom, CancellationToken cancellationToken)
    {
        var preset = custom ? (byte)16 : ActivePresets[SelectedEqPresetIndex].Value;
        var bands = EqBands.Select(band => RazerAudioFeatureProtocol.EncodeMarciEqBand(band.Level)).ToArray();
        byte previousPreset = 0;
        byte[] previousBands = [];
        if (!await RunWriteAsync(async (service, token) =>
        {
            var snapshot = await service.ReadStateAsync(token);
            previousPreset = snapshot.EqPreset ?? throw new IOException("Marci EQ preset could not be read.");
            previousBands = snapshot.EqBands?.ToArray() ?? throw new IOException("Marci EQ bands could not be read.");
            if (custom) await service.SetCustomEqAsync(preset, bands, token);
            else await service.SetEqPresetAsync(preset, token);
        }, cancellationToken))
        {
            if (previousBands.Length == 10)
            {
                SelectedEqPresetIndex = Array.FindIndex(MarciPresets, item => item.Value == previousPreset);
                ApplyEqBands(previousBands);
            }
            return;
        }

        SelectedEqPresetIndex = Array.FindIndex(MarciPresets, item => item.Value == preset);
        var persisted = ResolveHeadsetProfileSettings();
        var persistedPreset = persisted?.EqPreset;
        var persistedBands = persisted?.EqBands;
        var persistedTenths = persisted?.EqBandsTenths;
        await SaveHeadsetSettingAsync(
            settings =>
            {
                settings.EqPreset = preset.ToString("X2");
                if (custom)
                {
                    settings.EqBandsTenths = MarciBandMap(bands);
                    settings.EqBands = null;
                }
            },
            settings =>
            {
                settings.EqPreset = persistedPreset;
                settings.EqBandsTenths = persistedTenths;
                settings.EqBands = persistedBands;
            },
            () =>
            {
                SelectedEqPresetIndex = Array.FindIndex(MarciPresets, item => item.Value == previousPreset);
                ApplyEqBands(previousBands);
            },
            token => OpenService().SetCustomEqAsync(previousPreset, previousBands, token),
            cancellationToken);
        await RefreshEqBandsAsync(cancellationToken);
    }
}
