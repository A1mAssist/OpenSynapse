using OpenSynapse.Core.Profiles;
using OpenSynapse.Windows.Devices;
using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.App.ViewModels;

public sealed partial class RazerHidDeviceViewModel
{
    private readonly Func<ProfileDocument>? _getAutoApplyProfileDocument;
    private bool? _confirmedGameMode;
    private RazerHidKeyswitchOptimization? _confirmedKeyswitch;
    private (byte Brightness, int Language)? _confirmedOled;
    private (RazerKnobStage[] Stages, byte? Selected)? _confirmedKnob;
    private bool? _confirmedAnalogContinuous;
    private byte? _confirmedAnalogDeadZone;
    private (double Actuation, bool RapidTrigger, double Sensitivity)? _analogSelectionBaseline;
    private byte[] _analogSelectionIds = [];
    private long _analogSelectionRevision;
    private readonly HashSet<byte> _confirmedAnalogKeys = [];
    private (bool Enabled, RazerAnalogSnapTapPair[] Pairs)? _confirmedAnalogSnapTap;

    internal object KeyboardAutoApplyContext => (this, Connection, _getActiveProfile?.Invoke(),
        _getAutoApplyProfileDocument?.Invoke(), _getAutoApplyProfileDocument?.Invoke().ActiveProfileName,
        ActiveOnboardProfileId, _powerStateProvider?.Invoke());

    internal object AnalogKeyAutoApplyContext => (KeyboardAutoApplyContext, _analogSelectionRevision);

    internal Func<Task<string?>>? PrepareGameModeAutoApply(CancellationToken token) =>
        PrepareMouseApply(GameModeEnabled, _confirmedGameMode, true, () => CanEditGameMode,
            value => GameModeEnabled = value, ApplyGameModeAsync, token);

    internal Func<Task<string?>>? PrepareKeyswitchAutoApply(CancellationToken token) =>
        PrepareMouseApply(KeyswitchOptimization, _confirmedKeyswitch, Enum.IsDefined(KeyswitchOptimization),
            () => CanWriteKeyswitch && !IsKeyswitchLoading,
            value => KeyswitchOptimization = value, ApplyKeyswitchAsync, token);

    internal Func<Task<string?>>? PrepareProfileDebounceAutoApply(CancellationToken token) =>
        PrepareMouseApply((ProfileDebounceMake, ProfileDebounceMakeMask, ProfileDebounceBreak, ProfileDebounceBreakMask),
            _basicState?.ProfileDebounce is { } value
                ? ((int)value.MakeDebounceTimeMs, (int)value.MakeMaskTimeMs,
                    (int)value.BreakDebounceTimeMs, (int)value.BreakMaskTimeMs) : null,
            CanEditProfileDebounce, () => CanWriteProfileDebounce,
            value => (ProfileDebounceMake, ProfileDebounceMakeMask, ProfileDebounceBreak, ProfileDebounceBreakMask) = value,
            ApplyProfileDebounceAsync, token);

    internal Func<Task<string?>>? PrepareOledAutoApply(CancellationToken token) =>
        PrepareMouseApply((OledBrightness, SelectedOledLanguageIndex), _confirmedOled,
            OledBrightness <= 100 && SelectedOledLanguageIndex >= 0 && SelectedOledLanguageIndex < OledLanguageValues.Length,
            () => CanEditOled, value => (OledBrightness, SelectedOledLanguageIndex) = value, ApplyOledAsync, token);

    internal Func<Task<string?>>? PrepareKnobAutoApply(CancellationToken token)
    {
        if (_confirmedKnob is not { } confirmed || KnobModeTable is not { } table || !CanApplyKnob) return null;
        var stages = KnobStages.Select(row => row.ToStage()).ToArray();
        var selected = SelectedKnobStage?.Stage;
        if (selected == confirmed.Selected && stages.SequenceEqual(confirmed.Stages)) return null;
        return CaptureMouseApply(() => CanApplyKnob, () =>
        {
            KnobStages.Clear();
            foreach (var stage in stages) KnobStages.Add(new RazerHidKnobStageRowViewModel(stage, table));
            SelectedKnobStage = KnobStages.FirstOrDefault(row => row.Stage == selected);
        }, ApplyKnobAsync, token);
    }

    private void ConfirmKnob() =>
        _confirmedKnob = (KnobStages.Select(row => row.ToStage()).ToArray(), SelectedKnobStage?.Stage);

    internal Func<Task<string?>>? PrepareAnalogKeyAutoApply(CancellationToken token)
    {
        var continuous = AnalogKeyContinuousRapidTrigger;
        var deadZone = _analogKeyBottomDeadZone;
        if (_confirmedAnalogContinuous is null ||
            (continuous == _confirmedAnalogContinuous && deadZone == _confirmedAnalogDeadZone)) return null;
        return CaptureMouseApply(() => CanEditAnalogKey, () =>
        {
            AnalogKeyContinuousRapidTrigger = continuous;
            _analogKeyBottomDeadZone = deadZone;
            SetAnalogDeadZone(deadZone);
        }, ApplyAnalogKeyAsync, token);
    }

    private (double Actuation, bool RapidTrigger, double Sensitivity) AnalogSelectionDraft =>
        (AnalogKeyActuationPointMillimeters, AnalogKeyRapidTriggerEnabled, AnalogKeyRapidTriggerSensitivityMillimeters);

    internal Func<Task<string?>>? PrepareAnalogKeySelectionAutoApply(CancellationToken token)
    {
        var draft = AnalogSelectionDraft;
        var keys = _analogKeyKeys.Where(key => key.IsSelected).ToArray();
        if (_analogSelectionBaseline is not { } baseline || draft == baseline || keys.Length == 0 ||
            keys.Any(key => !_confirmedAnalogKeys.Contains(key.KeyId)) ||
            !double.IsFinite(draft.Actuation) || draft.Actuation is < 0.1 or > 4 ||
            !double.IsFinite(draft.Sensitivity) || draft.Sensitivity is < 0.1 or > 4) return null;
        var revision = _analogSelectionRevision;
        return CaptureMouseApply(() => CanApplyAnalogKeySelection && revision == _analogSelectionRevision,
            () => (AnalogKeyActuationPointMillimeters, AnalogKeyRapidTriggerEnabled,
                AnalogKeyRapidTriggerSensitivityMillimeters) = draft,
            async cancellationToken =>
            {
                await ApplyAnalogKeyToKeysAsync(keys, cancellationToken);
                if (_operationSucceeded && revision == _analogSelectionRevision) _analogSelectionBaseline = draft;
            }, token);
    }

    internal Func<Task<string?>>? PrepareAnalogKeyAllApply(CancellationToken token)
    {
        var draft = AnalogSelectionDraft;
        if (!CanEditAnalogKey || _analogKeyKeys.Count == 0 ||
            !double.IsFinite(draft.Actuation) || draft.Actuation is < 0.1 or > 4 ||
            !double.IsFinite(draft.Sensitivity) || draft.Sensitivity is < 0.1 or > 4) return null;
        var keys = _analogKeyKeys.ToArray();
        return CaptureMouseApply(() => CanEditAnalogKey,
            () => (AnalogKeyActuationPointMillimeters, AnalogKeyRapidTriggerEnabled,
                AnalogKeyRapidTriggerSensitivityMillimeters) = draft,
            cancellationToken => ApplyAnalogKeyToKeysAsync(keys, cancellationToken), token);
    }

    internal Func<Task<string?>>? PrepareAnalogSnapTapAutoApply(CancellationToken token)
    {
        if (_confirmedAnalogSnapTap is not { } confirmed || !CanApplyAnalogSnapTap) return null;
        var enabled = AnalogSnapTapEnabled;
        var pairs = _analogSnapTapPairs.ToArray();
        if (pairs.Any(pair => pair.Key1 is null || pair.Key2 is null))
        {
            AnalogSnapTapStatusText = AppStrings.Text("SnapTapNeedTwoKeys");
            return null;
        }
        var draft = pairs.Select(pair => new RazerAnalogSnapTapPair(pair.Key1!.KeyId, pair.Key2!.KeyId, pair.Mode)).ToArray();
        if (enabled && draft.Length == 0)
        {
            AnalogSnapTapStatusText = AppStrings.Text("SnapTapNeedPairBeforeEnable");
            return null;
        }
        if (draft.Any(pair => pair.KeyId1 == pair.KeyId2))
        {
            AnalogSnapTapStatusText = AppStrings.Text("SnapTapDuplicateInPair");
            return null;
        }
        if (draft.Length > RazerAnalogKeyProtocol.MaxSnapTapPairs ||
            draft.SelectMany(pair => new[] { pair.KeyId1, pair.KeyId2 }).Distinct().Count() != draft.Length * 2)
        {
            AnalogSnapTapStatusText = AppStrings.Text("SnapTapDuplicateAcrossPairs");
            return null;
        }
        if (enabled == confirmed.Enabled && draft.SequenceEqual(confirmed.Pairs)) return null;
        return CaptureMouseApply(() => CanApplyAnalogSnapTap, () =>
        {
            AnalogSnapTapEnabled = enabled;
            _analogSnapTapPairs.Clear();
            foreach (var pair in draft)
                _analogSnapTapPairs.Add(new AnalogSnapTapPairViewModel(_analogSnapTapKeyChoices,
                    _analogSnapTapKeyChoices.First(key => key.KeyId == pair.KeyId1),
                    _analogSnapTapKeyChoices.First(key => key.KeyId == pair.KeyId2), pair.Mode));
        }, ApplyAnalogSnapTapAsync, token);
    }
}
