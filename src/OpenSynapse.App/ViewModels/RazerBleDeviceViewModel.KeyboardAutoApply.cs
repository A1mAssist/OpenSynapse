using Microsoft.UI.Xaml;
using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.App.ViewModels;

public sealed partial class RazerBleDeviceViewModel
{
    private (bool Enabled, int Mode)? _confirmedSmartIllumination;
    private bool? _confirmedSnapTap;
    private RazerBleDebounceState? _confirmedDebounce;

    internal object KeyboardAutoApplyContext => (this, _service, _getProfile?.Invoke(),
        _getProfile?.Invoke().ActiveProfileName, _service?.MappingProfileId);

    internal Func<Task<string?>>? PrepareSmartIlluminationAutoApply(CancellationToken token)
    {
        var draft = (SmartIlluminationEnabled, SelectedSmartIlluminationModeIndex);
        return SmartIlluminationVisibility != Visibility.Visible || _confirmedSmartIllumination is null ||
            draft == _confirmedSmartIllumination || SelectedSmartIlluminationModeIndex is < 0 or >= SmartIlluminationModeCount
            ? null : CaptureMouseApply(() => ApplySmartIlluminationAsync(draft, token));
    }

    internal Func<Task<string?>>? PrepareKeysAutoApply(CancellationToken token)
    {
        var snapTap = SnapTapVisibility == Visibility.Visible && _confirmedSnapTap is not null &&
            SnapTapEnabled != _confirmedSnapTap ? SnapTapEnabled : (bool?)null;
        RazerBleDebounceState? debounce = null;
        if (DebounceVisibility == Visibility.Visible)
        {
            if (_confirmedDebounce is null || !ValidMouseNumber(DebounceMake, 0, 255) ||
                !ValidMouseNumber(DebounceBreak, 0, 255)) return null;
            var draft = new RazerBleDebounceState((byte)Math.Round(DebounceMake), _debounceMakeMask,
                (byte)Math.Round(DebounceBreak), _debounceBreakMask);
            if (draft != _confirmedDebounce) debounce = draft;
        }
        if (snapTap is null && debounce is null) return null;
        var applySnapTap = snapTap is { } enabled ? CaptureMouseApply(() => ApplySnapTapAsync(enabled, token)) : null;
        var applyDebounce = debounce is { } value ? CaptureMouseApply(() => ApplyDebounceAsync(value, token)) : null;
        return async () =>
        {
            if (applySnapTap is not null && await applySnapTap() is { } error) return error;
            return applyDebounce is null ? null : await applyDebounce();
        };
    }
}
