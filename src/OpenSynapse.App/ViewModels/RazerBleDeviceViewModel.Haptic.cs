using Microsoft.UI.Xaml;
using OpenSynapse.Core.Devices;
using OpenSynapse.Core.Profiles;

namespace OpenSynapse.App.ViewModels;

public sealed partial class RazerBleDeviceViewModel
{
    private byte? _confirmedHapticStage;
    private byte? _loadedHapticProfile;
    public string HapticStageProfileText => _loadedHapticProfile is { } id ? DescribeProfile(id) : string.Empty;
    public Visibility HapticStageEntryVisibility => _device.ProductId == 0x00A9 ? Visibility.Visible : Visibility.Collapsed;

    public async Task LoadHapticStageAsync(CancellationToken token = default)
    {
        if (_service?.SupportsHapticStages != true) { HapticStageVisibility = Visibility.Collapsed; return; }
        var profileId = HapticProfileId;
        await LoadExtraAsync(() => _service.ReadHapticStageAsync(profileId, token), state =>
        {
            _loadedHapticProfile = profileId;
            _confirmedHapticStage = state.Active;
            MaxHapticStage = state.Max;
            SelectedHapticStage = state.Active;
            HapticStageVisibility = Visibility.Visible;
            OnPropertyChanged(nameof(HapticStageProfileText));
        }, () => { _confirmedHapticStage = null; HapticStageVisibility = Visibility.Collapsed; });
    }

    internal Func<Task<string?>>? PrepareHapticAutoApply(CancellationToken token)
    {
        if (_loadedHapticProfile != HapticProfileId || _confirmedHapticStage is null ||
            !ValidMouseNumber(SelectedHapticStage, 1, MaxHapticStage) || SelectedHapticStage != Math.Round(SelectedHapticStage)) return null;
        var stage = (byte)SelectedHapticStage;
        if (stage == _confirmedHapticStage) return null;
        var profileId = HapticProfileId;
        return CaptureMouseApply(() => ApplyHapticStageAsync(stage, profileId, token));
    }

    [ProfileSetting("ble.haptic-level", DeviceCategory.Mouse)]
    public Task ApplyHapticStageAsync(CancellationToken token = default) =>
        ApplyHapticStageAsync(checked((byte)SelectedHapticStage), HapticProfileId, token);

    [NotProfileScoped]
    private async Task<bool> ApplyHapticStageAsync(byte stage, byte profileId, CancellationToken token)
    {
        if (_service is null || _loadedHapticProfile != profileId || !BeginOperation()) return false;
        try
        {
            var applied = await _service.ApplyHapticStageAsync(stage, profileId, async (value, ct) =>
            {
                if (!await PersistMouseAsync(mouse => { mouse.HapticLevel = value; mouse.HapticProfileId = profileId; }, ct))
                    throw new IOException(AppStrings.Text("ViperSettingAppliedProfileSaveFailed"));
            }, token);
            _confirmedHapticStage = applied;
            SelectedHapticStage = applied;
            ErrorText = string.Empty;
            OperationStatusText = AppStrings.Text("BlackSharkSaved");
            return true;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { return false; }
        catch (Exception error) when (IsExpectedException(error))
        {
            ErrorText = error.Message;
            OperationStatusText = string.Empty;
            return false;
        }
        finally { EndOperation(); }
    }
}
