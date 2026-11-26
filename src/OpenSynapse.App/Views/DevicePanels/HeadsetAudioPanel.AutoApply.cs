using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using OpenSynapse.App.ViewModels;

namespace OpenSynapse.App.Views.DevicePanels;

public sealed partial class HeadsetAudioPanel
{
    private readonly SettingApplyQueue _audioApplyQueue = new();

    private void InitializeAutoApply()
    {
        AttachAudioSetting(ThxSpatialGroup, HeadsetAudioSetting.ThxSpatial);
        AttachAudioSetting(ThxPresetGroup, HeadsetAudioSetting.ThxPreset);
        AttachAudioSetting(ThxBandsGroup, HeadsetAudioSetting.ThxBands);
        AttachAudioSetting(ThxNormalizationGroup, HeadsetAudioSetting.ThxNormalization);
        AttachAudioSetting(ThxVoiceClarityGroup, HeadsetAudioSetting.ThxVoiceClarity);
        AttachAudioSetting(ThxBassBoostGroup, HeadsetAudioSetting.ThxBassBoost);
        AttachAudioSetting(ThxSidetoneGroup, HeadsetAudioSetting.ThxSidetone);
        AttachAudioSetting(ThxCaptureNormalizationGroup, HeadsetAudioSetting.ThxCaptureNormalization);
        AttachAudioSetting(ThxCaptureVocalClarityGroup, HeadsetAudioSetting.ThxCaptureVocalClarity);
        AttachAudioSetting(ThxCaptureNoiseReductionGroup, HeadsetAudioSetting.ThxCaptureNoiseReduction);
        AttachAudioSetting(ThxCaptureVoiceGateGroup, HeadsetAudioSetting.ThxCaptureVoiceGate);
        AttachAudioSetting(EqPresetGroup, HeadsetAudioSetting.EqPreset);
        AttachAudioSetting(EqBandsGroup, HeadsetAudioSetting.EqBands);
        AttachAudioSetting(MicEqBandsGroup, HeadsetAudioSetting.MicEqBands);
        AttachAudioSetting(BassGroup, HeadsetAudioSetting.Bass);
        AttachAudioSetting(MicrophoneGroup, HeadsetAudioSetting.Microphone);
        AttachAudioSetting(SourceGroup, HeadsetAudioSetting.Source);
        AttachAudioSetting(NoiseGroup, HeadsetAudioSetting.Noise);
        AttachAudioSetting(AncGroup, HeadsetAudioSetting.Anc);
        AttachAudioSetting(AdvancedGroup, HeadsetAudioSetting.Advanced);
        AttachAudioSetting(MicProcessingGroup, HeadsetAudioSetting.MicProcessing);
        AttachAudioSetting(CamyT1Group, HeadsetAudioSetting.CamyT1);
        AttachAudioSetting(MicMonitoringGroup, HeadsetAudioSetting.MicMonitoring);
        AttachAudioSetting(ParametricEqGroup, HeadsetAudioSetting.ParametricEq);
        AttachAudioSetting(CompressorGroup, HeadsetAudioSetting.Compressor);
        AttachAudioSetting(HapticsGroup, HeadsetAudioSetting.Haptics);
        AttachAudioSetting(AutoSleepGroup, HeadsetAudioSetting.AutoSleep);
    }

    private void AttachAudioSetting(StackPanel group, HeadsetAudioSetting setting) =>
        SettingAutoApply.Attach(group, _audioApplyQueue, () => ViewModel, () =>
        {
            if (ViewModel is not { } target || Accessories is not { } accessories ||
                Lifetime.IsCancellationRequested || target.PrepareAutoApply(setting) is not { } apply)
                return null;

            var lifetime = Lifetime;
            return async () =>
            {
                string? error = AppStrings.Text("RazerHeadsetTransportUnavailable");
                await accessories.RunHeadsetOperationAsync(target, async (vm, token) =>
                {
                    token.ThrowIfCancellationRequested();
                    if (!ReferenceEquals(ViewModel, target) || !ReferenceEquals(vm, target))
                        throw new OperationCanceledException();

                    // Restore the captured draft only after polling/earlier writes release the gate.
                    error = await apply(token);
                }, lifetime);
                return error;
            };
        });
}
