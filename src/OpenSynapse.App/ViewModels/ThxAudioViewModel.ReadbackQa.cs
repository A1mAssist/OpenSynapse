#if DEBUG
using Microsoft.UI.Xaml;
using OpenSynapse.Core.Audio;

namespace OpenSynapse.App.ViewModels;

public sealed partial class ThxAudioViewModel
{
    internal static async Task<string> VerifyReadbackAsync()
    {
        static void Check(bool condition, string detail)
        {
            if (!condition) throw new InvalidOperationException("THX readback: " + detail);
        }

        var vm = new ThxAudioViewModel(null, true);
        var state = new ThxAudioState(ThxAudioEndpoint.Speakers, false, true,
            "game", [new("game", "Game")], [new(250, 2.5)],
            new Dictionary<string, IReadOnlyList<double>> { ["game"] = new[] { 1d } },
            null, new(true, 50), true, BassBoost: new(false, 0));
        vm.ApplyStatus(new(ThxAudioAvailability.Available, null, state));
        vm.Bands[0].GainDb = 9;
        var rejected = new ThxAudioStatus(ThxAudioAvailability.OperationRejected,
            "ThxOperationRejected:selectPreset", state);
        await vm.ExecuteAsync(() => Task.FromResult(rejected));

        Check(vm.ControlsVisibility == Visibility.Visible && vm.CanEdit && !vm.IsBusy,
            "rejected write hid or disabled the usable controls");
        Check(vm.HasError && vm.StatusText == AppStrings.Text("ThxStatusOperationRejected"),
            "rejected write reported ready");
        Check(vm.Bands[0].GainDb == 2.5 && !vm.HasEqualizerChanges &&
            vm._presetCurves[ThxAudioEndpoint.Speakers]["game"][0] == 2.5,
            "failed draft replaced actual readback");
        Check(vm.SoundNormalizationVisibility == Visibility.Collapsed &&
            vm.VoiceClarityLevelText == "50%" && vm.BassBoostLevelText == "0%",
            "optional failure changed another enhancement");

        vm.ApplyStatus(rejected with { State = state with { VoiceClarity = null, SelectedPresetId = null } });
        Check(vm.ControlsVisibility == Visibility.Visible && vm.SelectedPresetIndex == -1 &&
            !vm.IsEditablePreset && !vm.CanEditVoiceClarity && vm.VoiceClarityLevelText == "—" && vm.VoiceClarityLevel == 50,
            "missing native values became an active preset or a zero level");
        vm.VoiceClarityEnabled = true;
        vm.SoundNormalizationEnabled = true;
        Check(!vm.VoiceClarityEnabled && !vm.SoundNormalizationEnabled,
            "unavailable enhancement accepted a toggle");

        await vm.ExecuteAsync(() => Task.FromResult(new ThxAudioStatus(
            ThxAudioAvailability.Available, null, state)));
        Check(!vm.HasError && vm.StatusText == AppStrings.Text("ThxStatusReady") && vm.CanEdit,
            "successful retry did not clear the error");
        vm.ApplyStatus(rejected with { State = null });
        Check(vm.ControlsVisibility == Visibility.Collapsed && !vm.CanEdit,
            "missing readback left stale controls enabled");
        return "PASS THX: rejected readback, actual curve cache, optional values, missing selection, retry";
    }
}
#endif
