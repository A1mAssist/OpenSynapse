namespace OpenSynapse.App.ViewModels;

public sealed partial class RazerKeyboardViewModel
{
    private bool _restoringProfile;

    internal object KeyboardAutoApplyContext => (this, _getProfile?.Invoke(), _getProfile?.Invoke().ActiveProfileName);

    internal Func<Task<string?>>? PrepareGameModeAutoApply(CancellationToken token) =>
        PrepareKeyboardAutoApply(GameModeEnabled, _appliedGameMode, _hasGameMode,
            value => GameModeEnabled = value, ApplyGameModeAsync, token);

    internal Func<Task<string?>>? PreparePollingAutoApply(CancellationToken token) =>
        PrepareKeyboardAutoApply(SelectedPollingIndex, _appliedPollingIndex, _hasPolling,
            value => SelectedPollingIndex = value, ApplyPollingAsync, token);

    internal Func<Task<string?>>? PrepareBrightnessAutoApply(CancellationToken token) =>
        double.IsFinite(Brightness) && Brightness is >= 0 and <= 100
            ? PrepareKeyboardAutoApply((int)Math.Round(Brightness), _appliedBrightness, _hasBrightness,
                value => Brightness = value, ApplyBrightnessAsync, token)
            : null;

    private Func<Task<string?>>? PrepareKeyboardAutoApply<T>(T draft, T confirmed, bool supported,
        Action<T> restoreDraft, Func<CancellationToken, Task> apply, CancellationToken token)
    {
        if (!supported || IsBusy || _restoringProfile || EqualityComparer<T>.Default.Equals(draft, confirmed))
            return null;
        var profile = _getProfile?.Invoke();
        var profileName = profile?.ActiveProfileName;
        return async () =>
        {
            if (token.IsCancellationRequested || IsBusy || _restoringProfile ||
                !ReferenceEquals(profile, _getProfile?.Invoke()) || profileName != profile?.ActiveProfileName)
                return AppStrings.Text("SettingAutoApplyCancelled");
            restoreDraft(draft);
            await apply(token);
            if (!string.IsNullOrEmpty(ErrorText)) return ErrorText;
            return token.IsCancellationRequested || string.IsNullOrEmpty(OperationStatusText)
                ? AppStrings.Text("SettingAutoApplyCancelled") : null;
        };
    }
}
